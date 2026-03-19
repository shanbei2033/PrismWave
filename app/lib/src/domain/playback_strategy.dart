import '../models/playback_mode.dart';

/// Strict playback-index strategy following dev.md:
/// - loop: linear with wrap-around
/// - single: only auto-ended repeats current; manual next/previous behave like loop
/// - shuffle: random and avoid current index
class PlaybackStrategy {
  static int resolveNextIndex({
    required int playlistLength,
    required int currentIndex,
    required PlaybackMode mode,
    required bool fromAutoEnded,
    required int Function(int upperBoundExclusive) randomInt,
  }) {
    if (playlistLength <= 0) return -1;
    final safeCurrent = _normalizeIndex(
      currentIndex: currentIndex,
      playlistLength: playlistLength,
    );

    if (mode == PlaybackMode.single && fromAutoEnded) {
      return safeCurrent;
    }

    if (mode == PlaybackMode.shuffle) {
      return _pickShuffleIndex(
        playlistLength: playlistLength,
        currentIndex: safeCurrent,
        randomInt: randomInt,
      );
    }

    return (safeCurrent + 1) % playlistLength;
  }

  static int resolvePreviousIndex({
    required int playlistLength,
    required int currentIndex,
    required PlaybackMode mode,
    required int Function(int upperBoundExclusive) randomInt,
  }) {
    if (playlistLength <= 0) return -1;
    final safeCurrent = _normalizeIndex(
      currentIndex: currentIndex,
      playlistLength: playlistLength,
    );

    if (mode == PlaybackMode.shuffle) {
      return _pickShuffleIndex(
        playlistLength: playlistLength,
        currentIndex: safeCurrent,
        randomInt: randomInt,
      );
    }

    return (safeCurrent - 1 + playlistLength) % playlistLength;
  }

  static PlaybackMode cycleMode(PlaybackMode mode) {
    switch (mode) {
      case PlaybackMode.loop:
        return PlaybackMode.single;
      case PlaybackMode.single:
        return PlaybackMode.shuffle;
      case PlaybackMode.shuffle:
        return PlaybackMode.loop;
    }
  }

  static int _normalizeIndex({
    required int currentIndex,
    required int playlistLength,
  }) {
    if (currentIndex < 0 || currentIndex >= playlistLength) return 0;
    return currentIndex;
  }

  static int _pickShuffleIndex({
    required int playlistLength,
    required int currentIndex,
    required int Function(int upperBoundExclusive) randomInt,
  }) {
    if (playlistLength <= 1) return currentIndex;

    var pick = randomInt(playlistLength);
    while (pick == currentIndex) {
      pick = randomInt(playlistLength);
    }
    return pick;
  }
}
