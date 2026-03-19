import '../models/audio_output_mode.dart';
import '../models/playback_mode.dart';
import '../models/track.dart';

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
    this.error,
    this.developerMode = false,
    this.audioOutputMode = AudioOutputMode.wasapiExclusive,
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
  final String? error;
  final bool developerMode;
  final AudioOutputMode audioOutputMode;
  final List<String> debugLogs;

  bool get hasTrack => currentTrack != null;

  PlaybackState copyWith({
    Track? currentTrack,
    List<Track>? currentPlaylist,
    int? currentIndex,
    PlaybackMode? playbackMode,
    bool? isPlaying,
    bool? isLoading,
    Duration? currentTime,
    Duration? duration,
    double? volume,
    String? error,
    bool? developerMode,
    AudioOutputMode? audioOutputMode,
    List<String>? debugLogs,
    bool clearError = false,
  }) {
    return PlaybackState(
      currentTrack: currentTrack ?? this.currentTrack,
      currentPlaylist: currentPlaylist ?? this.currentPlaylist,
      currentIndex: currentIndex ?? this.currentIndex,
      playbackMode: playbackMode ?? this.playbackMode,
      isPlaying: isPlaying ?? this.isPlaying,
      isLoading: isLoading ?? this.isLoading,
      currentTime: currentTime ?? this.currentTime,
      duration: duration ?? this.duration,
      volume: volume ?? this.volume,
      error: clearError ? null : (error ?? this.error),
      developerMode: developerMode ?? this.developerMode,
      audioOutputMode: audioOutputMode ?? this.audioOutputMode,
      debugLogs: debugLogs ?? this.debugLogs,
    );
  }
}
