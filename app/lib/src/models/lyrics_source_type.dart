enum LyricsSourceType {
  local,
  online;

  String get id => switch (this) {
    LyricsSourceType.local => 'local',
    LyricsSourceType.online => 'online',
  };

  static LyricsSourceType fromId(String? value) => switch (value) {
    'online' => LyricsSourceType.online,
    _ => LyricsSourceType.local,
  };
}
