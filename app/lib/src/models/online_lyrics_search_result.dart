import 'dart:convert';

import 'lyrics_document.dart';
import 'lyric_line.dart';

class OnlineLyricsSearchResult {
  const OnlineLyricsSearchResult({
    required this.id,
    required this.title,
    required this.artist,
    required this.album,
    required this.durationSeconds,
    required this.instrumental,
    required this.syncedLyrics,
    required this.plainLyrics,
    required this.provider,
    this.score = 0,
  });

  final int id;
  final String title;
  final String artist;
  final String album;
  final double durationSeconds;
  final bool instrumental;
  final String? syncedLyrics;
  final String? plainLyrics;
  final String provider;
  final int score;

  String? get preferredRawLyrics {
    final synced = syncedLyrics?.trim();
    if (synced != null && synced.isNotEmpty) return synced;
    final plain = plainLyrics?.trim();
    if (plain != null && plain.isNotEmpty) return plain;
    return null;
  }

  bool get hasLyrics => preferredRawLyrics != null;

  bool get isSynced => (syncedLyrics?.trim().isNotEmpty ?? false);

  int get byteSize => utf8.encode(preferredRawLyrics ?? '').length;

  OnlineLyricsSearchResult copyWith({int? score}) {
    return OnlineLyricsSearchResult(
      id: id,
      title: title,
      artist: artist,
      album: album,
      durationSeconds: durationSeconds,
      instrumental: instrumental,
      syncedLyrics: syncedLyrics,
      plainLyrics: plainLyrics,
      provider: provider,
      score: score ?? this.score,
    );
  }

  factory OnlineLyricsSearchResult.fromJson(
    Map<String, dynamic> json, {
    required String provider,
  }) {
    return OnlineLyricsSearchResult(
      id: (json['id'] as num?)?.round() ?? 0,
      title:
          json['trackName']?.toString() ??
          json['name']?.toString() ??
          'Unknown Title',
      artist: json['artistName']?.toString() ?? 'Unknown Artist',
      album: json['albumName']?.toString() ?? 'Unknown Album',
      durationSeconds: (json['duration'] as num?)?.toDouble() ?? 0,
      instrumental: json['instrumental'] == true,
      syncedLyrics: json['syncedLyrics']?.toString(),
      plainLyrics: json['plainLyrics']?.toString(),
      provider: provider,
    );
  }

  LyricsDocument toLyricsDocument(List<LyricLine> lines) {
    return LyricsDocument(
      lines: lines,
      isSynced: isSynced,
      rawText: preferredRawLyrics,
      provider: provider,
      remoteId: id,
      title: title,
      artist: artist,
      album: album,
      byteSize: byteSize,
    );
  }
}
