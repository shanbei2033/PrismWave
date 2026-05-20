import '../models/lyrics_document.dart';
import '../models/hits_manifest.dart';
import '../models/track.dart';
import '../services/hits_scheduler.dart';
import 'dart:typed_data';

enum HitsStatus {
  idle,
  loading,
  ready,
  offAir,
  standby,
  noNetwork,
  cloudTimeout,
  unavailable,
}

class HitsState {
  const HitsState({
    this.status = HitsStatus.idle,
    this.latestManifest,
    this.schedule,
    this.position,
    this.matchedLibraryTrack,
    this.resolvedPlaybackTrack,
    this.onlineLyricsDocument,
    this.currentCoverBytes,
    this.isOnlineLyricsLoading = false,
    this.isCoverLoading = false,
    this.isResolvingPlaybackSource = false,
    this.isSessionActive = false,
    this.isPlaying = false,
    this.userPaused = false,
    this.usingCachedSchedule = false,
    this.currentUtcTime,
    this.error,
    this.isRefreshing = false,
  });

  final HitsStatus status;
  final HitsLatestManifest? latestManifest;
  final HitsSchedule? schedule;
  final HitsSchedulePosition? position;
  final Track? matchedLibraryTrack;
  final Track? resolvedPlaybackTrack;
  final LyricsDocument? onlineLyricsDocument;
  final Uint8List? currentCoverBytes;
  final bool isOnlineLyricsLoading;
  final bool isCoverLoading;
  final bool isResolvingPlaybackSource;
  final bool isSessionActive;
  final bool isPlaying;
  final bool userPaused;
  final bool usingCachedSchedule;
  final DateTime? currentUtcTime;
  final String? error;
  final bool isRefreshing;

  HitsScheduleTrack? get currentScheduleTrack => position?.currentTrack;

  bool get hasActiveTrack => currentScheduleTrack != null;

  bool get hasPlaybackSource => resolvedPlaybackTrack != null;

  bool get isUsingRemotePlayback => resolvedPlaybackTrack?.isRemote ?? false;

  bool get canTogglePlayback =>
      status == HitsStatus.ready && resolvedPlaybackTrack != null;

  HitsState copyWith({
    HitsStatus? status,
    HitsLatestManifest? latestManifest,
    HitsSchedule? schedule,
    HitsSchedulePosition? position,
    Object? matchedLibraryTrack = _hitsStateSentinel,
    Object? resolvedPlaybackTrack = _hitsStateSentinel,
    Object? onlineLyricsDocument = _hitsStateSentinel,
    Object? currentCoverBytes = _hitsStateSentinel,
    bool? isOnlineLyricsLoading,
    bool? isCoverLoading,
    bool? isResolvingPlaybackSource,
    bool? isSessionActive,
    bool? isPlaying,
    bool? userPaused,
    bool? usingCachedSchedule,
    DateTime? currentUtcTime,
    String? error,
    bool clearError = false,
    bool? isRefreshing,
  }) {
    return HitsState(
      status: status ?? this.status,
      latestManifest: latestManifest ?? this.latestManifest,
      schedule: schedule ?? this.schedule,
      position: position ?? this.position,
      matchedLibraryTrack: matchedLibraryTrack == _hitsStateSentinel
          ? this.matchedLibraryTrack
          : matchedLibraryTrack as Track?,
      resolvedPlaybackTrack: resolvedPlaybackTrack == _hitsStateSentinel
          ? this.resolvedPlaybackTrack
          : resolvedPlaybackTrack as Track?,
      onlineLyricsDocument: onlineLyricsDocument == _hitsStateSentinel
          ? this.onlineLyricsDocument
          : onlineLyricsDocument as LyricsDocument?,
      currentCoverBytes: currentCoverBytes == _hitsStateSentinel
          ? this.currentCoverBytes
          : currentCoverBytes as Uint8List?,
      isOnlineLyricsLoading:
          isOnlineLyricsLoading ?? this.isOnlineLyricsLoading,
      isCoverLoading: isCoverLoading ?? this.isCoverLoading,
      isResolvingPlaybackSource:
          isResolvingPlaybackSource ?? this.isResolvingPlaybackSource,
      isSessionActive: isSessionActive ?? this.isSessionActive,
      isPlaying: isPlaying ?? this.isPlaying,
      userPaused: userPaused ?? this.userPaused,
      usingCachedSchedule: usingCachedSchedule ?? this.usingCachedSchedule,
      currentUtcTime: currentUtcTime ?? this.currentUtcTime,
      error: clearError ? null : (error ?? this.error),
      isRefreshing: isRefreshing ?? this.isRefreshing,
    );
  }
}

const Object _hitsStateSentinel = Object();
