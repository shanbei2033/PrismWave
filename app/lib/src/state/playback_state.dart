import '../models/audio_output_device.dart';
import '../models/audio_output_mode.dart';
import '../models/playback_backend_kind.dart';
import '../models/playback_mode.dart';
import '../models/track.dart';
import '../models/windows_dsd_device.dart';

class PlaybackState {
  const PlaybackState({
    this.currentTrack,
    this.currentPlaylist = const [],
    this.currentIndex = -1,
    this.playbackMode = PlaybackMode.loop,
    this.isPlaying = false,
    this.isLoading = false,
    this.currentTime = Duration.zero,
    this.duration = Duration.zero,
    this.volume = 1.0,
    this.fadeEnabled = true,
    this.fadeDuration = const Duration(milliseconds: 220),
    this.error,
    this.developerMode = false,
    this.audioOutputMode = AudioOutputMode.wasapiExclusive,
    this.audioOutputDeviceId = 'auto',
    this.availableAudioOutputDevices = const [AudioOutputDevice.auto],
    this.windowsDsdAvailable = false,
    this.selectedWindowsDsdDeviceId = 'auto',
    this.availableWindowsDsdDevices = const [],
    this.windowsDsdOutputModeLabel,
    this.windowsDsdActiveDeviceName,
    this.windowsDsdFallbackReason,
    this.backendKind = PlaybackBackendKind.mediaKit,
    this.debugLogs = const [],
  });

  final Track? currentTrack;
  final List<Track> currentPlaylist;
  final int currentIndex;
  final PlaybackMode playbackMode;
  final bool isPlaying;
  final bool isLoading;
  final Duration currentTime;
  final Duration duration;
  final double volume;
  final bool fadeEnabled;
  final Duration fadeDuration;
  final String? error;
  final bool developerMode;
  final AudioOutputMode audioOutputMode;
  final String audioOutputDeviceId;
  final List<AudioOutputDevice> availableAudioOutputDevices;
  final bool windowsDsdAvailable;
  final String selectedWindowsDsdDeviceId;
  final List<WindowsDsdDevice> availableWindowsDsdDevices;
  final String? windowsDsdOutputModeLabel;
  final String? windowsDsdActiveDeviceName;
  final String? windowsDsdFallbackReason;
  final PlaybackBackendKind backendKind;
  final List<String> debugLogs;

  bool get hasTrack => currentTrack != null;

  PlaybackState copyWith({
    Object? currentTrack = _playbackStateSentinel,
    List<Track>? currentPlaylist,
    int? currentIndex,
    PlaybackMode? playbackMode,
    bool? isPlaying,
    bool? isLoading,
    Duration? currentTime,
    Duration? duration,
    double? volume,
    bool? fadeEnabled,
    Duration? fadeDuration,
    String? error,
    bool? developerMode,
    AudioOutputMode? audioOutputMode,
    String? audioOutputDeviceId,
    List<AudioOutputDevice>? availableAudioOutputDevices,
    bool? windowsDsdAvailable,
    String? selectedWindowsDsdDeviceId,
    List<WindowsDsdDevice>? availableWindowsDsdDevices,
    Object? windowsDsdOutputModeLabel = _playbackStateSentinel,
    Object? windowsDsdActiveDeviceName = _playbackStateSentinel,
    Object? windowsDsdFallbackReason = _playbackStateSentinel,
    PlaybackBackendKind? backendKind,
    List<String>? debugLogs,
    bool clearError = false,
  }) {
    return PlaybackState(
      currentTrack: currentTrack == _playbackStateSentinel
          ? this.currentTrack
          : currentTrack as Track?,
      currentPlaylist: currentPlaylist ?? this.currentPlaylist,
      currentIndex: currentIndex ?? this.currentIndex,
      playbackMode: playbackMode ?? this.playbackMode,
      isPlaying: isPlaying ?? this.isPlaying,
      isLoading: isLoading ?? this.isLoading,
      currentTime: currentTime ?? this.currentTime,
      duration: duration ?? this.duration,
      volume: volume ?? this.volume,
      fadeEnabled: fadeEnabled ?? this.fadeEnabled,
      fadeDuration: fadeDuration ?? this.fadeDuration,
      error: clearError ? null : (error ?? this.error),
      developerMode: developerMode ?? this.developerMode,
      audioOutputMode: audioOutputMode ?? this.audioOutputMode,
      audioOutputDeviceId: audioOutputDeviceId ?? this.audioOutputDeviceId,
      availableAudioOutputDevices:
          availableAudioOutputDevices ?? this.availableAudioOutputDevices,
      windowsDsdAvailable: windowsDsdAvailable ?? this.windowsDsdAvailable,
      selectedWindowsDsdDeviceId:
          selectedWindowsDsdDeviceId ?? this.selectedWindowsDsdDeviceId,
      availableWindowsDsdDevices:
          availableWindowsDsdDevices ?? this.availableWindowsDsdDevices,
      windowsDsdOutputModeLabel:
          windowsDsdOutputModeLabel == _playbackStateSentinel
          ? this.windowsDsdOutputModeLabel
          : windowsDsdOutputModeLabel as String?,
      windowsDsdActiveDeviceName:
          windowsDsdActiveDeviceName == _playbackStateSentinel
          ? this.windowsDsdActiveDeviceName
          : windowsDsdActiveDeviceName as String?,
      windowsDsdFallbackReason:
          windowsDsdFallbackReason == _playbackStateSentinel
          ? this.windowsDsdFallbackReason
          : windowsDsdFallbackReason as String?,
      backendKind: backendKind ?? this.backendKind,
      debugLogs: debugLogs ?? this.debugLogs,
    );
  }
}

class PlaybackSessionSnapshot {
  const PlaybackSessionSnapshot({
    required this.playlist,
    required this.currentTrack,
    required this.currentIndex,
    required this.currentTime,
    required this.playbackMode,
    required this.wasPlaying,
  });

  final List<Track> playlist;
  final Track? currentTrack;
  final int currentIndex;
  final Duration currentTime;
  final PlaybackMode playbackMode;
  final bool wasPlaying;

  bool get hasPlayback =>
      currentTrack != null ||
      playlist.isNotEmpty ||
      currentTime > Duration.zero;
}

const Object _playbackStateSentinel = Object();
