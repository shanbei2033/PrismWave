import 'lyric_line.dart';

class LyricsDocument {
  const LyricsDocument({
    required this.lines,
    required this.isSynced,
    this.rawText,
    this.provider,
    this.remoteId,
    this.title,
    this.artist,
    this.album,
    this.byteSize,
  });

  final List<LyricLine> lines;
  final bool isSynced;
  final String? rawText;
  final String? provider;
  final int? remoteId;
  final String? title;
  final String? artist;
  final String? album;
  final int? byteSize;

  bool get isEmpty => lines.isEmpty;

  Map<String, dynamic> toCacheJson() {
    return <String, dynamic>{
      'isSynced': isSynced,
      'rawText': rawText,
      'provider': provider,
      'remoteId': remoteId,
      'title': title,
      'artist': artist,
      'album': album,
      'byteSize': byteSize,
      'lines': lines
          .map(
            (line) => <String, dynamic>{
              'timeMs': line.time.inMilliseconds,
              'text': line.text,
            },
          )
          .toList(growable: false),
    };
  }

  factory LyricsDocument.fromCacheJson(Map<String, dynamic> json) {
    final rawLines = json['lines'];
    final lines = rawLines is List
        ? rawLines
              .whereType<Map>()
              .map(
                (line) => LyricLine(
                  time: Duration(
                    milliseconds: (line['timeMs'] as num?)?.round() ?? 0,
                  ),
                  text: line['text']?.toString() ?? '',
                ),
              )
              .where((line) => line.text.trim().isNotEmpty)
              .toList(growable: false)
        : const <LyricLine>[];

    return LyricsDocument(
      lines: lines,
      isSynced: json['isSynced'] == true,
      rawText: json['rawText']?.toString(),
      provider: json['provider']?.toString(),
      remoteId: (json['remoteId'] as num?)?.round(),
      title: json['title']?.toString(),
      artist: json['artist']?.toString(),
      album: json['album']?.toString(),
      byteSize: (json['byteSize'] as num?)?.round(),
    );
  }
}
