import 'dart:async';
import 'dart:ffi';
import 'dart:io';

import 'package:ffi/ffi.dart';
import 'package:path/path.dart' as p;

import '../models/windows_dsd_device.dart';
import 'bass_ffi_bindings.dart';

class WindowsDsdBackendException implements Exception {
  const WindowsDsdBackendException(this.message);

  final String message;

  @override
  String toString() => message;
}

class WindowsDsdBackendService {
  static const int _bassAsioThread = 1;
  static const int _bassAttribDsdRate = 0x14001;

  BassFfiBindings? _bindings;
  HStream _streamHandle = 0;
  bool _isPlaying = false;
  bool _completedFired = false;
  bool _isInitialized = false;
  bool _usingRawDsd = false;
  Duration _duration = Duration.zero;
  Duration _position = Duration.zero;
  String? _loadedPath;
  int? _activeDeviceId;
  String? _activeDeviceName;
  Timer? _pollTimer;

  final StreamController<Duration> _positionController =
      StreamController<Duration>.broadcast();
  final StreamController<bool> _playingController =
      StreamController<bool>.broadcast();
  final StreamController<void> _completedController =
      StreamController<void>.broadcast();

  Stream<Duration> get positionStream => _positionController.stream;
  Stream<bool> get playingStream => _playingController.stream;
  Stream<void> get completedStream => _completedController.stream;

  bool get isAvailable => _bindings != null;
  bool get isPlaying => _isPlaying;
  bool get isLoaded => _streamHandle != 0;
  bool get usingRawDsd => _usingRawDsd;
  Duration get duration => _duration;
  Duration get position => _position;
  String? get loadedPath => _loadedPath;
  int? get activeDeviceId => _activeDeviceId;
  String? get activeDeviceName => _activeDeviceName;

  String get outputModeLabel =>
      _usingRawDsd ? 'DSD Native (ASIO)' : 'DSD over PCM (DoP)';

  Future<bool> ensureInitialized() async {
    if (_bindings != null) {
      return true;
    }

    final bassPath = _resolveLibraryPath('bass.dll');
    final bassDsdPath = _resolveLibraryPath('bassdsd.dll');
    final bassAsioPath = _resolveLibraryPath('bassasio.dll');
    if (bassPath == null || bassDsdPath == null || bassAsioPath == null) {
      return false;
    }

    _bindings = BassFfiBindings.open(
      bassPath: bassPath,
      bassDsdPath: bassDsdPath,
      bassAsioPath: bassAsioPath,
    );
    return true;
  }

  Future<void> loadTrack(
    String path, {
    bool preferNativeDsd = true,
    int asioDevice = -1,
  }) async {
    final initialized = await ensureInitialized();
    if (!initialized || _bindings == null) {
      throw const WindowsDsdBackendException(
        'BASS DSD runtime is not available.',
      );
    }

    await stop();
    _disposeCurrentStream();

    _loadedPath = path;
    _duration = Duration.zero;
    _position = Duration.zero;
    _usingRawDsd = false;
    _activeDeviceId = null;
    _activeDeviceName = null;

    final bass = _bindings!;
    if (!_isInitialized) {
      final initOk = bass.bassInit(0, 48000, 0, 0, nullptr);
      if (initOk == 0) {
        throw WindowsDsdBackendException(
          'BASS_Init failed with code ${bass.bassErrorGetCode()}.',
        );
      }
      _isInitialized = true;
    }

    final rawHandle = _createDsdStream(
      path,
      flags: bassDsdRaw | bassStreamDecode,
    );
    if (rawHandle == 0) {
      throw WindowsDsdBackendException(
        'BASS_DSD_StreamCreateFile failed with code ${bass.bassErrorGetCode()}.',
      );
    }

    final channelInfo = calloc<BassChannelInfo>();
    final dsdRate = calloc<Float>();
    try {
      if (bass.bassChannelGetInfo(rawHandle, channelInfo) == 0) {
        throw WindowsDsdBackendException(
          'BASS_ChannelGetInfo failed with code ${bass.bassErrorGetCode()}.',
        );
      }
      final attrOk = bass.bassChannelGetAttribute(
        rawHandle,
        _bassAttribDsdRate,
        dsdRate,
      );
      final rawRate = attrOk != 0 ? dsdRate.value.toDouble() : 0.0;

      final asioInitOk = bass.bassAsioInit(asioDevice, _bassAsioThread);
      if (asioInitOk == 0) {
        bass.bassStreamFree(rawHandle);
        throw WindowsDsdBackendException(
          'BASS_ASIO_Init failed with code ${bass.bassAsioErrorGetCode()}.',
        );
      }
      _captureActiveAsioDeviceMetadata();

      var effectiveHandle = rawHandle;
      var effectiveRate = rawRate;
      var channelCount = channelInfo.ref.chans;
      _usingRawDsd = preferNativeDsd && bass.bassAsioSetDsd(1) != 0;

      if (!_usingRawDsd) {
        bass.bassAsioSetDsd(0);
        bass.bassStreamFree(rawHandle);
        effectiveHandle = _createDsdStream(
          path,
          flags: bassDsdDop | bassSampleFloat | bassStreamDecode,
        );
        if (effectiveHandle == 0) {
          bass.bassAsioFree();
          throw WindowsDsdBackendException(
            'BASS_DSD_StreamCreateFile (DoP) failed with code ${bass.bassErrorGetCode()}.',
          );
        }
        if (bass.bassChannelGetInfo(effectiveHandle, channelInfo) == 0) {
          bass.bassStreamFree(effectiveHandle);
          bass.bassAsioFree();
          throw WindowsDsdBackendException(
            'BASS_ChannelGetInfo (DoP) failed with code ${bass.bassErrorGetCode()}.',
          );
        }
        effectiveRate = channelInfo.ref.freq.toDouble();
        channelCount = channelInfo.ref.chans;
      }

      if (effectiveRate <= 0) {
        bass.bassStreamFree(effectiveHandle);
        bass.bassAsioFree();
        throw const WindowsDsdBackendException(
          'Resolved DSD output rate is invalid.',
        );
      }

      if (bass.bassAsioSetRate(effectiveRate) == 0) {
        bass.bassStreamFree(effectiveHandle);
        bass.bassAsioFree();
        throw WindowsDsdBackendException(
          'BASS_ASIO_SetRate failed with code ${bass.bassAsioErrorGetCode()}.',
        );
      }

      if (bass.bassAsioChannelEnableBass(0, 0, effectiveHandle, 1) == 0) {
        bass.bassStreamFree(effectiveHandle);
        bass.bassAsioFree();
        throw WindowsDsdBackendException(
          'BASS_ASIO_ChannelEnableBASS failed with code ${bass.bassAsioErrorGetCode()}.',
        );
      }

      if (channelCount == 1) {
        bass.bassAsioChannelEnableMirror(1, 0, 0);
      }

      final lengthBytes = bass.bassChannelGetLength(
        effectiveHandle,
        bassPosByte,
      );
      _duration = _secondsToDuration(
        bass.bassChannelBytes2Seconds(effectiveHandle, lengthBytes),
      );
      _streamHandle = effectiveHandle;
      _emitState();
    } finally {
      calloc.free(channelInfo);
      calloc.free(dsdRate);
    }
  }

  Future<void> play() async {
    if (_bindings == null || _streamHandle == 0) {
      return;
    }
    final started = _bindings!.bassAsioStart(0, 0);
    if (started == 0) {
      throw WindowsDsdBackendException(
        'BASS_ASIO_Start failed with code ${_bindings!.bassAsioErrorGetCode()}.',
      );
    }
    _isPlaying = true;
    _startPolling();
    _emitState();
  }

  Future<void> pause() async {
    if (_bindings == null || _streamHandle == 0) {
      return;
    }
    _bindings!.bassAsioStop();
    _isPlaying = false;
    _emitState();
  }

  Future<void> stop() async {
    if (_bindings == null || _streamHandle == 0) {
      return;
    }
    _bindings!.bassAsioStop();
    _isPlaying = false;
    await seek(Duration.zero);
    _emitState();
  }

  Future<void> seek(Duration position) async {
    if (_bindings == null || _streamHandle == 0) {
      return;
    }
    final bytes = _bindings!.bassChannelSeconds2Bytes(
      _streamHandle,
      position.inMicroseconds / Duration.microsecondsPerSecond,
    );
    _bindings!.bassChannelSetPosition(_streamHandle, bytes, bassPosByte);
    _position = position;
    _positionController.add(_position);
  }

  Future<void> dispose() async {
    _pollTimer?.cancel();
    _pollTimer = null;
    _disposeCurrentStream();
    if (_bindings != null && _isInitialized) {
      // _disposeCurrentStream already called bassAsioFree when a stream was active.
      _bindings!.bassFree();
    }
    _isInitialized = false;
    _bindings = null;
    await _positionController.close();
    await _playingController.close();
    await _completedController.close();
  }

  int bassErrorCode() => _bindings?.bassErrorGetCode() ?? 0;
  int asioErrorCode() => _bindings?.bassAsioErrorGetCode() ?? 0;

  Future<List<WindowsDsdDevice>> listAvailableDevices() async {
    final initialized = await ensureInitialized();
    if (!initialized || _bindings == null) {
      return const <WindowsDsdDevice>[];
    }

    final bass = _bindings!;
    final devices = <WindowsDsdDevice>[];
    final currentDevice = bass.bassAsioGetDevice();

    for (var deviceId = 0; deviceId < 32; deviceId += 1) {
      final infoPtr = calloc<BassAsioDeviceInfo>();
      try {
        final ok = bass.bassAsioGetDeviceInfo(deviceId, infoPtr);
        if (ok == 0) {
          break;
        }

        bass.bassAsioSetDevice(deviceId);
        final asioInfoPtr = calloc<BassAsioInfo>();
        try {
          final infoOk = bass.bassAsioGetInfo(asioInfoPtr);
          if (infoOk == 0) {
            continue;
          }

          final supportsNativeDsd = _supportsNativeDsdOutput(asioInfoPtr.ref);
          devices.add(
            WindowsDsdDevice(
              id: deviceId,
              name: infoPtr.ref.name.cast<Utf8>().toDartString(),
              driver: infoPtr.ref.driver.cast<Utf8>().toDartString(),
              inputChannels: asioInfoPtr.ref.inputs,
              outputChannels: asioInfoPtr.ref.outputs,
              supportsNativeDsd: supportsNativeDsd,
            ),
          );
        } finally {
          calloc.free(asioInfoPtr);
        }
      } finally {
        calloc.free(infoPtr);
      }
    }

    if (currentDevice >= 0) {
      bass.bassAsioSetDevice(currentDevice);
    }
    return devices;
  }

  void _disposeCurrentStream() {
    _pollTimer?.cancel();
    _pollTimer = null;
    if (_bindings != null && _streamHandle != 0) {
      _bindings!.bassAsioStop();
      _bindings!.bassAsioFree();
      _bindings!.bassStreamFree(_streamHandle);
    }
    _streamHandle = 0;
    _isPlaying = false;
    _completedFired = false;
    _usingRawDsd = false;
    _duration = Duration.zero;
    _position = Duration.zero;
    _activeDeviceId = null;
    _activeDeviceName = null;
  }

  bool _supportsNativeDsdOutput(BassAsioInfo info) {
    if (info.outputs <= 0) {
      return false;
    }
    final bass = _bindings;
    if (bass == null) {
      return false;
    }

    final channelInfoPtr = calloc<BassAsioChannelInfo>();
    try {
      for (var channel = 0; channel < info.outputs; channel += 1) {
        final ok = bass.bassAsioChannelGetInfo(0, channel, channelInfoPtr);
        if (ok == 0) {
          continue;
        }
        final format = channelInfoPtr.ref.format;
        if (format == 32 || format == 33) {
          return true;
        }
      }
    } finally {
      calloc.free(channelInfoPtr);
    }
    return false;
  }

  int _createDsdStream(String path, {required int flags}) {
    final utf16Path = path.toNativeUtf16();
    try {
      return _bindings!.bassDsdStreamCreateFile(
        0,
        utf16Path,
        0,
        0,
        flags | bassUnicode,
        0,
      );
    } finally {
      calloc.free(utf16Path);
    }
  }

  void _startPolling() {
    _pollTimer?.cancel();
    _pollTimer = Timer.periodic(const Duration(milliseconds: 100), (_) {
      final bindings = _bindings;
      if (bindings == null || _streamHandle == 0) {
        return;
      }
      final positionBytes = bindings.bassChannelGetPosition(
        _streamHandle,
        bassPosByte,
      );
      _position = _secondsToDuration(
        bindings.bassChannelBytes2Seconds(_streamHandle, positionBytes),
      );
      _positionController.add(_position);

      final activeState = bindings.bassChannelIsActive(_streamHandle);
      final playing = activeState == bassActivePlaying;
      if (_isPlaying != playing) {
        _isPlaying = playing;
        _playingController.add(_isPlaying);
      }

      if (_duration > Duration.zero && _position >= _duration) {
        if (!_completedFired) {
          _completedFired = true;
          _completedController.add(null);
        }
      }
    });
  }

  void _captureActiveAsioDeviceMetadata() {
    final bass = _bindings;
    if (bass == null) {
      _activeDeviceId = null;
      _activeDeviceName = null;
      return;
    }

    final deviceId = bass.bassAsioGetDevice();
    if (deviceId < 0) {
      _activeDeviceId = null;
      _activeDeviceName = null;
      return;
    }

    _activeDeviceId = deviceId;
    final infoPtr = calloc<BassAsioDeviceInfo>();
    try {
      final ok = bass.bassAsioGetDeviceInfo(deviceId, infoPtr);
      if (ok == 0) {
        _activeDeviceName = 'ASIO $deviceId';
        return;
      }
      final namePtr = infoPtr.ref.name;
      _activeDeviceName = namePtr.address == 0
          ? 'ASIO $deviceId'
          : namePtr.cast<Utf8>().toDartString();
    } finally {
      calloc.free(infoPtr);
    }
  }

  void _emitState() {
    _positionController.add(_position);
    _playingController.add(_isPlaying);
  }

  Duration _secondsToDuration(double seconds) {
    if (seconds.isNaN || seconds.isInfinite || seconds <= 0) {
      return Duration.zero;
    }
    return Duration(
      microseconds: (seconds * Duration.microsecondsPerSecond).round(),
    );
  }

  String? _resolveLibraryPath(String fileName) {
    final executableDir = File(Platform.resolvedExecutable).parent.path;
    final searchRoots = <String>{
      executableDir,
      Directory.current.path,
      ..._ancestorDirectories(executableDir),
      ..._ancestorDirectories(Directory.current.path),
    };
    final candidates = <String>[];

    for (final root in searchRoots) {
      candidates.add(p.join(root, fileName));
      candidates.add(
        p.join(
          root,
          'native',
          'windows_dsd',
          'vendor',
          'bass24',
          'x64',
          fileName,
        ),
      );
      candidates.add(
        p.join(
          root,
          'native',
          'windows_dsd',
          'vendor',
          'bassdsd24',
          'x64',
          fileName,
        ),
      );
      candidates.add(
        p.join(
          root,
          'native',
          'windows_dsd',
          'vendor',
          'bassasio14',
          'x64',
          fileName,
        ),
      );
    }

    for (final candidate in candidates) {
      if (File(candidate).existsSync()) {
        return candidate;
      }
    }
    return null;
  }

  Iterable<String> _ancestorDirectories(String start) sync* {
    var current = p.normalize(start);
    for (var depth = 0; depth < 6; depth += 1) {
      final parent = p.dirname(current);
      if (parent == current) {
        break;
      }
      yield parent;
      current = parent;
    }
  }
}
