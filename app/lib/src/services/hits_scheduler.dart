import '../models/hits_manifest.dart';

enum HitsPositionKind { onAir, offAir, standby }

class HitsSchedulePosition {
  const HitsSchedulePosition({
    required this.kind,
    this.activeWindow,
    this.currentTrack,
    this.nextTrack,
    this.playbackOffset = Duration.zero,
    this.timeUntilNextChange,
  });

  final HitsPositionKind kind;
  final HitsTimeWindow? activeWindow;
  final HitsScheduleTrack? currentTrack;
  final HitsScheduleTrack? nextTrack;
  final Duration playbackOffset;
  final Duration? timeUntilNextChange;
}

class HitsScheduler {
  const HitsScheduler();

  HitsSchedulePosition resolve({
    required HitsSchedule schedule,
    required DateTime nowUtc,
  }) {
    final currentTrack = schedule.tracks.cast<HitsScheduleTrack?>().firstWhere(
      (track) => track != null && track.contains(nowUtc),
      orElse: () => null,
    );
    if (currentTrack != null) {
      return HitsSchedulePosition(
        kind: HitsPositionKind.onAir,
        activeWindow: schedule.serviceWindows.cast<HitsTimeWindow?>().firstWhere(
          (window) => window != null && window.contains(nowUtc),
          orElse: () => null,
        ),
        currentTrack: currentTrack,
        nextTrack: _nextTrackAfter(schedule.tracks, nowUtc),
        playbackOffset: nowUtc.difference(currentTrack.startAt),
        timeUntilNextChange: currentTrack.endAt.difference(nowUtc),
      );
    }

    final offAirWindow = schedule.offAirWindows.cast<HitsTimeWindow?>().firstWhere(
      (window) => window != null && window.contains(nowUtc),
      orElse: () => null,
    );
    if (offAirWindow != null) {
      return HitsSchedulePosition(
        kind: HitsPositionKind.offAir,
        activeWindow: offAirWindow,
        nextTrack: _nextTrackAfter(schedule.tracks, nowUtc),
        timeUntilNextChange: offAirWindow.endAt.difference(nowUtc),
      );
    }

    final serviceWindow = schedule.serviceWindows.cast<HitsTimeWindow?>().firstWhere(
      (window) => window != null && window.contains(nowUtc),
      orElse: () => null,
    );
    final nextTrack = _nextTrackAfter(schedule.tracks, nowUtc);
    return HitsSchedulePosition(
      kind: HitsPositionKind.standby,
      activeWindow: serviceWindow,
      nextTrack: nextTrack,
      timeUntilNextChange: nextTrack?.startAt.difference(nowUtc),
    );
  }

  HitsScheduleTrack? _nextTrackAfter(List<HitsScheduleTrack> tracks, DateTime nowUtc) {
    for (final track in tracks) {
      if (track.startAt.isAfter(nowUtc)) {
        return track;
      }
    }
    return null;
  }
}
