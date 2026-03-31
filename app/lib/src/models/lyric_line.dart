class LyricSegment {
  const LyricSegment({
    required this.start,
    required this.end,
    required this.text,
  });

  final Duration start;
  final Duration end;
  final String text;

  LyricSegment copyWith({
    Duration? start,
    Duration? end,
    String? text,
  }) {
    return LyricSegment(
      start: start ?? this.start,
      end: end ?? this.end,
      text: text ?? this.text,
    );
  }
}

class LyricLine {
  const LyricLine({
    required this.time,
    required this.text,
    this.segments = const <LyricSegment>[],
  });

  final Duration time;
  final String text;
  final List<LyricSegment> segments;

  bool get hasTimedSegments => segments.isNotEmpty;

  LyricLine copyWith({
    Duration? time,
    String? text,
    List<LyricSegment>? segments,
  }) {
    return LyricLine(
      time: time ?? this.time,
      text: text ?? this.text,
      segments: segments ?? this.segments,
    );
  }
}
