import 'package:just_audio/just_audio.dart';

import '../models/track.dart';

typedef DurationBatchCallback = bool Function(Map<String, Duration> batch);

Future<void> resolveTrackDurations(
  List<Track> tracks, {
  required DurationBatchCallback onBatch,
  Duration perFileTimeout = const Duration(seconds: 4),
  int commitEvery = 8,
}) async {
  if (tracks.isEmpty) return;

  final player = AudioPlayer();
  final batch = <String, Duration>{};

  try {
    for (final track in tracks) {
      try {
        final duration = await player
            .setFilePath(
              track.path,
              initialPosition: Duration.zero,
              preload: true,
            )
            .timeout(perFileTimeout);
        if (duration != null && duration > Duration.zero) {
          batch[track.path] = duration;
        }
      } catch (_) {
        // Skip unreadable/unresolvable duration files in demo phase.
      }

      if (batch.length >= commitEvery) {
        final shouldContinue = onBatch(Map<String, Duration>.from(batch));
        batch.clear();
        if (!shouldContinue) return;
      }
    }

    if (batch.isNotEmpty) {
      onBatch(Map<String, Duration>.from(batch));
    }
  } finally {
    await player.dispose();
  }
}
