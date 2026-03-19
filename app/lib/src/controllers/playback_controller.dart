import 'dart:async';
import 'dart:io';
import 'dart:math';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:just_audio/just_audio.dart';
import 'package:just_audio_media_kit/just_audio_media_kit.dart';
import 'package:path/path.dart' as p;
import 'package:shared_preferences/shared_preferences.dart';

import '../domain/playback_strategy.dart';
import '../models/audio_output_mode.dart';
import '../models/playback_mode.dart';
import '../models/track.dart';
import '../state/playback_state.dart';

class PlaybackController extends StateNotifier<PlaybackState> {
  PlaybackController() : super(const PlaybackState()) {
    JustAudioMediaKit.nativeAudioRouteLogger = (message) {
      _debug('native.output => $message', force: true);
    };
    _applyAudioOutputModeToBackend(state.audioOutputMode);
    _initializePlayer();
    unawaited(_loadDeveloperMode());
    unawaited(_loadAudioOutputMode());
  }

  late AudioPlayer _player;
  final Random _random = Random();

  static const Set<String> _demoPlayableExtensions = {
    '.mp3',
    '.wav',
    '.flac',
    '.ogg',
    '.aac',
    '.m4a',
    '.mp4',
  };

  static const String _prefDeveloperMode = 'debug.playbackDeveloperMode';
  static const int _maxDebugLogs = 500;
  static const String _devLogDirName = 'PrismWave';
  static const String _devLogSubDir = 'logs';

  StreamSubscription<PlayerState>? _playerStateSub;
  StreamSubscription<Duration>? _positionSub;
  StreamSubscription<Duration?>? _durationSub;
  StreamSubscription<int?>? _currentIndexSub;
  StreamSubscription<PlayerException>? _errorSub;
  String? _developerLogFilePath;
  bool _developerConsoleSpawned = false;

  int _sessionToken = 0;
  bool _autoAdvancing = false;
  bool _recoveringDecoderError = false;
  int _decoderRecoveryCount = 0;
  DateTime _decoderRecoveryWindowStart = DateTime.fromMillisecondsSinceEpoch(0);
  ProcessingState? _lastProcessingState;
  bool? _lastPlayingState;

  void _initializePlayer() {
    _player = AudioPlayer();
    _bindPlayerEvents();
    _player.setVolume(state.volume);
    unawaited(_syncNativeLoopMode());
  }

  Future<void> _loadAudioOutputMode() async {
    final prefs = await SharedPreferences.getInstance();
    final restored = AudioOutputMode.fromId(
      prefs.getString(kPrefAudioOutputMode),
    );
    if (restored == state.audioOutputMode) return;
    await _setAudioOutputModeInternal(restored, persist: false);
  }

  Future<void> _loadDeveloperMode() async {
    final prefs = await SharedPreferences.getInstance();
    final enabled = prefs.getBool(_prefDeveloperMode) ?? false;
    if (enabled != state.developerMode) {
      state = state.copyWith(developerMode: enabled);
    }
    if (enabled) {
      await _enableDeveloperOutputs(openConsole: true);
      _debug('Developer mode restored from settings.', force: true);
    }
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
    await _rebuildPlayerForOutputMode(mode);
  }

  void clearDebugLogs() {
    state = state.copyWith(debugLogs: const []);
  }

  void _applyAudioOutputModeToBackend(AudioOutputMode mode) {
    switch (mode) {
      case AudioOutputMode.compatibility:
        JustAudioMediaKit.preferWasapi = false;
        JustAudioMediaKit.preferWasapiExclusive = false;
        JustAudioMediaKit.fallbackToWasapiShared = false;
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

  Future<void> _rebuildPlayerForOutputMode(AudioOutputMode mode) async {
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
    state = state.copyWith(
      audioOutputMode: mode,
      isLoading: hadPlaylist,
      isPlaying: false,
      clearError: true,
    );

    _player = oldPlayer;
    await _disposeCurrentPlayerInstance();

    _applyAudioOutputModeToBackend(mode);
    _initializePlayer();

    _debug('audio.outputMode switched -> ${mode.name}', force: true);

    if (!hadPlaylist) {
      state = state.copyWith(
        audioOutputMode: mode,
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
      currentPlaylist: playlist,
      currentTrack: track,
      currentIndex: restoreIndex,
      currentTime: restorePosition,
      duration: Duration.zero,
      isLoading: true,
      clearError: true,
    );

    try {
      await _player.setFilePath(
        track.path,
        initialPosition: restorePosition,
        preload: true,
      );
      if (!_isSessionActive(token)) return;
      await _syncNativeLoopMode();
      if (!_isSessionActive(token)) return;

      if (wasPlaying) {
        await _player.play();
      } else {
        await _player.pause();
      }
      if (!_isSessionActive(token)) return;

      state = state.copyWith(
        audioOutputMode: mode,
        isLoading: false,
        currentTime: restorePosition,
        clearError: true,
      );
    } catch (error) {
      if (!_isSessionActive(token)) return;
      state = state.copyWith(
        audioOutputMode: mode,
        isLoading: false,
        error: 'Switch output mode failed: $error',
      );
      _debug('audio.outputMode reload failed -> $error', force: true);
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

    await _player.setVolume(state.volume);
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

      if (_shouldRecoverFromDecodeError(error)) {
        unawaited(_attemptDecodeRecovery(trigger: message));
        return;
      }

      if (_autoAdvancing) {
        _debug('Transient auto-advance error ignored at UI layer.');
        return;
      }

      state = state.copyWith(isLoading: false, error: message);
    });
  }

  Future<void> playFromPlaylist(Track track, List<Track> playlist) async {
    await _playFromContext(track, playlist);
  }

  Future<void> playFromLibrary(Track track, List<Track> libraryTracks) async {
    await _playFromContext(track, libraryTracks);
  }

  Future<void> _playFromContext(Track track, List<Track> playlist) async {
    if (playlist.isEmpty) return;
    if (!_isPlayableInDemo(track.path)) {
      state = state.copyWith(
        error:
            'This file format is not playable in current demo backend: ${p.extension(track.path)}',
      );
      return;
    }

    final playablePlaylist = playlist
        .where((item) => _isPlayableInDemo(item.path))
        .toList(growable: false);
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

    _debug(
      'playFromContext -> selectedIndex=$index, playlistLength=${playablePlaylist.length}, '
      'track="${track.title}", ext=${p.extension(track.path).toLowerCase()}, '
      'outputMode=${state.audioOutputMode.name}',
      force: true,
    );

    final token = _newSession();
    state = state.copyWith(
      currentPlaylist: playablePlaylist,
      currentTrack: playablePlaylist[index],
      currentIndex: index,
      currentTime: Duration.zero,
      duration: Duration.zero,
      isLoading: true,
      clearError: true,
    );
    await _syncNativeLoopMode();

    await _loadPlaylistAndPlay(
      playlist: playablePlaylist,
      index: index,
      expectedToken: token,
      errorPrefix: 'Play failed',
    );
  }

  Future<void> togglePlayPause() async {
    if (!state.hasTrack) return;

    _debug(
      'togglePlayPause -> playing=${_player.playing}, '
      'processing=${_player.processingState.name}',
      force: true,
    );

    if (_player.playing) {
      await _player.pause();
      return;
    }

    if (_player.processingState == ProcessingState.completed) {
      await _restartCurrentTrack();
      return;
    }

    if (_player.processingState == ProcessingState.idle &&
        state.currentPlaylist.isNotEmpty) {
      final token = _newSession();
      state = state.copyWith(isLoading: true, clearError: true);
      await _syncNativeLoopMode();
      await _loadPlaylistAndPlay(
        playlist: state.currentPlaylist,
        index: state.currentIndex < 0 ? 0 : state.currentIndex,
        expectedToken: token,
        errorPrefix: 'Play failed',
      );
      return;
    }

    try {
      await _syncNativeLoopMode();
      await _player.play();
      state = state.copyWith(clearError: true);
    } catch (error) {
      state = state.copyWith(isLoading: false, error: 'Play failed: $error');
    }
  }

  Future<void> seekTo(Duration position) async {
    if (!state.hasTrack) return;
    await _player.seek(position);
  }

  Future<void> setVolume(double volume) async {
    final normalized = volume.clamp(0.0, 1.0);
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
    await _playIndex(nextIndex);
  }

  Future<void> _playIndex(int index) async {
    if (index < 0 || index >= state.currentPlaylist.length) return;

    if (index == state.currentIndex) {
      _debug('playIndex -> same index($index), restart current.', force: true);
      await _restartCurrentTrack();
      return;
    }

    final token = _newSession();
    state = state.copyWith(
      currentIndex: index,
      currentTrack: state.currentPlaylist[index],
      currentTime: Duration.zero,
      duration: Duration.zero,
      isLoading: true,
      clearError: true,
    );
    await _syncNativeLoopMode();

    try {
      _debug(
        'playIndex -> reload target track directly. index=$index, '
        'title="${state.currentPlaylist[index].title}"',
        force: true,
      );
      await _loadPlaylistAndPlay(
        playlist: state.currentPlaylist,
        index: index,
        expectedToken: token,
        errorPrefix: 'Switch track failed',
      );
    } catch (error) {
      if (!_isSessionActive(token)) return;
      state = state.copyWith(
        isLoading: false,
        error: 'Switch track failed: $error',
      );
    }
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

    try {
      if (shouldRebuildForExclusiveRestart) {
        _debug(
          'restartCurrentTrack -> completed in exclusive mode, reload via fresh player.',
          force: true,
        );
        await _loadPlaylistAndPlay(
          playlist: state.currentPlaylist,
          index: index,
          expectedToken: token,
          errorPrefix: 'Failed to restart track',
        );
        return;
      }

      _debug('restartCurrentTrack -> seek(0)+play, index=$index', force: true);
      await _player.seek(Duration.zero);
      if (!_isSessionActive(token)) return;

      await _player.play();
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
      );
    }
  }

  bool _shouldRecoverFromDecodeError(PlayerException error) {
    final message = (error.message ?? '').toLowerCase();
    final looksLikeDecodeError =
        message.contains('decode') || message.contains('decoding');
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
        'setFilePath(managed-playlist) -> index=$index, playlistLength=${playlist.length}, '
        'file=${track.fileName}, ext=${p.extension(track.path).toLowerCase()}',
      );
      _debug('loadPlaylistAndPlay -> begin setFilePath');
      await _player.setFilePath(
        track.path,
        initialPosition: Duration.zero,
        preload: true,
      );
      if (!_isSessionActive(expectedToken)) return;
      _debug('loadPlaylistAndPlay -> setFilePath completed');
      await _syncNativeLoopMode();
      if (!_isSessionActive(expectedToken)) return;

      _debug('loadPlaylistAndPlay -> begin play');
      await _player.play();
      if (!_isSessionActive(expectedToken)) return;
      _debug('loadPlaylistAndPlay -> play completed');

      state = state.copyWith(isLoading: false, clearError: true);
      _debug('loadPlaylistAndPlay success.');
    } on PlayerInterruptedException {
      if (!_isSessionActive(expectedToken)) return;
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
      state = state.copyWith(
        isLoading: false,
        error: '$errorPrefix: ${track?.title ?? 'Unknown Track'} ($error)',
      );
      _debug(
        '$errorPrefix -> ${track?.title ?? 'Unknown Track'} | $error',
        force: true,
      );
    }
  }

  bool _isPlayableInDemo(String path) {
    final extension = p.extension(path).toLowerCase();
    return _demoPlayableExtensions.contains(extension);
  }

  int _newSession() {
    _sessionToken += 1;
    return _sessionToken;
  }

  bool _isSessionActive(int token) => token == _sessionToken;

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
    return p.extension(track.path).toLowerCase() == '.flac';
  }

  void _debug(String message, {bool force = false}) {
    if (!force && !state.developerMode) return;

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
    if (logPath == null || logPath.isEmpty) return;

    final escaped = logPath.replaceAll("'", "''");
    final scriptPath = p.join(
      File(logPath).parent.path,
      'tail_${DateTime.now().millisecondsSinceEpoch}.ps1',
    );
    final scriptFile = File(scriptPath);
    scriptFile.writeAsStringSync('''
\$Host.UI.RawUI.WindowTitle = 'PrismWave Developer Log'
\$logPath = '$escaped'
Write-Host 'PrismWave Dev Mode Active'
Write-Host ('Log File: ' + \$logPath)
if (!(Test-Path \$logPath)) { New-Item -ItemType File -Force -Path \$logPath | Out-Null }
Get-Content -Path \$logPath -Wait
''');

    await Process.start('cmd.exe', [
      '/c',
      'start',
      '',
      'powershell.exe',
      '-NoLogo',
      '-NoExit',
      '-ExecutionPolicy',
      'Bypass',
      '-File',
      scriptPath,
    ], mode: ProcessStartMode.detached);
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
    unawaited(_disposeCurrentPlayerInstance());
    unawaited(_disableDeveloperOutputs());
    super.dispose();
  }
}
