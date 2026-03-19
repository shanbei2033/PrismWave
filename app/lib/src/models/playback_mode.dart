enum PlaybackMode { loop, single, shuffle }

extension PlaybackModeX on PlaybackMode {
  String get label {
    switch (this) {
      case PlaybackMode.loop:
        return 'Loop';
      case PlaybackMode.single:
        return 'Single';
      case PlaybackMode.shuffle:
        return 'Shuffle';
    }
  }
}
