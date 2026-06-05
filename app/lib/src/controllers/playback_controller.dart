import 'dart:async';
import 'dart:io';
import 'dart:math';
import 'package:win32/win32.dart';

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:just_audio/just_audio.dart';
import 'package:just_audio_media_kit/just_audio_media_kit.dart';
import 'package:media_kit/media_kit.dart' as media_kit;
import 'package:path/path.dart' as p;
import 'package:shared_preferences/shared_preferences.dart';

import '../domain/playback_strategy.dart';
import '../models/audio_output_device.dart';
import '../models/audio_output_mode.dart';
import '../models/playback_backend_kind.dart';
import '../models/playback_mode.dart';
import '../models/track.dart';
import '../services/windows_dsd_backend_service.dart';
import '../state/playback_state.dart';

typedef PlaybackQueueTrackResolver =
    Future<Track?> Function(Track track, int index, {bool forceRefresh});
typedef PlaybackQueueTrackFailureHandler =
    void Function(Track track, String reason);

class PlaybackController extends StateNotifier<PlaybackState> {
  PlaybackController() : super(const PlaybackState()) {
    JustAudioMediaKit.nativeAudioRouteLogger = (message) {
      _debug('native.output => $message');
    };
    JustAudioMediaKit.nativeAudioDevicesListener = _handleNativeAudioDevices;
    JustAudioMediaKit.nativeSelectedAudioDeviceListener =
        _handleNativeSelectedAudioDevice;
    JustAudioMediaKit.preferredAudioDevice = state.audioOutputDeviceId;
    _applyAudioOutputModeToBackend(state.audioOutputMode);
    _initializeAudioDeviceProbe();
    _initializePlayer();
    _bindWindowsDsdBackendEvents();
    unawaited(_refreshWindowsDsdDevices());
    unawaited(_loadDeveloperMode());
    unawaited(_loadAudioRoutePreferences());
    unawaited(_loadFadePreferences());
  }

  late AudioPlayer _player;
  late final media_kit.Player _audioDeviceProbe;
  final WindowsDsdBackendService _windowsDsdBackend =
      WindowsDsdBackendService();
  final Random _random = Random();

  static const Set<String> _demoPlayableExtensions = {
    '.mp3',
    '.wav',
    '.flac',
    '.ogg',
    '.aac',
    '.m4a',
    '.mp4',
    '.dsf',
    '.dff',
  };
  static const Set<String> _remotePlayableSchemes = {'http', 'https', 'file'};

  static const String _prefDeveloperMode = 'debug.playbackDeveloperMode';
  static const String _prefAudioOutputDevice = 'audio.outputDevice';
  static const String _prefWindowsDsdDevice = 'audio.windowsDsdDevice';
  static const String _prefFadeEnabled = 'audio.fadeEnabled';
  static const String _prefFadeDurationMs = 'audio.fadeDurationMs';
  static const int _maxDebugLogs = 500;
  static const String _devLogDirName = 'PrismWave';
  static const String _devLogSubDir = 'logs';
  static const int _minFadeDurationMs = 100;
  static const int _maxFadeDurationMs = 1200;
  static const int _volumeFadeSteps = 6;

  StreamSubscription<PlayerState>? _playerStateSub;
  StreamSubscription<Duration>? _positionSub;
  StreamSubscription<Duration?>? _durationSub;
  StreamSubscription<int?>? _currentIndexSub;
  StreamSubscription<PlayerException>? _errorSub;
  StreamSubscription<List<media_kit.AudioDevice>>? _probeAudioDevicesSub;
  StreamSubscription<media_kit.AudioDevice>? _probeAudioDeviceSub;
  StreamSubscription<Duration>? _windowsDsdPositionSub;
  StreamSubscription<bool>? _windowsDsdPlayingSub;
  StreamSubscription<void>? _windowsDsdCompletedSub;
  String? _developerLogFilePath;
  String? _developerConsoleControlFilePath;
  bool _developerConsoleSpawned = false;

  int _sessionToken = 0;
  bool _autoAdvancing = false;
  bool _recoveringDecoderError = false;
  bool _recoveringAudioDeviceError = false;
  bool _currentTrackStartedByAutoAdvance = false;
  int _volumeRampToken = 0;
  int _decoderRecoveryCount = 0;
  DateTime _decoderRecoveryWindowStart = DateTime.fromMillisecondsSinceEpoch(0);
  int _decodeSkipCount = 0;
  DateTime _decodeSkipWindowStart = DateTime.fromMillisecondsSinceEpoch(0);
  ProcessingState? _lastProcessingState;
  bool? _lastPlayingState;
  PlaybackQueueTrackResolver? _queueTrackResolver;
  PlaybackQueueTrackFailureHandler? _queueTrackFailureHandler;
  final Set<String> _failedOnlineQueueTrackIds = <String>{};

  void setQueueTrackResolver(PlaybackQueueTrackResolver? resolver) {
    _queueTrackResolver = resolver;
  }

  void setQueueTrackFailureHandler(PlaybackQueueTrackFailureHandler? handler) {
    _queueTrackFailureHandler = handler;
  }

  void _initializePlayer() {
    JustAudioMediaKit.nativeMpvProperties = const {
      'cache-secs': '12',
      'cache-on-disk': 'no',
    };
    // Bypass just_audio's local proxy server for HTTP headers.
    // The proxy causes a race condition on first HITS open (mpv connects
    // before the proxy is ready). media_kit already passes httpHeaders to
    // mpv via http-header-fields, so the proxy is unnecessary.
    _player = AudioPlayer(useProxyForRequestHeaders: false);
    _bindPlayerEvents();
    _player.setVolume(
      _effectiveOutputVolumeForTrack(state.currentTrack, state.volume),
    );
    _syncKnownAudioDevicesFromBackend();
    unawaited(_syncNativeLoopMode());
  }

  void _initializeAudioDeviceProbe() {
    _audioDeviceProbe = media_kit.Player(
      configuration: const media_kit.PlayerConfiguration(
        title: 'PrismWave Device Probe',
      ),
    );
    _probeAudioDevicesSub = _audioDeviceProbe.stream.audioDevices.listen(
      _handleProbeAudioDevices,
    );
    _probeAudioDeviceSub = _audioDeviceProbe.stream.audioDevice.listen(
      _handleProbeAudioDevice,
    );
    unawaited(
      _refreshAudioDeviceProbe(
        mode: state.audioOutputMode,
        deviceId: state.audioOutputDeviceId,
      ),
    );
  }

  Future<void> _loadAudioRoutePreferences() async {
    final prefs = await SharedPreferences.getInstance();
    final restored = AudioOutputMode.fromId(
      prefs.getString(kPrefAudioOutputMode),
    );
    final restoredDevice = _normalizeAudioDeviceId(
      prefs.getString(_prefAudioOutputDevice),
    );
    JustAudioMediaKit.preferredAudioDevice = restoredDevice;
    _syncKnownAudioDevicesFromBackend();
    await _refreshAudioDeviceProbe(mode: restored, deviceId: restoredDevice);

    if (restored == state.audioOutputMode &&
        restoredDevice == state.audioOutputDeviceId) {
      return;
    }

    await _rebuildPlayerForAudioConfiguration(
      mode: restored,
      deviceId: restoredDevice,
    );
  }

  Future<void> _refreshWindowsDsdDevices() async {
    final prefs = await SharedPreferences.getInstance();
    final restoredDeviceId =
        prefs.getString(_prefWindowsDsdDevice)?.trim().isNotEmpty == true
        ? prefs.getString(_prefWindowsDsdDevice)!.trim()
        : 'auto';

    final runtimeAvailable = await _windowsDsdBackend.ensureInitialized();
    final devices = await _windowsDsdBackend.listAvailableDevices();
    final hasSelected =
        restoredDeviceId == 'auto' ||
        devices.any((device) => device.id.toString() == restoredDeviceId);

    state = state.copyWith(
      windowsDsdAvailable: runtimeAvailable,
      selectedWindowsDsdDeviceId: hasSelected ? restoredDeviceId : 'auto',
      availableWindowsDsdDevices: devices,
    );

    if (!hasSelected && restoredDeviceId != 'auto') {
      await prefs.setString(_prefWindowsDsdDevice, 'auto');
    }
  }

  Future<void> setWindowsDsdDevice(String deviceId) async {
    final normalized = deviceId.trim().isEmpty ? 'auto' : deviceId.trim();
    if (normalized == state.selectedWindowsDsdDeviceId) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_prefWindowsDsdDevice, normalized);
      return;
    }

    state = state.copyWith(selectedWindowsDsdDeviceId: normalized);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_prefWindowsDsdDevice, normalized);
  }

  Future<void> _loadDeveloperMode() async {
    final prefs = await SharedPreferences.getInstance();
    final enabled = prefs.getBool(_prefDeveloperMode) ?? false;
    if (enabled && kReleaseMode) {
      await prefs.setBool(_prefDeveloperMode, false);
      if (state.developerMode) {
        state = state.copyWith(developerMode: false);
      }
      return;
    }
    if (enabled != state.developerMode) {
      state = state.copyWith(developerMode: enabled);
    }
    if (enabled) {
      await _enableDeveloperOutputs(openConsole: !kReleaseMode);
      _debug('Developer mode restored from settings.', force: true);
    }
  }

  Future<void> _loadFadePreferences() async {
    final prefs = await SharedPreferences.getInstance();
    final restoredEnabled =
        prefs.getBool(_prefFadeEnabled) ?? state.fadeEnabled;
    final restoredDuration = Duration(
      milliseconds: _normalizeFadeDurationMs(
        prefs.getInt(_prefFadeDurationMs) ?? state.fadeDuration.inMilliseconds,
      ),
    );

    if (restoredEnabled == state.fadeEnabled &&
        restoredDuration == state.fadeDuration) {
      return;
    }

    state = state.copyWith(
      fadeEnabled: restoredEnabled,
      fadeDuration: restoredDuration,
    );
  }

  Future<void> setDeveloperMode(bool enabled) async {
    if (enabled == state.developerMode) return;

    if (enabled) {
      state = state.copyWith(developerMode: true);
      final prefs = await SharedPreferences.getInstance();
      await prefs.setBool(_prefDeveloperMode, true);
      await _enableDeveloperOutputs(openConsole: true);
      _debug('Developer mode enabled by user.', force: true);
      return;
    }

    _debug('Developer mode disabled by user.', force: true);
    state = state.copyWith(developerMode: false);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_prefDeveloperMode, false);
    await _disableDeveloperOutputs();
  }

  Future<void> setAudioOutputMode(AudioOutputMode mode) async {
    await _setAudioOutputModeInternal(mode, persist: true);
  }

  Future<void> setFadeEnabled(bool enabled) async {
    if (enabled == state.fadeEnabled) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setBool(_prefFadeEnabled, enabled);
      return;
    }

    state = state.copyWith(fadeEnabled: enabled);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_prefFadeEnabled, enabled);

    if (!enabled) {
      _cancelPendingVolumeRamp();
      await _setPlayerVolumeSafely(state.volume);
    }

    _debug('audio.fade setting -> enabled=$enabled', force: true);
  }

  Future<void> setFadeDuration(Duration duration) async {
    final normalized = Duration(
      milliseconds: _normalizeFadeDurationMs(duration.inMilliseconds),
    );
    if (normalized == state.fadeDuration) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setInt(_prefFadeDurationMs, normalized.inMilliseconds);
      return;
    }

    state = state.copyWith(fadeDuration: normalized);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setInt(_prefFadeDurationMs, normalized.inMilliseconds);
    _debug(
      'audio.fade setting -> durationMs=${normalized.inMilliseconds}',
      force: true,
    );
  }

  Future<void> setAudioOutputDevice(String deviceId) async {
    final normalized = _normalizeAudioDeviceId(deviceId);
    if (normalized == state.audioOutputDeviceId) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_prefAudioOutputDevice, normalized);
      return;
    }

    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_prefAudioOutputDevice, normalized);
    await _refreshAudioDeviceProbe(
      mode: state.audioOutputMode,
      deviceId: normalized,
    );
    await _rebuildPlayerForAudioConfiguration(
      mode: state.audioOutputMode,
      deviceId: normalized,
    );
  }

  Future<void> _setAudioOutputModeInternal(
    AudioOutputMode mode, {
    required bool persist,
  }) async {
    if (mode == state.audioOutputMode) {
      if (persist) {
        final prefs = await SharedPreferences.getInstance();
        await prefs.setString(kPrefAudioOutputMode, mode.id);
      }
      return;
    }

    if (persist) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(kPrefAudioOutputMode, mode.id);
    }
    await _refreshAudioDeviceProbe(
      mode: mode,
      deviceId: state.audioOutputDeviceId,
    );
    await _rebuildPlayerForAudioConfiguration(
      mode: mode,
      deviceId: state.audioOutputDeviceId,
    );
  }

  void clearDebugLogs() {
    state = state.copyWith(debugLogs: const []);
  }

  void appendDeveloperLog(String message, {bool force = false}) {
    _debug(message, force: force);
  }

  void _applyAudioOutputModeToBackend(AudioOutputMode mode) {
    switch (mode) {
      case AudioOutputMode.compatibility:
        JustAudioMediaKit.preferWasapi = true;
        JustAudioMediaKit.preferWasapiExclusive = false;
        JustAudioMediaKit.fallbackToWasapiShared = true;
        return;
      case AudioOutputMode.wasapiShared:
        JustAudioMediaKit.preferWasapi = true;
        JustAudioMediaKit.preferWasapiExclusive = false;
        JustAudioMediaKit.fallbackToWasapiShared = true;
        return;
      case AudioOutputMode.wasapiExclusive:
        JustAudioMediaKit.preferWasapi = true;
        JustAudioMediaKit.preferWasapiExclusive = true;
        JustAudioMediaKit.fallbackToWasapiShared = true;
        return;
    }
  }

  Future<void> _rebuildPlayerForAudioConfiguration({
    required AudioOutputMode mode,
    required String deviceId,
    bool forcePlay = false,
  }) async {
    final previous = state;
    final hadPlaylist = previous.currentPlaylist.isNotEmpty;
    final wasPlaying = previous.isPlaying;
    final oldPlayer = _player;
    final restorePosition = previous.currentTime;
    final restoreIndex =
        previous.currentIndex >= 0 &&
            previous.currentIndex < previous.currentPlaylist.length
        ? previous.currentIndex
        : 0;

    _newSession();
    final normalizedDeviceId = _normalizeAudioDeviceId(deviceId);
    JustAudioMediaKit.preferredAudioDevice = normalizedDeviceId;
    final effectiveMode = _resolveEffectiveAudioOutputMode(
      mode,
      normalizedDeviceId,
    );
    state = state.copyWith(
      audioOutputMode: mode,
      audioOutputDeviceId: normalizedDeviceId,
      isLoading: hadPlaylist,
      isPlaying: false,
      clearError: true,
    );

    _player = oldPlayer;
    await _disposeCurrentPlayerInstance();

    _applyAudioOutputModeToBackend(effectiveMode);
    _initializePlayer();

    _debug(
      'audio.route switched -> requested=${mode.name}, '
      'effective=${effectiveMode.name}, device=$normalizedDeviceId',
      force: true,
    );

    if (!hadPlaylist) {
      state = state.copyWith(
        audioOutputMode: mode,
        audioOutputDeviceId: normalizedDeviceId,
        isLoading: false,
        isPlaying: false,
        clearError: true,
      );
      return;
    }

    final token = _newSession();
    final playlist = previous.currentPlaylist;
    final track = playlist[restoreIndex];

    state = state.copyWith(
      audioOutputMode: mode,
      audioOutputDeviceId: normalizedDeviceId,
      currentPlaylist: playlist,
      currentTrack: track,
      currentIndex: restoreIndex,
      currentTime: restorePosition,
      duration: Duration.zero,
      isLoading: true,
      clearError: true,
    );

    try {
      await _loadTrackSource(
        track,
        initialPosition: restorePosition,
        preload: true,
      );
      if (!_isSessionActive(token)) return;
      await _syncNativeLoopMode();
      if (!_isSessionActive(token)) return;

      if (forcePlay || wasPlaying) {
        await _setPlayerVolumeSafelyForTrack(state.volume, track: track);
        await _player.play();
      } else {
        await _setPlayerVolumeSafelyForTrack(state.volume, track: track);
        await _player.pause();
      }
      if (!_isSessionActive(token)) return;

      state = state.copyWith(
        audioOutputMode: mode,
        audioOutputDeviceId: normalizedDeviceId,
        isLoading: false,
        currentTime: restorePosition,
        clearError: true,
      );
    } catch (error) {
      if (!_isSessionActive(token)) return;
      state = state.copyWith(
        audioOutputMode: mode,
        audioOutputDeviceId: normalizedDeviceId,
        isLoading: false,
        error: 'Switch audio route failed: $error',
      );
      _debug('audio.route reload failed -> $error', force: true);
    }
  }

  Future<void> _disposeCurrentPlayerInstance() async {
    await _playerStateSub?.cancel();
    await _positionSub?.cancel();
    await _durationSub?.cancel();
    await _currentIndexSub?.cancel();
    await _errorSub?.cancel();
    _playerStateSub = null;
    _positionSub = null;
    _durationSub = null;
    _currentIndexSub = null;
    _errorSub = null;
    await _player.dispose();
  }

  Future<void> _recreatePlayerForExclusiveHandoff({
    required int expectedToken,
    required String reason,
  }) async {
    _debug(
      'exclusive handoff -> recreating native player. reason=$reason',
      force: true,
    );

    final oldPlayer = _player;
    _player = oldPlayer;
    await _disposeCurrentPlayerInstance();
    if (!_isSessionActive(expectedToken)) return;

    await Future<void>.delayed(const Duration(milliseconds: 90));
    if (!_isSessionActive(expectedToken)) return;

    _applyAudioOutputModeToBackend(state.audioOutputMode);
    _initializePlayer();
    if (!_isSessionActive(expectedToken)) return;

    await _setPlayerVolumeSafelyForTrack(
      state.volume,
      track: state.currentTrack,
    );
    await _syncNativeLoopMode();
    _debug('exclusive handoff -> fresh player ready.', force: true);
  }

  void _bindPlayerEvents() {
    unawaited(_playerStateSub?.cancel());
    unawaited(_positionSub?.cancel());
    unawaited(_durationSub?.cancel());
    unawaited(_currentIndexSub?.cancel());
    unawaited(_errorSub?.cancel());
    _lastProcessingState = null;
    _lastPlayingState = null;

    _playerStateSub = _player.playerStateStream.listen((playerState) async {
      if (playerState.processingState != _lastProcessingState ||
          playerState.playing != _lastPlayingState) {
        _lastProcessingState = playerState.processingState;
        _lastPlayingState = playerState.playing;
        _debug(
          'player.state => processing=${playerState.processingState.name}, '
          'playing=${playerState.playing}, loopMode=${_player.loopMode.name}',
        );
      }

      final shouldAutoAdvance =
          playerState.processingState == ProcessingState.completed &&
          state.hasTrack &&
          !_autoAdvancing;

      state = state.copyWith(
        isPlaying: playerState.playing,
        isLoading:
            playerState.processingState == ProcessingState.loading ||
            playerState.processingState == ProcessingState.buffering,
      );

      if (shouldAutoAdvance) {
        _autoAdvancing = true;
        _debug(
          'completed -> auto next. currentIndex=${state.currentIndex}, '
          'playlistLength=${state.currentPlaylist.length}',
        );
        try {
          await next(fromAutoEnded: true);
        } finally {
          _autoAdvancing = false;
        }
      }
    });

    _positionSub = _player.positionStream.listen((position) {
      state = state.copyWith(currentTime: position);
    });

    _durationSub = _player.durationStream.listen((duration) {
      state = state.copyWith(duration: duration ?? Duration.zero);
    });

    _currentIndexSub = _player.currentIndexStream.listen((_) {});

    _errorSub = _player.errorStream.listen((error) {
      if (_recoveringAudioDeviceError) {
        _debug(
          'player.error suppressed during audio-device recovery: '
          '[${error.code}] ${error.message}',
        );
        return;
      }

      if (_recoveringDecoderError) {
        _debug(
          'player.error suppressed during recovery: [${error.code}] ${error.message}',
        );
        return;
      }

      final message = 'Playback error [${error.code}]: ${error.message}';
      _debug(
        'player.error => $message, autoAdvancing=$_autoAdvancing, '
        'loopMode=${_player.loopMode.name}',
        force: true,
      );

      if (_shouldSkipTrackAfterDecodeError(error)) {
        _notifyQueuedTrackPlaybackFailure(message);
        unawaited(_skipTrackAfterDecodeError(trigger: message));
        return;
      }

      if (_shouldTreatDecodeErrorAsTrackCompletion(error)) {
        _notifyQueuedTrackPlaybackFailure(message);
        unawaited(_handleDecodeErrorAsTrackCompletion(trigger: message));
        return;
      }

      if (_shouldRecoverFromDecodeError(error)) {
        _notifyQueuedTrackPlaybackFailure(message);
        unawaited(_attemptDecodeRecovery(trigger: message));
        return;
      }

      if (_shouldRecoverFromAudioDeviceError(error)) {
        unawaited(_recoverToAutoAudioDevice(trigger: message));
        return;
      }

      if (_autoAdvancing) {
        _debug('Transient auto-advance error ignored at UI layer.');
        return;
      }

      state = state.copyWith(isLoading: false, error: message);
    });
  }

  Future<void> playFromPlaylist(
    Track track,
    List<Track> playlist, {
    bool includeUnplayableInQueue = false,
  }) async {
    await _playFromContext(
      track,
      playlist,
      includeUnplayableInQueue: includeUnplayableInQueue,
    );
  }

  Future<void> playFromLibrary(Track track, List<Track> libraryTracks) async {
    await _playFromContext(track, libraryTracks);
  }

  Future<void> playFromCurrentQueue(Track track) async {
    final index = state.currentPlaylist.indexWhere(
      (item) => item.id == track.id,
    );
    if (index < 0) return;
    await _playIndex(index);
  }

  PlaybackSessionSnapshot captureSessionSnapshot() {
    return PlaybackSessionSnapshot(
      playlist: List<Track>.from(state.currentPlaylist, growable: false),
      currentTrack: state.currentTrack,
      currentIndex: state.currentIndex,
      currentTime: state.currentTime,
      playbackMode: state.playbackMode,
      wasPlaying: state.isPlaying,
    );
  }

  Future<void> playStandaloneTrack(
    Track track, {
    Duration initialPosition = Duration.zero,
    bool autoplay = true,
  }) async {
    if (!_isPlayableTrack(track)) {
      state = state.copyWith(error: _unsupportedTrackMessage(track));
      return;
    }

    final token = _newSession();
    state = state.copyWith(
      currentPlaylist: const [],
      currentTrack: track,
      currentIndex: -1,
      currentTime: initialPosition,
      duration: Duration.zero,
      isLoading: true,
      clearError: true,
    );
    await _syncNativeLoopMode();

    await _loadPlaylistAndPlay(
      playlist: [track],
      index: 0,
      expectedToken: token,
      errorPrefix: 'Play failed',
      markAutoAdvancedTrack: false,
      autoplay: autoplay,
      initialPosition: initialPosition,
    );
  }

  Future<void> restoreSession(PlaybackSessionSnapshot snapshot) async {
    final playablePlaylist = snapshot.playlist
        .where(_isPlayableTrack)
        .toList(growable: false);
    final restoreTrack = snapshot.currentTrack;

    if (playablePlaylist.isEmpty && restoreTrack == null) {
      await stopAndClear();
      state = state.copyWith(
        playbackMode: snapshot.playbackMode,
        clearError: true,
      );
      return;
    }

    if (playablePlaylist.isEmpty && restoreTrack != null) {
      state = state.copyWith(
        playbackMode: snapshot.playbackMode,
        clearError: true,
      );
      await playStandaloneTrack(
        restoreTrack,
        initialPosition: snapshot.currentTime,
        autoplay: snapshot.wasPlaying,
      );
      return;
    }

    final fallbackIndex = snapshot.currentIndex.clamp(
      0,
      playablePlaylist.length - 1,
    );
    final preferredTrackId = restoreTrack?.id;
    final restoreIndex = preferredTrackId == null
        ? fallbackIndex
        : (() {
            final matchedIndex = playablePlaylist.indexWhere(
              (track) => track.id == preferredTrackId,
            );
            return matchedIndex >= 0 ? matchedIndex : fallbackIndex;
          })();

    final token = _newSession();
    state = state.copyWith(
      playbackMode: snapshot.playbackMode,
      currentPlaylist: playablePlaylist,
      currentTrack: playablePlaylist[restoreIndex],
      currentIndex: restoreIndex,
      currentTime: snapshot.currentTime,
      duration: Duration.zero,
      isLoading: true,
      clearError: true,
    );
    await _syncNativeLoopMode();

    await _loadPlaylistAndPlay(
      playlist: playablePlaylist,
      index: restoreIndex,
      expectedToken: token,
      errorPrefix: 'Restore playback failed',
      markAutoAdvancedTrack: false,
      autoplay: snapshot.wasPlaying,
      initialPosition: snapshot.currentTime,
    );
  }

  Future<void> stopAndClear({bool useFade = false}) async {
    final token = _newSession();
    if (state.backendKind == PlaybackBackendKind.windowsDsd) {
      try {
        await _windowsDsdBackend.stop();
      } catch (_) {
        // Keep stop resilient if the backend is already idle.
      }
      if (!_isSessionActive(token)) return;
      state = state.copyWith(
        currentPlaylist: const [],
        currentTrack: null,
        currentIndex: -1,
        currentTime: Duration.zero,
        duration: Duration.zero,
        isLoading: false,
        isPlaying: false,
        windowsDsdOutputModeLabel: null,
        windowsDsdActiveDeviceName: null,
        windowsDsdFallbackReason: null,
        backendKind: PlaybackBackendKind.mediaKit,
        clearError: true,
      );
      return;
    }
    if (useFade) {
      await _fadeOutCurrentTrack(
        expectedToken: token,
        duration: _configuredFadeDuration,
        reason: 'stop',
      );
    }
    if (!_isSessionActive(token)) return;
    try {
      await _player.stop();
    } catch (_) {
      // Keep stop resilient even if backend is already idle.
    }
    if (!_isSessionActive(token)) return;
    await _setPlayerVolumeSafely(state.volume);

    state = state.copyWith(
      currentPlaylist: const [],
      currentTrack: null,
      currentIndex: -1,
      currentTime: Duration.zero,
      duration: Duration.zero,
      isLoading: false,
      isPlaying: false,
      windowsDsdOutputModeLabel: null,
      windowsDsdActiveDeviceName: null,
      windowsDsdFallbackReason: null,
      clearError: true,
    );
  }

  Future<void> _playFromContext(
    Track track,
    List<Track> playlist, {
    bool includeUnplayableInQueue = false,
  }) async {
    if (playlist.isEmpty) return;
    if (!_isPlayableTrack(track)) {
      state = state.copyWith(error: _unsupportedTrackMessage(track));
      return;
    }

    final playablePlaylist = includeUnplayableInQueue
        ? List<Track>.from(playlist, growable: false)
        : playlist.where(_isPlayableTrack).toList(growable: false);
    if (playablePlaylist.isEmpty) {
      state = state.copyWith(
        error:
            'No playable tracks found in selected context for current demo backend.',
      );
      return;
    }

    final index = playablePlaylist.indexWhere((item) => item.id == track.id);
    if (index < 0) {
      state = state.copyWith(
        error:
            'Selected track cannot be found in playable playlist for demo backend.',
      );
      return;
    }

    final rotatedPlaylist = _rotatePlaylistToSelectedFirst(
      playablePlaylist,
      startIndex: index,
    );

    _debug(
      'playFromContext -> selectedIndex=$index, playlistLength=${playablePlaylist.length}, '
      'queueStartsWith="${rotatedPlaylist.first.title}", '
      'track="${track.title}", remote=${track.isRemote}, '
      'ext=${p.extension(track.path).toLowerCase()}, '
      'outputMode=${state.audioOutputMode.name}',
      force: true,
    );

    final token = _newSession();
    state = state.copyWith(
      currentPlaylist: rotatedPlaylist,
      currentTrack: rotatedPlaylist.first,
      currentIndex: 0,
      currentTime: Duration.zero,
      duration: Duration.zero,
      isLoading: true,
      clearError: true,
    );
    await _syncNativeLoopMode();

    await _loadPlaylistAndPlay(
      playlist: rotatedPlaylist,
      index: 0,
      expectedToken: token,
      errorPrefix: 'Play failed',
      markAutoAdvancedTrack: false,
    );
  }

  List<Track> _rotatePlaylistToSelectedFirst(
    List<Track> playlist, {
    required int startIndex,
  }) {
    if (playlist.isEmpty) return const [];
    if (startIndex <= 0 || startIndex >= playlist.length) {
      return List<Track>.from(playlist, growable: false);
    }

    return <Track>[...playlist.skip(startIndex), ...playlist.take(startIndex)];
  }

  Future<void> togglePlayPause() async {
    if (!state.hasTrack) return;

    if (state.backendKind == PlaybackBackendKind.windowsDsd) {
      if (state.isPlaying) {
        await _windowsDsdBackend.pause();
        state = state.copyWith(isPlaying: false, clearError: true);
        return;
      }

      if (state.duration > Duration.zero &&
          state.currentTime >= state.duration) {
        await _windowsDsdBackend.seek(Duration.zero);
      }
      await _windowsDsdBackend.play();
      state = state.copyWith(isPlaying: true, clearError: true);
      return;
    }

    _debug(
      'togglePlayPause -> playing=${_player.playing}, '
      'processing=${_player.processingState.name}',
      force: true,
    );

    if (_player.playing) {
      await _pauseWithFade();
      return;
    }

    if (_player.processingState == ProcessingState.completed) {
      await _restartCurrentTrack();
      return;
    }

    if (_player.processingState == ProcessingState.idle &&
        state.currentPlaylist.isNotEmpty) {
      _debug('togglePlayPause -> idle reload via queue index.', force: true);
      await _playIndex(state.currentIndex < 0 ? 0 : state.currentIndex);
      return;
    }

    try {
      await _syncNativeLoopMode();
      await _resumeWithFade();
      state = state.copyWith(clearError: true);
    } catch (error) {
      state = state.copyWith(isLoading: false, error: 'Play failed: $error');
    }
  }

  Future<void> seekTo(Duration position) async {
    if (!state.hasTrack) return;
    if (state.backendKind == PlaybackBackendKind.windowsDsd) {
      await _windowsDsdBackend.seek(position);
      state = state.copyWith(currentTime: position);
      return;
    }
    await _player.seek(position);
  }

  Future<void> setVolume(double volume) async {
    final normalized = volume.clamp(0.0, 1.0);
    _cancelPendingVolumeRamp();
    if (state.backendKind == PlaybackBackendKind.windowsDsd) {
      state = state.copyWith(volume: normalized);
      return;
    }
    await _player.setVolume(normalized);
    state = state.copyWith(volume: normalized);
  }

  void setMode(PlaybackMode mode) {
    state = state.copyWith(playbackMode: mode);
    _debug('setMode -> ${mode.name}', force: true);
    unawaited(_syncNativeLoopMode());
  }

  void cycleMode() {
    final nextMode = PlaybackStrategy.cycleMode(state.playbackMode);
    state = state.copyWith(playbackMode: nextMode);
    _debug('cycleMode -> ${nextMode.name}', force: true);
    unawaited(_syncNativeLoopMode());
  }

  Future<void> replaceQueuePreservingCurrent(
    List<Track> playlist, {
    bool includeUnplayable = false,
  }) async {
    final currentTrack = state.currentTrack;
    if (currentTrack == null || playlist.isEmpty) return;

    final nextPlaylist = includeUnplayable
        ? List<Track>.from(playlist, growable: false)
        : playlist.where(_isPlayableTrack).toList(growable: false);
    if (nextPlaylist.isEmpty) return;

    final currentIndex = nextPlaylist.indexWhere(
      (track) => track.id == currentTrack.id,
    );
    if (currentIndex < 0) {
      _debug(
        'queue.replace-preserve skipped -> current track missing, '
        'incomingLength=${nextPlaylist.length}',
        force: true,
      );
      return;
    }

    state = state.copyWith(
      currentPlaylist: nextPlaylist,
      currentIndex: currentIndex,
      clearError: true,
    );
    _debug(
      'queue.replace-preserve -> length=${nextPlaylist.length}, '
      'currentIndex=$currentIndex, current="${currentTrack.title}"',
      force: true,
    );
  }

  void reorderQueue(int oldIndex, int newIndex) {
    final queue = [...state.currentPlaylist];
    if (queue.length <= 1 ||
        oldIndex < 0 ||
        oldIndex >= queue.length ||
        newIndex < 0 ||
        newIndex > queue.length) {
      return;
    }

    var normalizedNewIndex = newIndex;
    if (normalizedNewIndex > oldIndex) {
      normalizedNewIndex -= 1;
    }
    if (normalizedNewIndex == oldIndex ||
        normalizedNewIndex < 0 ||
        normalizedNewIndex >= queue.length) {
      return;
    }

    final moved = queue.removeAt(oldIndex);
    queue.insert(normalizedNewIndex, moved);

    final currentTrack = state.currentTrack;
    final currentIndex = currentTrack == null
        ? -1
        : queue.indexWhere((track) => track.id == currentTrack.id);

    state = state.copyWith(
      currentPlaylist: queue,
      currentIndex: currentIndex,
      clearError: true,
    );
    _debug(
      'queue.reorder -> from=$oldIndex, to=$normalizedNewIndex, '
      'currentIndex=$currentIndex',
      force: true,
    );
  }

  Future<void> removeFromQueueAt(int index) async {
    final queue = [...state.currentPlaylist];
    if (index < 0 || index >= queue.length) return;

    final removed = queue.removeAt(index);
    final removedCurrent = state.currentTrack?.id == removed.id;

    if (queue.isEmpty) {
      _newSession();
      try {
        await _player.stop();
      } catch (_) {
        // Keep queue removal resilient even if backend already stopped.
      }
      state = state.copyWith(
        currentPlaylist: const [],
        currentTrack: null,
        currentIndex: -1,
        currentTime: Duration.zero,
        duration: Duration.zero,
        isLoading: false,
        isPlaying: false,
        clearError: true,
      );
      _debug(
        'queue.remove -> removed last track "${removed.title}"',
        force: true,
      );
      return;
    }

    if (!removedCurrent) {
      final currentTrack = state.currentTrack;
      final currentIndex = currentTrack == null
          ? -1
          : queue.indexWhere((track) => track.id == currentTrack.id);
      state = state.copyWith(
        currentPlaylist: queue,
        currentIndex: currentIndex,
        clearError: true,
      );
      _debug(
        'queue.remove -> removed "${removed.title}", '
        'currentIndex=$currentIndex',
        force: true,
      );
      return;
    }

    final targetIndex = index.clamp(0, queue.length - 1);
    final token = _newSession();
    final shouldAutoplay = state.isPlaying;
    state = state.copyWith(
      currentPlaylist: queue,
      currentTrack: queue[targetIndex],
      currentIndex: targetIndex,
      currentTime: Duration.zero,
      duration: Duration.zero,
      isLoading: true,
      clearError: true,
    );
    await _syncNativeLoopMode();
    await _loadPlaylistAndPlay(
      playlist: queue,
      index: targetIndex,
      expectedToken: token,
      errorPrefix: 'Remove from queue failed',
      markAutoAdvancedTrack: false,
      autoplay: shouldAutoplay,
    );
  }

  Future<void> previous() async {
    if (!state.hasTrack || state.currentPlaylist.isEmpty) return;
    final prev = PlaybackStrategy.resolvePreviousIndex(
      playlistLength: state.currentPlaylist.length,
      currentIndex: state.currentIndex,
      mode: state.playbackMode,
      randomInt: _random.nextInt,
    );
    _debug('previous -> targetIndex=$prev', force: true);
    await _playIndex(prev);
  }

  Future<void> next({bool fromAutoEnded = false}) async {
    if (!state.hasTrack || state.currentPlaylist.isEmpty) return;
    final nextIndex = PlaybackStrategy.resolveNextIndex(
      playlistLength: state.currentPlaylist.length,
      currentIndex: state.currentIndex,
      mode: state.playbackMode,
      fromAutoEnded: fromAutoEnded,
      randomInt: _random.nextInt,
    );
    _debug(
      'next(fromAutoEnded=$fromAutoEnded) -> targetIndex=$nextIndex',
      force: true,
    );
    await _playIndex(nextIndex, causedByAutoAdvance: fromAutoEnded);
  }

  Future<void> _playIndex(
    int index, {
    bool causedByAutoAdvance = false,
    Set<int>? skippedUnplayableIndices,
  }) async {
    if (index < 0 || index >= state.currentPlaylist.length) return;
    var targetIndex = index;
    var targetTrack = state.currentPlaylist[targetIndex];
    var forceRefresh = _isFailedOnlineQueueTrack(targetTrack);

    if (!_isPlayableTrack(targetTrack) || forceRefresh) {
      final resolved = await _resolveQueuedTrackIfNeeded(
        index: targetIndex,
        track: targetTrack,
        forceRefresh: forceRefresh,
      );
      if (resolved != null) {
        _clearFailedOnlineQueueTrack(targetTrack);
        targetIndex = resolved.index;
        targetTrack = resolved.track;
        _clearFailedOnlineQueueTrack(targetTrack);
        forceRefresh = false;
      }
    }

    if (!_isPlayableTrack(targetTrack) || forceRefresh) {
      _debug(
        'playIndex skipped unresolved track -> index=$targetIndex, '
        'title="${targetTrack.title}", path=${targetTrack.path}',
        force: true,
      );

      final attempted = skippedUnplayableIndices ?? <int>{};
      attempted.add(targetIndex);
      if (causedByAutoAdvance &&
          attempted.length < state.currentPlaylist.length) {
        final nextIndex = _nextUnattemptedQueueIndex(targetIndex, attempted);
        if (nextIndex != null) {
          _debug(
            'playIndex unresolved auto-skip -> from=$targetIndex to=$nextIndex',
            force: true,
          );
          await _playIndex(
            nextIndex,
            causedByAutoAdvance: true,
            skippedUnplayableIndices: attempted,
          );
          return;
        }
      }

      state = state.copyWith(
        isLoading: false,
        error: _queuedUnplayableTrackMessage(targetTrack),
      );
      return;
    }

    if (targetIndex == state.currentIndex) {
      _debug(
        'playIndex -> same index($targetIndex), restart current.',
        force: true,
      );
      _currentTrackStartedByAutoAdvance = causedByAutoAdvance;
      await _restartCurrentTrack();
      return;
    }

    final token = _newSession();
    state = state.copyWith(
      currentIndex: targetIndex,
      currentTrack: targetTrack,
      currentTime: Duration.zero,
      duration: Duration.zero,
      isLoading: true,
      clearError: true,
    );
    await _syncNativeLoopMode();

    try {
      _debug(
        'playIndex -> reload target track directly. index=$targetIndex, '
        'title="${targetTrack.title}"',
        force: true,
      );
      await _loadPlaylistAndPlay(
        playlist: state.currentPlaylist,
        index: targetIndex,
        expectedToken: token,
        errorPrefix: 'Switch track failed',
        markAutoAdvancedTrack: causedByAutoAdvance,
      );
    } catch (error) {
      if (!_isSessionActive(token)) return;
      state = state.copyWith(
        isLoading: false,
        error: 'Switch track failed: $error',
      );
    }
  }

  Future<({Track track, int index})?> _resolveQueuedTrackIfNeeded({
    required int index,
    required Track track,
    bool forceRefresh = false,
  }) async {
    final resolver = _queueTrackResolver;
    if (resolver == null || !_looksLikeOnlineQueueTrack(track)) {
      return null;
    }

    _debug(
      'queue.resolve-on-demand.start -> index=$index, '
      'title="${track.title}", path=${track.path}, forceRefresh=$forceRefresh',
      force: true,
    );
    state = state.copyWith(isLoading: true, clearError: true);

    try {
      final resolved = await resolver(track, index, forceRefresh: forceRefresh);
      if (resolved == null || !_isPlayableTrack(resolved)) {
        _debug(
          'queue.resolve-on-demand.failed -> index=$index, '
          'title="${track.title}"',
          force: true,
        );
        return null;
      }

      final queue = List<Track>.from(state.currentPlaylist, growable: false);
      var targetIndex = index;
      if (targetIndex < 0 ||
          targetIndex >= queue.length ||
          queue[targetIndex].id != track.id) {
        targetIndex = queue.indexWhere((item) => item.id == track.id);
      }
      if (targetIndex < 0) {
        _debug(
          'queue.resolve-on-demand.stale -> title="${track.title}", '
          'resolvedPath=${resolved.path}',
          force: true,
        );
        return null;
      }

      queue[targetIndex] = resolved;
      final resolvingCurrent =
          state.currentTrack?.id == track.id ||
          state.currentIndex == targetIndex;
      var nextCurrentIndex = state.currentIndex;
      if (resolvingCurrent) {
        nextCurrentIndex = targetIndex;
      } else if (state.currentTrack != null) {
        final existingCurrentIndex = queue.indexWhere(
          (item) => item.id == state.currentTrack!.id,
        );
        if (existingCurrentIndex >= 0) nextCurrentIndex = existingCurrentIndex;
      }

      state = state.copyWith(
        currentPlaylist: queue,
        currentTrack: resolvingCurrent ? resolved : state.currentTrack,
        currentIndex: nextCurrentIndex,
        clearError: true,
      );
      _debug(
        'queue.resolve-on-demand.ok -> index=$targetIndex, '
        'title="${resolved.title}", remote=${resolved.isRemote}, '
        'forceRefresh=$forceRefresh',
        force: true,
      );
      return (track: resolved, index: targetIndex);
    } catch (error) {
      _debug(
        'queue.resolve-on-demand.error -> index=$index, '
        'title="${track.title}", error=$error',
        force: true,
      );
      return null;
    }
  }

  bool _isFailedOnlineQueueTrack(Track track) {
    if (!_looksLikeOnlineQueueTrack(track)) return false;
    return _failedOnlineQueueTrackIds.contains(track.id) ||
        _failedOnlineQueueTrackIds.contains(track.path);
  }

  void _markFailedOnlineQueueTrack(Track track) {
    if (!_looksLikeOnlineQueueTrack(track)) return;
    _failedOnlineQueueTrackIds.add(track.id);
    _failedOnlineQueueTrackIds.add(track.path);
  }

  void _clearFailedOnlineQueueTrack(Track track) {
    _failedOnlineQueueTrackIds.remove(track.id);
    _failedOnlineQueueTrackIds.remove(track.path);
  }

  int? _nextUnattemptedQueueIndex(int fromIndex, Set<int> attempted) {
    final length = state.currentPlaylist.length;
    if (length <= 1) return null;
    for (var offset = 1; offset < length; offset += 1) {
      final candidate = (fromIndex + offset) % length;
      if (!attempted.contains(candidate)) return candidate;
    }
    return null;
  }

  void _notifyQueuedTrackPlaybackFailure(String reason) {
    final track = state.currentTrack;
    final handler = _queueTrackFailureHandler;
    if (track == null ||
        handler == null ||
        !_looksLikeOnlineQueueTrack(track)) {
      return;
    }
    _debug(
      'queue.playback-failure -> title="${track.title}", '
      'path=${track.path}, reason=$reason',
      force: true,
    );
    _markFailedOnlineQueueTrack(track);
    handler(track, reason);
  }

  Future<void> _restartCurrentTrack() async {
    if (!state.hasTrack || state.currentPlaylist.isEmpty) return;

    final index = state.currentIndex < 0 ? 0 : state.currentIndex;
    final token = _newSession();
    state = state.copyWith(
      currentTime: Duration.zero,
      isLoading: true,
      clearError: true,
    );
    await _syncNativeLoopMode();

    final shouldRebuildForExclusiveRestart =
        state.audioOutputMode == AudioOutputMode.wasapiExclusive &&
        _player.processingState == ProcessingState.completed;
    final shouldReloadForIdleRestart =
        _player.processingState == ProcessingState.idle;

    try {
      await _fadeOutCurrentTrack(
        expectedToken: token,
        duration: _configuredFadeDuration,
        reason: 'restart-current',
      );

      if (shouldRebuildForExclusiveRestart || shouldReloadForIdleRestart) {
        _debug(
          'restartCurrentTrack -> reload current track. '
          'reason=${shouldReloadForIdleRestart ? 'idle-player' : 'exclusive-completed'}',
          force: true,
        );
        await _loadPlaylistAndPlay(
          playlist: state.currentPlaylist,
          index: index,
          expectedToken: token,
          errorPrefix: 'Failed to restart track',
          markAutoAdvancedTrack: _currentTrackStartedByAutoAdvance,
        );
        return;
      }

      _debug('restartCurrentTrack -> seek(0)+play, index=$index', force: true);
      await _player.seek(Duration.zero);
      if (!_isSessionActive(token)) return;

      await _resumeWithFade(expectedToken: token, reason: 'restart-current');
      if (!_isSessionActive(token)) return;

      state = state.copyWith(
        currentTime: Duration.zero,
        isLoading: false,
        clearError: true,
      );
    } on PlayerInterruptedException {
      if (!_isSessionActive(token)) return;
      state = state.copyWith(
        isLoading: false,
        error:
            'Track restart was interrupted by another playback request. Please retry.',
      );
    } catch (error) {
      _debug(
        'restart seek+play failed -> reload fallback. error=$error',
        force: true,
      );
      await _loadPlaylistAndPlay(
        playlist: state.currentPlaylist,
        index: index,
        expectedToken: token,
        errorPrefix: 'Failed to restart track',
        markAutoAdvancedTrack: _currentTrackStartedByAutoAdvance,
      );
    }
  }

  bool _shouldSkipTrackAfterDecodeError(PlayerException error) {
    final message = (error.message ?? '').toLowerCase();
    final looksLikeDecodeError =
        message.contains('decode') ||
        message.contains('decoding') ||
        message.contains('format');
    if (!looksLikeDecodeError) return false;
    if (_recoveringDecoderError) return false;
    if (state.playbackMode == PlaybackMode.single) return false;
    if (state.currentPlaylist.length <= 1) return false;

    final now = DateTime.now();
    if (now.difference(_decodeSkipWindowStart) > const Duration(seconds: 30)) {
      _decodeSkipWindowStart = now;
      _decodeSkipCount = 0;
    }

    _debug(
      'decode skip decision -> mode=${state.playbackMode.name}, '
      'playlistLength=${state.currentPlaylist.length}, '
      'autoAdvanced=$_currentTrackStartedByAutoAdvance, '
      'positionMs=${_player.position.inMilliseconds}',
      force: true,
    );

    return _decodeSkipCount < min(state.currentPlaylist.length, 8);
  }

  bool _shouldRecoverFromDecodeError(PlayerException error) {
    final message = (error.message ?? '').toLowerCase();
    final looksLikeDecodeError =
        message.contains('decode') ||
        message.contains('decoding') ||
        message.contains('format');
    if (!looksLikeDecodeError) return false;
    if (state.currentPlaylist.isEmpty || state.currentIndex < 0) return false;
    if (_recoveringDecoderError) return false;

    final now = DateTime.now();
    if (now.difference(_decoderRecoveryWindowStart) >
        const Duration(seconds: 30)) {
      _decoderRecoveryWindowStart = now;
      _decoderRecoveryCount = 0;
    }

    if (_decoderRecoveryCount >= 6) return false;

    // Decoder recovery is most useful in auto-advance / loop-one edge cases.
    // For FLAC we also allow recovery in normal playback because some files
    // fail at specific frames and can continue after a small seek.
    if (!_autoAdvancing &&
        _player.loopMode != LoopMode.one &&
        !_isCurrentTrackFlac()) {
      return false;
    }

    return true;
  }

  bool _shouldTreatDecodeErrorAsTrackCompletion(PlayerException error) {
    final message = (error.message ?? '').toLowerCase();
    final looksLikeDecodeError =
        message.contains('decode') ||
        message.contains('decoding') ||
        message.contains('format');
    if (!looksLikeDecodeError) return false;
    if (_recoveringDecoderError || _autoAdvancing) return false;
    if (!state.hasTrack || state.currentPlaylist.isEmpty) return false;

    final duration = _currentKnownTrackDuration();
    if (duration <= Duration.zero) return false;

    final position = _currentKnownTrackPosition();
    if (position <= Duration.zero) return false;

    final remaining = duration - position;
    final nearEndByTime = remaining <= const Duration(milliseconds: 1800);
    final nearEndByProgress =
        duration.inMilliseconds > 0 &&
        position.inMilliseconds >= (duration.inMilliseconds * 0.96).round();

    final shouldTreatAsCompleted = nearEndByTime || nearEndByProgress;
    if (shouldTreatAsCompleted) {
      _debug(
        'decode completion decision -> mode=${state.playbackMode.name}, '
        'positionMs=${position.inMilliseconds}, durationMs=${duration.inMilliseconds}, '
        'remainingMs=${remaining.inMilliseconds}',
        force: true,
      );
    }
    return shouldTreatAsCompleted;
  }

  Duration _currentKnownTrackDuration() {
    final playerDuration = _player.duration ?? Duration.zero;
    if (playerDuration > Duration.zero) return playerDuration;
    return state.duration;
  }

  Duration _currentKnownTrackPosition() {
    final playerPosition = _player.position;
    if (playerPosition > state.currentTime) return playerPosition;
    return state.currentTime;
  }

  Future<void> _handleDecodeErrorAsTrackCompletion({
    required String trigger,
  }) async {
    if (_autoAdvancing) return;

    _autoAdvancing = true;
    final position = _currentKnownTrackPosition();
    final duration = _currentKnownTrackDuration();
    _debug(
      'decode completion -> treating as auto next. '
      'mode=${state.playbackMode.name}, positionMs=${position.inMilliseconds}, '
      'durationMs=${duration.inMilliseconds}, trigger="$trigger"',
      force: true,
    );

    try {
      await next(fromAutoEnded: true);
    } finally {
      _autoAdvancing = false;
    }
  }

  bool _shouldRecoverFromAudioDeviceError(PlayerException error) {
    if (_recoveringAudioDeviceError) return false;
    if (_resolveNextAudioRouteFallbackStep() == null) return false;
    return _looksLikeAudioDeviceInitializationFailure(
      error.message ?? '$error',
    );
  }

  bool _looksLikeAudioDeviceInitializationFailure(String message) {
    final normalized = message.toLowerCase();
    if (normalized.contains('could not open/initialize audio device')) {
      return true;
    }
    if (normalized.contains('could not open or initialize audio device')) {
      return true;
    }
    if (normalized.contains('failed to initialize audio device')) {
      return true;
    }
    if (normalized.contains('audio device') &&
        normalized.contains('no sound')) {
      return true;
    }
    return false;
  }

  Future<void> _persistAudioRoutePreferences({
    required AudioOutputMode mode,
    required String deviceId,
  }) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(kPrefAudioOutputMode, mode.id);
    await prefs.setString(_prefAudioOutputDevice, deviceId);
  }

  ({AudioOutputMode mode, String deviceId, String description})?
  _resolveNextAudioRouteFallbackStep() {
    if (state.audioOutputDeviceId != AudioOutputDevice.auto.id) {
      return (
        mode: state.audioOutputMode,
        deviceId: AudioOutputDevice.auto.id,
        description: 'selected device -> auto',
      );
    }

    if (state.audioOutputMode == AudioOutputMode.wasapiExclusive) {
      return (
        mode: AudioOutputMode.wasapiShared,
        deviceId: AudioOutputDevice.auto.id,
        description: 'WASAPI exclusive -> WASAPI shared',
      );
    }

    if (state.audioOutputMode == AudioOutputMode.wasapiShared) {
      return (
        mode: AudioOutputMode.compatibility,
        deviceId: AudioOutputDevice.auto.id,
        description: 'WASAPI shared -> compatibility',
      );
    }

    return null;
  }

  bool _didAudioRouteRecoveryStepFail() {
    return _player.processingState == ProcessingState.idle;
  }

  Future<void> _recoverToAutoAudioDevice({required String trigger}) async {
    if (_recoveringAudioDeviceError) return;

    _recoveringAudioDeviceError = true;

    try {
      for (var attempt = 0; attempt < 3; attempt += 1) {
        final step = _resolveNextAudioRouteFallbackStep();
        if (step == null) {
          state = state.copyWith(isLoading: false, error: trigger);
          _debug(
            'audio.route recovery exhausted -> no further fallback. '
            'mode=${state.audioOutputMode.name}, device=${state.audioOutputDeviceId}',
            force: true,
          );
          return;
        }

        _debug(
          'audio.route recovery -> ${step.description}. '
          'from mode=${state.audioOutputMode.name}, device=${state.audioOutputDeviceId}, '
          'to mode=${step.mode.name}, device=${step.deviceId}, trigger="$trigger"',
          force: true,
        );

        state = state.copyWith(isLoading: true, clearError: true);
        await _persistAudioRoutePreferences(
          mode: step.mode,
          deviceId: step.deviceId,
        );
        await _refreshAudioDeviceProbe(
          mode: step.mode,
          deviceId: step.deviceId,
        );
        await _rebuildPlayerForAudioConfiguration(
          mode: step.mode,
          deviceId: step.deviceId,
          forcePlay: true,
        );

        await Future<void>.delayed(const Duration(milliseconds: 260));

        if (!_didAudioRouteRecoveryStepFail()) {
          _debug(
            'audio.route recovery -> ${step.description} succeeded. '
            'mode=${state.audioOutputMode.name}, device=${state.audioOutputDeviceId}, '
            'processing=${_player.processingState.name}',
            force: true,
          );
          return;
        }

        _debug(
          'audio.route recovery -> ${step.description} still failed. '
          'processing=${_player.processingState.name}, playing=${_player.playing}',
          force: true,
        );
      }

      state = state.copyWith(isLoading: false, error: trigger);
      _debug(
        'audio.route recovery exhausted -> maximum fallback attempts reached.',
        force: true,
      );
    } catch (error) {
      _debug('audio.device recovery failed -> $error', force: true);
      state = state.copyWith(
        isLoading: false,
        error: 'Audio device recovery failed: $error',
      );
    } finally {
      _recoveringAudioDeviceError = false;
    }
  }

  Future<void> _skipTrackAfterDecodeError({required String trigger}) async {
    _recoveringDecoderError = true;
    _decodeSkipCount += 1;
    final failedIndex = state.currentIndex < 0 ? 0 : state.currentIndex;
    final failedTrackTitle = state.currentTrack?.title ?? 'Unknown Track';

    _debug(
      'decode skip #$_decodeSkipCount triggered by "$trigger" at index=$failedIndex, '
      'title="$failedTrackTitle"',
      force: true,
    );

    state = state.copyWith(isLoading: true, clearError: true);

    try {
      await _player.stop();
      await next(fromAutoEnded: true);
    } catch (error) {
      state = state.copyWith(
        isLoading: false,
        error: 'Decode skip failed: $error',
      );
    } finally {
      _recoveringDecoderError = false;
    }
  }

  Future<void> _attemptDecodeRecovery({required String trigger}) async {
    _recoveringDecoderError = true;
    _decoderRecoveryCount += 1;

    final index = state.currentIndex < 0 ? 0 : state.currentIndex;
    final token = _newSession();
    _debug(
      'decoder recovery #$_decoderRecoveryCount triggered by "$trigger" at index=$index',
      force: true,
    );

    state = state.copyWith(
      isLoading: true,
      currentTime: Duration.zero,
      clearError: true,
    );

    try {
      final currentPosition = _player.position;
      final currentDuration = _player.duration ?? Duration.zero;
      final canSoftSeekRecover =
          _isCurrentTrackFlac() &&
          currentPosition > Duration.zero &&
          currentDuration > const Duration(seconds: 2) &&
          currentPosition <
              (currentDuration - const Duration(milliseconds: 1200));

      if (canSoftSeekRecover) {
        final seekTarget = currentPosition + const Duration(milliseconds: 900);
        _debug(
          'decoder soft recovery -> seek to ${seekTarget.inMilliseconds}ms '
          '(from ${currentPosition.inMilliseconds}ms)',
          force: true,
        );
        await _player.seek(seekTarget);
        if (!_isSessionActive(token)) return;
        await _player.play();
        if (!_isSessionActive(token)) return;
        state = state.copyWith(isLoading: false, clearError: true);
        return;
      }

      await _player.stop();
      if (!_isSessionActive(token)) return;

      await _loadPlaylistAndPlay(
        playlist: state.currentPlaylist,
        index: index,
        expectedToken: token,
        errorPrefix: 'Decoder recovery failed',
        markAutoAdvancedTrack: _currentTrackStartedByAutoAdvance,
      );
    } catch (error) {
      if (!_isSessionActive(token)) return;
      state = state.copyWith(
        isLoading: false,
        error: 'Decoder recovery failed: $error',
      );
    } finally {
      _recoveringDecoderError = false;
    }
  }

  Future<void> _loadPlaylistAndPlay({
    required List<Track> playlist,
    required int index,
    required int expectedToken,
    required String errorPrefix,
    required bool markAutoAdvancedTrack,
    bool autoplay = true,
    Duration initialPosition = Duration.zero,
  }) async {
    _debug(
      'native.output.requested => mode=${state.audioOutputMode.name}, '
      'preferWasapi=${JustAudioMediaKit.preferWasapi}, '
      'exclusive=${JustAudioMediaKit.preferWasapiExclusive}, '
      'fallbackToShared=${JustAudioMediaKit.fallbackToWasapiShared}',
      force: true,
    );
    try {
      final track = playlist[index];
      final dsdHandled = await _tryLoadWindowsDsdTrack(
        track: track,
        expectedToken: expectedToken,
        autoplay: autoplay,
        initialPosition: initialPosition,
      );
      if (dsdHandled || !_isSessionActive(expectedToken)) return;

      if (state.backendKind == PlaybackBackendKind.windowsDsd) {
        try {
          await _windowsDsdBackend.stop();
        } catch (_) {
          // Ignore backend stop failures during backend switching.
        }
        state = state.copyWith(
          backendKind: PlaybackBackendKind.mediaKit,
          windowsDsdOutputModeLabel: null,
          windowsDsdActiveDeviceName: null,
          windowsDsdFallbackReason: null,
        );
      }

      if (!_isSessionActive(expectedToken)) return;
      await _fadeOutCurrentTrack(
        expectedToken: expectedToken,
        duration: _configuredFadeDuration,
        reason: 'track-load',
      );
      if (!_isSessionActive(expectedToken)) return;

      if (state.audioOutputMode == AudioOutputMode.wasapiExclusive) {
        await _recreatePlayerForExclusiveHandoff(
          expectedToken: expectedToken,
          reason:
              'reload track index=$index title="${track.title}" after managed handoff',
        );
      } else {
        _debug('loadPlaylistAndPlay -> stop existing player before reload.');
        try {
          await _player.stop();
        } catch (_) {
          // Some backends may already be idle/completed; keep reload resilient.
        }
      }
      if (!_isSessionActive(expectedToken)) return;
      _debug(
        'loadTrackSource(managed-playlist) -> index=$index, playlistLength=${playlist.length}, '
        'title="${track.title}", remote=${track.isRemote}, '
        'source=${_describeTrackSource(track)}, ext=${p.extension(track.path).toLowerCase()}',
      );
      _debug('loadPlaylistAndPlay -> begin loadTrackSource');
      await _loadTrackSource(
        track,
        initialPosition: initialPosition,
        preload: true,
      );
      if (!_isSessionActive(expectedToken)) return;
      _debug('loadPlaylistAndPlay -> loadTrackSource completed');
      await _syncNativeLoopMode();
      if (!_isSessionActive(expectedToken)) return;

      if (autoplay) {
        _debug('loadPlaylistAndPlay -> begin play');
        if (_shouldUseNearLosslessDsdPath(track)) {
          await _setPlayerVolumeSafelyForTrack(state.volume, track: track);
          if (!_isSessionActive(expectedToken)) return;
          await _player.play();
        } else {
          await _resumeWithFade(
            expectedToken: expectedToken,
            reason: 'track-load',
          );
        }
        if (!_isSessionActive(expectedToken)) return;
        _debug('loadPlaylistAndPlay -> play completed');
      } else {
        _cancelPendingVolumeRamp();
        await _setPlayerVolumeSafelyForTrack(state.volume, track: track);
        try {
          await _player.pause();
        } catch (_) {
          // Keep paused reload resilient.
        }
        if (!_isSessionActive(expectedToken)) return;
        _debug('loadPlaylistAndPlay -> loaded without autoplay', force: true);
      }

      _currentTrackStartedByAutoAdvance = markAutoAdvancedTrack;
      state = state.copyWith(
        isLoading: false,
        isPlaying: autoplay ? state.isPlaying : false,
        windowsDsdOutputModeLabel: null,
        windowsDsdActiveDeviceName: null,
        windowsDsdFallbackReason: null,
        backendKind: PlaybackBackendKind.mediaKit,
        clearError: true,
      );
      _debug('loadPlaylistAndPlay success.');
    } on PlayerInterruptedException {
      if (!_isSessionActive(expectedToken)) return;
      _currentTrackStartedByAutoAdvance = false;
      state = state.copyWith(
        isLoading: false,
        error:
            '$errorPrefix: request interrupted by another playback action. Please retry.',
      );
    } catch (error) {
      if (!_isSessionActive(expectedToken)) return;
      final track = (index >= 0 && index < playlist.length)
          ? playlist[index]
          : state.currentTrack;
      final detailedMessage =
          '$errorPrefix: ${track?.title ?? 'Unknown Track'} ($error)';
      if (!_recoveringAudioDeviceError &&
          _resolveNextAudioRouteFallbackStep() != null &&
          _looksLikeAudioDeviceInitializationFailure('$error')) {
        await _recoverToAutoAudioDevice(trigger: detailedMessage);
        return;
      }
      _currentTrackStartedByAutoAdvance = false;
      state = state.copyWith(isLoading: false, error: detailedMessage);
      _debug(
        '$errorPrefix -> ${track?.title ?? 'Unknown Track'} | $error',
        force: true,
      );
    }
  }

  Future<void> _loadTrackSource(
    Track track, {
    Duration initialPosition = Duration.zero,
    bool preload = true,
  }) async {
    if (track.isRemote) {
      final uri = Uri.tryParse(track.playbackSource);
      if (uri != null && uri.scheme.toLowerCase() == 'file') {
        await _player.setFilePath(
          uri.toFilePath(windows: Platform.isWindows),
          initialPosition: initialPosition,
          preload: preload,
        );
        return;
      }
      final headers = <String, String>{
        'User-Agent':
            'PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)',
        ...?track.playbackHeaders,
      };
      await _player.setUrl(
        track.playbackSource,
        headers: headers,
        initialPosition: initialPosition,
        preload: preload,
      );
      return;
    }

    await _player.setFilePath(
      track.path,
      initialPosition: initialPosition,
      preload: preload,
    );
  }

  void _bindWindowsDsdBackendEvents() {
    unawaited(_windowsDsdPositionSub?.cancel() ?? Future<void>.value());
    unawaited(_windowsDsdPlayingSub?.cancel() ?? Future<void>.value());
    unawaited(_windowsDsdCompletedSub?.cancel() ?? Future<void>.value());

    _windowsDsdPositionSub = _windowsDsdBackend.positionStream.listen((
      position,
    ) {
      if (state.backendKind != PlaybackBackendKind.windowsDsd) {
        return;
      }
      state = state.copyWith(currentTime: position);
    });

    _windowsDsdPlayingSub = _windowsDsdBackend.playingStream.listen((playing) {
      if (state.backendKind != PlaybackBackendKind.windowsDsd) {
        return;
      }
      state = state.copyWith(isPlaying: playing, isLoading: false);
    });

    _windowsDsdCompletedSub = _windowsDsdBackend.completedStream.listen((
      _,
    ) async {
      if (state.backendKind != PlaybackBackendKind.windowsDsd ||
          !state.hasTrack ||
          _autoAdvancing) {
        return;
      }
      _autoAdvancing = true;
      try {
        await next(fromAutoEnded: true);
      } finally {
        _autoAdvancing = false;
      }
    });
  }

  Future<bool> _tryLoadWindowsDsdTrack({
    required Track track,
    required int expectedToken,
    required bool autoplay,
    required Duration initialPosition,
  }) async {
    if (!Platform.isWindows || !_isDsdTrack(track)) {
      return false;
    }

    try {
      if (_player.playing || _player.processingState != ProcessingState.idle) {
        await _player.stop();
      }
    } catch (_) {
      // Ignore legacy backend stop failures during DSD handoff.
    }
    if (!_isSessionActive(expectedToken)) return true;

    try {
      final selectedDsdDevice = state.selectedWindowsDsdDeviceId == 'auto'
          ? -1
          : int.tryParse(state.selectedWindowsDsdDeviceId) ?? -1;
      await _windowsDsdBackend.loadTrack(
        track.path,
        asioDevice: selectedDsdDevice,
      );
      if (!_isSessionActive(expectedToken)) {
        await _windowsDsdBackend.stop();
        return true;
      }

      if (initialPosition > Duration.zero) {
        await _windowsDsdBackend.seek(initialPosition);
      }

      if (autoplay) {
        await _windowsDsdBackend.play();
      } else {
        await _windowsDsdBackend.pause();
      }
      if (!_isSessionActive(expectedToken)) {
        await _windowsDsdBackend.stop();
        return true;
      }

      state = state.copyWith(
        currentTime: initialPosition,
        duration: _windowsDsdBackend.duration,
        isLoading: false,
        isPlaying: autoplay,
        windowsDsdOutputModeLabel: _windowsDsdBackend.outputModeLabel,
        windowsDsdActiveDeviceName: _windowsDsdBackend.activeDeviceName,
        windowsDsdFallbackReason: null,
        backendKind: PlaybackBackendKind.windowsDsd,
        clearError: true,
      );
      _debug(
        'windows.dsd -> loaded "${track.title}" '
        '(raw=${_windowsDsdBackend.usingRawDsd})',
        force: true,
      );
      return true;
    } catch (error) {
      _debug('windows.dsd fallback -> ${track.title} | $error', force: true);
      state = state.copyWith(
        backendKind: PlaybackBackendKind.mediaKit,
        windowsDsdOutputModeLabel: null,
        windowsDsdActiveDeviceName: null,
        windowsDsdFallbackReason: _describeWindowsDsdFallback(error),
      );
      return false;
    }
  }

  String _describeWindowsDsdFallback(Object error) {
    final message = error.toString();
    if (!state.windowsDsdAvailable) {
      return 'Windows DSD backend runtime is unavailable.';
    }
    if (state.availableWindowsDsdDevices.isEmpty) {
      return 'No ASIO device is available for the Windows DSD backend.';
    }
    if (message.contains('BASS_ASIO_Init failed')) {
      return state.selectedWindowsDsdDeviceId == 'auto'
          ? 'The default ASIO device could not be initialized.'
          : 'The selected ASIO device could not be initialized.';
    }
    if (message.contains('BASS_ASIO_SetRate failed')) {
      return 'The ASIO device rejected the requested DSD output rate.';
    }
    if (message.contains('BASS_ASIO_ChannelEnableBASS failed')) {
      return 'The ASIO device could not bind the DSD stream.';
    }
    if (message.contains('BASS_DSD_StreamCreateFile failed') ||
        message.contains('BASS_DSD_StreamCreateFile (DoP) failed')) {
      return 'The Windows DSD backend could not open this DSF/DFF file.';
    }
    return message;
  }

  bool _isPlayableTrack(Track track) {
    if (track.isRemote) {
      final uri = Uri.tryParse(track.playbackSource);
      if (uri == null) return false;
      final scheme = uri.scheme.toLowerCase();
      if (!_remotePlayableSchemes.contains(scheme)) {
        return false;
      }
      if (scheme == 'file') {
        return true;
      }
      return uri.host.isNotEmpty;
    }

    final extension = p.extension(track.path).toLowerCase();
    return _demoPlayableExtensions.contains(extension);
  }

  bool _isDsdTrack(Track? track) {
    if (track == null || track.isRemote) return false;
    final extension = p.extension(track.path).toLowerCase();
    return extension == '.dsf' || extension == '.dff';
  }

  bool _looksLikeOnlineQueueTrack(Track track) {
    final uri = Uri.tryParse(track.path);
    if (uri == null) return false;
    return uri.scheme.toLowerCase() == 'online';
  }

  String _queuedUnplayableTrackMessage(Track track) {
    if (_looksLikeOnlineQueueTrack(track)) {
      return 'This online track could not be resolved to a playable source.';
    }
    return _unsupportedTrackMessage(track);
  }

  String _unsupportedTrackMessage(Track track) {
    if (track.isRemote) {
      return 'This remote audio source is not supported in current demo backend.';
    }
    return 'This file format is not playable in current demo backend: ${p.extension(track.path)}';
  }

  String _describeTrackSource(Track track) {
    if (track.isRemote) {
      final uri = Uri.tryParse(track.playbackSource);
      if (uri == null) return track.playbackSource;
      return '${uri.scheme}://${uri.host}${uri.path}';
    }
    return track.path;
  }

  int _newSession() {
    _cancelPendingVolumeRamp();
    _sessionToken += 1;
    return _sessionToken;
  }

  bool _isSessionActive(int token) => token == _sessionToken;

  int _normalizeFadeDurationMs(int milliseconds) {
    return milliseconds.clamp(_minFadeDurationMs, _maxFadeDurationMs);
  }

  Duration get _configuredFadeDuration => Duration(
    milliseconds: _normalizeFadeDurationMs(state.fadeDuration.inMilliseconds),
  );

  void _cancelPendingVolumeRamp() {
    _volumeRampToken += 1;
  }

  bool _canFadeCurrentTrack() {
    if (!state.hasTrack) return false;
    if (_shouldUseNearLosslessDsdPath(state.currentTrack)) return false;
    if (!state.fadeEnabled) return false;
    if (state.volume <= 0) return false;
    if (!_player.playing) return false;
    return _player.processingState == ProcessingState.ready ||
        _player.processingState == ProcessingState.buffering;
  }

  bool _shouldUseNearLosslessDsdPath(Track? track) {
    return _isDsdTrack(track);
  }

  Future<void> _pauseWithFade() async {
    if (!state.fadeEnabled ||
        _shouldUseNearLosslessDsdPath(state.currentTrack)) {
      await _player.pause();
      return;
    }

    final expectedToken = _sessionToken;
    await _fadePlayerVolume(
      from: _player.volume,
      to: 0,
      duration: _configuredFadeDuration,
      expectedToken: expectedToken,
      reason: 'pause',
    );
    if (!_isSessionActive(expectedToken)) return;
    await _player.pause();
    if (!_isSessionActive(expectedToken)) return;
    await _setPlayerVolumeSafely(state.volume);
  }

  Future<void> _resumeWithFade({
    int? expectedToken,
    String reason = 'resume',
  }) async {
    final effectiveToken = expectedToken ?? _sessionToken;
    if (_shouldUseNearLosslessDsdPath(state.currentTrack)) {
      await _setPlayerVolumeSafelyForTrack(
        state.volume,
        track: state.currentTrack,
      );
      if (!_isSessionActive(effectiveToken)) return;
      await _player.play();
      return;
    }
    final targetVolume = state.volume.clamp(0.0, 1.0);
    if (targetVolume <= 0) {
      await _player.play();
      return;
    }

    if (!state.fadeEnabled) {
      await _setPlayerVolumeSafely(targetVolume);
      if (!_isSessionActive(effectiveToken)) return;
      await _player.play();
      return;
    }

    await _setPlayerVolumeSafely(0);
    if (!_isSessionActive(effectiveToken)) return;

    await _player.play();
    if (!_isSessionActive(effectiveToken)) return;

    await _fadePlayerVolume(
      from: 0,
      to: targetVolume,
      duration: _configuredFadeDuration,
      expectedToken: effectiveToken,
      reason: '$reason-in',
    );
  }

  Future<void> _fadeOutCurrentTrack({
    required int expectedToken,
    required Duration duration,
    required String reason,
  }) async {
    if (!_canFadeCurrentTrack()) return;

    final startVolume = _player.volume.clamp(0.0, 1.0);
    if (startVolume <= 0) return;

    await _fadePlayerVolume(
      from: startVolume,
      to: 0,
      duration: duration,
      expectedToken: expectedToken,
      reason: '$reason-out',
    );
  }

  Future<void> _fadePlayerVolume({
    required double from,
    required double to,
    required Duration duration,
    int? expectedToken,
    required String reason,
  }) async {
    final start = from.clamp(0.0, 1.0);
    final end = to.clamp(0.0, 1.0);
    final delta = (start - end).abs();

    if (delta < 0.01 || duration <= Duration.zero) {
      await _setPlayerVolumeSafely(end);
      return;
    }

    final rampToken = ++_volumeRampToken;
    final stepCount = max(
      1,
      min(_volumeFadeSteps, duration.inMilliseconds ~/ 24),
    );
    final stepDelay = Duration(
      milliseconds: max(12, duration.inMilliseconds ~/ stepCount),
    );

    _debug(
      'audio.fade -> reason=$reason, from=${start.toStringAsFixed(2)}, '
      'to=${end.toStringAsFixed(2)}, durationMs=${duration.inMilliseconds}',
      force: true,
    );

    for (var step = 1; step <= stepCount; step++) {
      if (rampToken != _volumeRampToken) return;
      if (expectedToken != null && !_isSessionActive(expectedToken)) return;

      final progress = step / stepCount;
      final nextVolume = start + ((end - start) * progress);
      await _setPlayerVolumeSafely(nextVolume);

      if (step < stepCount) {
        await Future<void>.delayed(stepDelay);
      }
    }
  }

  Future<void> _setPlayerVolumeSafely(double volume) async {
    await _setPlayerVolumeSafelyForTrack(volume, track: state.currentTrack);
  }

  Future<void> _setPlayerVolumeSafelyForTrack(
    double volume, {
    Track? track,
  }) async {
    try {
      final effectiveVolume = _effectiveOutputVolumeForTrack(track, volume);
      await _player.setVolume(effectiveVolume);
    } catch (_) {
      // Ignore transient backend volume failures during player rebuilds.
    }
  }

  double _effectiveOutputVolumeForTrack(Track? track, double requestedVolume) {
    if (_shouldUseNearLosslessDsdPath(track)) {
      return 1.0;
    }
    return requestedVolume.clamp(0.0, 1.0);
  }

  Future<void> _syncNativeLoopMode() async {
    final targetMode = LoopMode.off;
    if (_player.loopMode == targetMode) return;
    await _player.setLoopMode(targetMode);
    _debug(
      'native loopMode -> ${targetMode.name}, '
      'trackExt=${state.currentTrack == null ? 'n/a' : p.extension(state.currentTrack!.path).toLowerCase()}',
      force: true,
    );
  }

  bool _isCurrentTrackFlac() {
    final track = state.currentTrack;
    if (track == null) return false;
    if (track.isRemote) return false;
    return p.extension(track.path).toLowerCase() == '.flac';
  }

  void _debug(String message, {bool force = false}) {
    if (!state.developerMode) return;

    final timestamp = DateTime.now().toIso8601String();
    final line = '[$timestamp] $message';
    final current = state.debugLogs;
    final next = <String>[
      if (current.length >= _maxDebugLogs)
        ...current.skip(current.length - _maxDebugLogs + 1)
      else
        ...current,
      line,
    ];

    state = state.copyWith(debugLogs: next);
    _writeDebugLineToFile(line);
  }

  Future<void> _enableDeveloperOutputs({required bool openConsole}) async {
    try {
      if (_developerLogFilePath == null) {
        final logDirectory = await _resolveDeveloperLogDirectory();
        final fileName = _buildDeveloperLogFileName(DateTime.now());
        final logFile = File(p.join(logDirectory.path, fileName));
        if (!logFile.existsSync()) {
          logFile.createSync(recursive: true);
        }

        _developerLogFilePath = logFile.path;
        _writeDebugLineToFile(
          '[${DateTime.now().toIso8601String()}] ==== PrismWave developer log started ====',
        );
        _writeDebugLineToFile(
          '[${DateTime.now().toIso8601String()}] log.file=$_developerLogFilePath',
        );
      }

      if (openConsole && Platform.isWindows && !_developerConsoleSpawned) {
        final controlPath = p.join(
          File(_developerLogFilePath!).parent.path,
          'console_${DateTime.now().millisecondsSinceEpoch}.flag',
        );
        File(controlPath).writeAsStringSync('active', flush: true);
        _developerConsoleControlFilePath = controlPath;
        await _spawnDeveloperConsole();
        _developerConsoleSpawned = true;
      }
    } catch (error) {
      state = state.copyWith(
        error: 'Failed to start developer outputs: $error',
      );
    }
  }

  Future<void> _disableDeveloperOutputs() async {
    try {
      final controlPath = _developerConsoleControlFilePath;
      _developerConsoleControlFilePath = null;
      if (controlPath != null && controlPath.isNotEmpty) {
        final file = File(controlPath);
        if (file.existsSync()) {
          file.deleteSync();
        }
      }
      _developerLogFilePath = null;
      _developerConsoleSpawned = false;
    } catch (_) {}
  }

  Future<Directory> _resolveDeveloperLogDirectory() async {
    final localAppData = Platform.environment['LOCALAPPDATA'];
    if (localAppData != null && localAppData.isNotEmpty) {
      final dir = Directory(
        p.join(localAppData, _devLogDirName, _devLogSubDir),
      );
      if (!dir.existsSync()) {
        dir.createSync(recursive: true);
      }
      return dir;
    }

    final userProfile = Platform.environment['USERPROFILE'];
    if (userProfile != null && userProfile.isNotEmpty) {
      final dir = Directory(
        p.join(userProfile, 'Documents', _devLogDirName, _devLogSubDir),
      );
      if (!dir.existsSync()) {
        dir.createSync(recursive: true);
      }
      return dir;
    }

    final fallback = Directory(p.join(Directory.current.path, _devLogSubDir));
    if (!fallback.existsSync()) {
      fallback.createSync(recursive: true);
    }
    return fallback;
  }

  String _buildDeveloperLogFileName(DateTime now) {
    final y = now.year.toString().padLeft(4, '0');
    final m = now.month.toString().padLeft(2, '0');
    final d = now.day.toString().padLeft(2, '0');
    final hh = now.hour.toString().padLeft(2, '0');
    final mm = now.minute.toString().padLeft(2, '0');
    final ss = now.second.toString().padLeft(2, '0');
    return 'playback_$y$m${d}_$hh$mm$ss.log';
  }

  Future<void> _spawnDeveloperConsole() async {
    final logPath = _developerLogFilePath;
    final controlPath = _developerConsoleControlFilePath;
    if (logPath == null || logPath.isEmpty) return;
    if (controlPath == null || controlPath.isEmpty) return;

    final escapedLogPath = logPath.replaceAll("'", "''");
    final escapedControlPath = controlPath.replaceAll("'", "''");
    final parentProcessId = pid;
    final scriptPath = p.join(
      File(logPath).parent.path,
      'tail_${DateTime.now().millisecondsSinceEpoch}.ps1',
    );
    final scriptFile = File(scriptPath);
    scriptFile.writeAsStringSync('''
\$Host.UI.RawUI.WindowTitle = 'PrismWave Developer Log'
\$logPath = '$escapedLogPath'
\$controlPath = '$escapedControlPath'
\$parentPid = $parentProcessId
Write-Host 'PrismWave Dev Mode Active'
Write-Host ('Log File: ' + \$logPath)
if (!(Test-Path \$logPath)) { New-Item -ItemType File -Force -Path \$logPath | Out-Null }
\$watcher = Start-Job -ScriptBlock {
  param(\$parentPid, \$controlPath, \$selfPid)
  while (\$true) {
    \$parentAlive = \$null -ne (Get-Process -Id \$parentPid -ErrorAction SilentlyContinue)
    \$controlAlive = Test-Path \$controlPath
    if (-not \$parentAlive -or -not \$controlAlive) {
      Stop-Process -Id \$selfPid -Force
      break
    }
    Start-Sleep -Milliseconds 350
  }
} -ArgumentList \$parentPid, \$controlPath, \$PID
Get-Content -Path \$logPath -Wait
''');

    try {
      ShellExecute(
        0,
        TEXT('open'),
        TEXT('powershell.exe'),
        TEXT('-NoLogo -NoExit -ExecutionPolicy Bypass -File "$scriptPath"'),
        TEXT(''),
        SW_SHOW,
      );
    } catch (e) {
      _writeDebugLineToFile(
        '[${DateTime.now().toIso8601String()}] _spawnDeveloperConsole failed: $e',
      );
    }
  }

  void _writeDebugLineToFile(String line) {
    final path = _developerLogFilePath;
    if (path == null || path.isEmpty) return;
    try {
      File(path).writeAsStringSync(
        '$line${Platform.lineTerminator}',
        mode: FileMode.append,
        flush: true,
      );
    } catch (_) {
      // Keep logging side effects from breaking playback.
    }
  }

  @override
  void dispose() {
    JustAudioMediaKit.nativeAudioRouteLogger = null;
    JustAudioMediaKit.nativeAudioDevicesListener = null;
    JustAudioMediaKit.nativeSelectedAudioDeviceListener = null;
    unawaited(_windowsDsdPositionSub?.cancel() ?? Future<void>.value());
    unawaited(_windowsDsdPlayingSub?.cancel() ?? Future<void>.value());
    unawaited(_windowsDsdCompletedSub?.cancel() ?? Future<void>.value());
    unawaited(_probeAudioDevicesSub?.cancel() ?? Future<void>.value());
    unawaited(_probeAudioDeviceSub?.cancel() ?? Future<void>.value());
    unawaited(_windowsDsdBackend.dispose());
    unawaited(_audioDeviceProbe.dispose());
    unawaited(_disposeCurrentPlayerInstance());
    unawaited(_disableDeveloperOutputs());
    super.dispose();
  }

  static String _normalizeAudioDeviceId(String? value) {
    final trimmed = value?.trim() ?? '';
    return trimmed.isEmpty ? AudioOutputDevice.auto.id : trimmed;
  }

  void _syncKnownAudioDevicesFromBackend() {
    _handleNativeAudioDevices(JustAudioMediaKit.latestAudioDevices);
    _handleNativeSelectedAudioDevice(
      JustAudioMediaKit.latestSelectedAudioDevice,
    );
  }

  void _handleNativeAudioDevices(List<NativeAudioDeviceInfo> devices) {
    final normalized = <AudioOutputDevice>[
      AudioOutputDevice.auto,
      ...devices
          .where((device) => device.id != AudioOutputDevice.auto.id)
          .map(
            (device) => AudioOutputDevice(
              id: _normalizeAudioDeviceId(device.id),
              label: device.label.trim().isEmpty ? device.id : device.label,
            ),
          ),
    ];
    _mergeAvailableAudioDevices(normalized);
  }

  void _handleNativeSelectedAudioDevice(String deviceId) {
    final normalized = _normalizeAudioDeviceId(deviceId);
    if (normalized == AudioOutputDevice.auto.id ||
        normalized == state.audioOutputDeviceId) {
      return;
    }

    final exists = state.availableAudioOutputDevices.any(
      (device) => device.id == normalized,
    );
    if (exists) return;

    state = state.copyWith(
      availableAudioOutputDevices: <AudioOutputDevice>[
        ...state.availableAudioOutputDevices,
        AudioOutputDevice(id: normalized, label: normalized),
      ],
    );
  }

  Future<void> _refreshAudioDeviceProbe({
    required AudioOutputMode mode,
    required String deviceId,
  }) async {
    final normalizedDeviceId = _normalizeAudioDeviceId(deviceId);
    final effectiveMode = _resolveEffectiveAudioOutputMode(
      mode,
      normalizedDeviceId,
    );
    try {
      await _audioDeviceProbe.setAudioDevice(
        media_kit.AudioDevice(normalizedDeviceId, ''),
      );
      await Future<void>.delayed(const Duration(milliseconds: 250));
      _handleProbeAudioDevices(_audioDeviceProbe.state.audioDevices);
      _handleProbeAudioDevice(_audioDeviceProbe.state.audioDevice);
      _debug(
        'audio.deviceProbe => requested=${mode.name}, '
        'effective=${effectiveMode.name}, '
        'count=${_audioDeviceProbe.state.audioDevices.length}, '
        'selected=${_audioDeviceProbe.state.audioDevice.name}',
        force: true,
      );
    } catch (error) {
      _debug('audio.deviceProbe failed => $error', force: true);
    }
  }

  void _handleProbeAudioDevices(List<media_kit.AudioDevice> devices) {
    final mapped = devices
        .map(
          (device) => AudioOutputDevice(
            id: _normalizeAudioDeviceId(device.name),
            label: device.description.trim().isEmpty
                ? device.name
                : device.description,
          ),
        )
        .toList(growable: false);

    final filtered = _filterDevicesForCurrentMode(mapped);
    final next = <AudioOutputDevice>[
      AudioOutputDevice.auto,
      ...filtered.where((device) => device.id != AudioOutputDevice.auto.id),
    ];
    _mergeAvailableAudioDevices(next);
  }

  void _handleProbeAudioDevice(media_kit.AudioDevice device) {
    _handleNativeSelectedAudioDevice(device.name);
  }

  void _mergeAvailableAudioDevices(List<AudioOutputDevice> devices) {
    final deduped = <String, AudioOutputDevice>{};
    for (final device in devices) {
      deduped[device.id] = device;
    }
    final selectedId = state.audioOutputDeviceId;
    if (!deduped.containsKey(AudioOutputDevice.auto.id)) {
      deduped[AudioOutputDevice.auto.id] = AudioOutputDevice.auto;
    }
    if (selectedId != AudioOutputDevice.auto.id &&
        !deduped.containsKey(selectedId)) {
      deduped[selectedId] = AudioOutputDevice(
        id: selectedId,
        label: selectedId,
      );
    }
    state = state.copyWith(
      availableAudioOutputDevices: deduped.values.toList(growable: false),
    );
  }

  List<AudioOutputDevice> _filterDevicesForCurrentMode(
    List<AudioOutputDevice> devices,
  ) {
    final mode = state.audioOutputMode;
    if (mode == AudioOutputMode.compatibility) {
      return devices;
    }
    return devices
        .where(
          (device) =>
              device.id == AudioOutputDevice.auto.id ||
              device.id.toLowerCase().startsWith('wasapi/'),
        )
        .toList(growable: false);
  }

  AudioOutputMode _resolveEffectiveAudioOutputMode(
    AudioOutputMode requestedMode,
    String deviceId,
  ) {
    if (requestedMode != AudioOutputMode.wasapiExclusive) {
      return requestedMode;
    }
    if (deviceId == AudioOutputDevice.auto.id) {
      return requestedMode;
    }

    final label = _labelForAudioDeviceId(deviceId).toLowerCase();
    const headsetHints = <String>[
      'headphone',
      'headphones',
      'headset',
      'earphone',
      'earphones',
      'earbud',
      'earbuds',
      'inzone',
      '耳机',
      '耳麦',
      '耳麥',
    ];
    final looksLikeHeadset = headsetHints.any(label.contains);
    return looksLikeHeadset ? AudioOutputMode.wasapiShared : requestedMode;
  }

  String _labelForAudioDeviceId(String deviceId) {
    for (final device in state.availableAudioOutputDevices) {
      if (device.id == deviceId) {
        return device.label;
      }
    }
    return deviceId;
  }
}
