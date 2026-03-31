import 'dart:convert';

import 'lyrics_document.dart';
import 'lyric_line.dart';

final RegExp _enhancedLrcSegmentPattern = RegExp(
  r'<\d{1,2}:\d{2}(?:[.:]\d{1,3})?>',
);
final RegExp _qrcLinePattern = RegExp(r'^\[\d+,\d+\]');
final RegExp _qrcWordPattern = RegExp(r'[^()]+?\(\d+,\d+\)');

class OnlineLyricsSearchResult {
  OnlineLyricsSearchResult({
    required this.id,
    required this.title,
    required this.artist,
    required this.album,
    required this.durationSeconds,
    required this.instrumental,
    required this.syncedLyrics,
    required this.plainLyrics,
    required this.provider,
    this.hasTimedSegments = false,
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
  final bool hasTimedSegments;
  final int score;

  late final String? preferredRawLyrics = () {
    final synced = syncedLyrics?.trim();
    if (synced != null && synced.isNotEmpty) return synced;
    final plain = plainLyrics?.trim();
    if (plain != null && plain.isNotEmpty) return plain;
    return null;
  }();

  late final bool hasLyrics = preferredRawLyrics != null;

  late final bool isSynced = syncedLyrics?.trim().isNotEmpty ?? false;

  late final bool isQrc = () {
    final raw = preferredRawLyrics?.trim() ?? '';
    if (raw.isEmpty) return false;
    return raw.contains('LyricContent=') ||
        _qrcLinePattern.hasMatch(raw) ||
        _qrcWordPattern.hasMatch(raw);
  }();

  late final bool isEnhancedLrc = () {
    final raw = preferredRawLyrics?.trim() ?? '';
    if (raw.isEmpty) return false;
    return _enhancedLrcSegmentPattern.hasMatch(raw);
  }();

  late final String badgeLabel = () {
    if (isQrc) return 'QRC';
    if (isEnhancedLrc) return 'ELRC';
    if (provider.trim().toLowerCase() == 'lrclib') return 'LRCLIB';
    if (provider.trim().toLowerCase() == 'qqmusic') return 'QQ';
    return isSynced ? 'LRC' : 'TXT';
  }();

  late final bool badgeHighlighted =
      isQrc || isEnhancedLrc || hasTimedSegments;
  late final bool badgeEmphasized = isSynced || provider.trim().isNotEmpty;

  late final int byteSize = utf8.encode(preferredRawLyrics ?? '').length;

  OnlineLyricsSearchResult copyWith({int? score, bool? hasTimedSegments}) {
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
      hasTimedSegments: hasTimedSegments ?? this.hasTimedSegments,
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
