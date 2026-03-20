import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:path/path.dart' as p;

import '../models/lyrics_document.dart';
import '../models/online_lyrics_search_result.dart';
import '../models/track.dart';
import 'lyrics_reader.dart';

class OnlineLyricsService {
  OnlineLyricsService();

  static const String _provider = 'lrclib';
  static const String _host = 'lrclib.net';
  static const String _cacheDirName = 'PrismWave';
  static const String _cacheSubDir = 'lyrics_cache';

  final HttpClient _httpClient = HttpClient()..connectionTimeout = const Duration(seconds: 6);

  Future<LyricsDocument?> loadCachedLyricsForTrack(
    Track track, {
    Duration? durationHint,
  }) async {
    final file = await _resolveCacheFile(track);
    if (!file.existsSync()) return null;

    try {
      final raw = jsonDecode(await file.readAsString()) as Map<String, dynamic>;
      final document = LyricsDocument.fromCacheJson(raw);
      if (!document.isEmpty) return document;
      if ((document.rawText ?? '').trim().isNotEmpty) {
        final reparsed = parseLyricsDocument(
          document.rawText!,
          durationHint: durationHint,
        );
        if (reparsed != null && !reparsed.isEmpty) return reparsed;
      }
    } catch (_) {
      // Ignore broken cache and let online fetch refill it.
    }

    return null;
  }

  Future<void> saveCachedLyricsForTrack(
    Track track,
    LyricsDocument document,
  ) async {
    if (document.isEmpty) return;
    final file = await _resolveCacheFile(track);
    await file.parent.create(recursive: true);
    await file.writeAsString(jsonEncode(document.toCacheJson()), flush: true);
  }

  Future<LyricsDocument?> fetchBestLyricsForTrack(
    Track track, {
    Duration? durationHint,
  }) async {
    final exact = await _getExactLyrics(track, durationHint: durationHint);
    if (exact != null) return exact;

    final results = await searchLyricsForTrack(
      track,
      query: track.title,
      durationHint: durationHint,
    );
    for (final result in results) {
      final document = _toDocument(result, durationHint: durationHint);
      if (document != null && !document.isEmpty) {
        return document;
      }
    }
    return null;
  }

  Future<List<OnlineLyricsSearchResult>> searchLyricsForTrack(
    Track track, {
    required String query,
    Duration? durationHint,
  }) async {
    final results = await _search(
      <String, String>{'q': query.trim()},
    );
    final scored = results
        .where((item) => item.hasLyrics && !item.instrumental)
        .map(
          (item) => item.copyWith(
            score: _scoreResult(
              item,
              query: query,
              track: track,
              durationHint: durationHint,
            ),
          ),
        )
        .toList(growable: false)
      ..sort((a, b) {
        final scoreCompare = b.score.compareTo(a.score);
        if (scoreCompare != 0) return scoreCompare;
        return a.byteSize.compareTo(b.byteSize);
      });
    return scored;
  }

  Future<LyricsDocument?> _getExactLyrics(
    Track track, {
    Duration? durationHint,
  }) async {
    final params = <String, String>{
      'track_name': track.title,
      'artist_name': track.artist,
      if (track.album.trim().isNotEmpty) 'album_name': track.album,
      if (durationHint != null && durationHint > Duration.zero)
        'duration': durationHint.inSeconds.toString(),
    };
    final raw = await _requestJson('/api/get', params);
    if (raw is! Map<String, dynamic>) return null;
    final result = OnlineLyricsSearchResult.fromJson(raw, provider: _provider);
    return _toDocument(result, durationHint: durationHint);
  }

  Future<List<OnlineLyricsSearchResult>> _search(Map<String, String> params) async {
    final raw = await _requestJson('/api/search', params);
    if (raw is! List) return const <OnlineLyricsSearchResult>[];
    return raw
        .whereType<Map>()
        .map(
          (item) => OnlineLyricsSearchResult.fromJson(
            Map<String, dynamic>.from(item),
            provider: _provider,
          ),
        )
        .where((item) => item.hasLyrics)
        .toList(growable: false);
  }

  Future<dynamic> _requestJson(String path, Map<String, String> params) async {
    final uri = Uri.https(_host, path, params);
    try {
      final request = await _httpClient.getUrl(uri);
      request.headers.set(HttpHeaders.acceptHeader, 'application/json');
      request.headers.set(
        HttpHeaders.userAgentHeader,
        'PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)',
      );
      final response = await request.close();
      if (response.statusCode < 200 || response.statusCode >= 300) {
        return null;
      }

      final bytes = await consolidateHttpClientResponseBytes(response);
      final body = utf8.decode(bytes, allowMalformed: true);
      return jsonDecode(body);
    } catch (_) {
      return null;
    }
  }

  LyricsDocument? _toDocument(
    OnlineLyricsSearchResult result, {
    Duration? durationHint,
  }) {
    final raw = result.preferredRawLyrics;
    if (raw == null || raw.trim().isEmpty) return null;
    final parsed = parseLyricsDocument(raw, durationHint: durationHint);
    if (parsed == null || parsed.isEmpty) return null;
    return result.toLyricsDocument(parsed.lines).copyWithParsed(
      rawText: raw,
      isSynced: parsed.isSynced,
    );
  }

  int _scoreResult(
    OnlineLyricsSearchResult result, {
    required String query,
    required Track track,
    Duration? durationHint,
  }) {
    var score = 0;

    final queryKey = _normalize(query);
    final titleKey = _normalize(track.title);
    final artistKey = _normalize(track.artist);
    final albumKey = _normalize(track.album);
    final resultTitleKey = _normalize(result.title);
    final resultArtistKey = _normalize(result.artist);
    final resultAlbumKey = _normalize(result.album);

    if (result.instrumental) score -= 1000;
    if (result.isSynced) score += 10;

    if (titleKey.isNotEmpty && titleKey == resultTitleKey) {
      score += 50;
    } else if (titleKey.isNotEmpty &&
        (resultTitleKey.contains(titleKey) || titleKey.contains(resultTitleKey))) {
      score += 24;
    }

    if (artistKey.isNotEmpty && artistKey == resultArtistKey) {
      score += 35;
    } else if (artistKey.isNotEmpty &&
        (resultArtistKey.contains(artistKey) ||
            artistKey.contains(resultArtistKey))) {
      score += 16;
    }

    if (albumKey.isNotEmpty && albumKey == resultAlbumKey) {
      score += 12;
    }

    if (queryKey.isNotEmpty &&
        (resultTitleKey.contains(queryKey) || queryKey.contains(resultTitleKey))) {
      score += 12;
    }

    final durationSeconds =
        durationHint != null && durationHint > Duration.zero
        ? durationHint.inSeconds
        : 0;
    if (durationSeconds > 0 && result.durationSeconds > 0) {
      final delta = (result.durationSeconds - durationSeconds).abs();
      if (delta <= 2) {
        score += 16;
      } else if (delta <= 5) {
        score += 10;
      } else if (delta <= 10) {
        score += 5;
      }
    }

    return score;
  }

  String _normalize(String input) {
    return input
        .toLowerCase()
        .replaceAll(RegExp(r'\[[^\]]*\]'), '')
        .replaceAll(RegExp(r'\([^)]*\)'), '')
        .replaceAll(RegExp(r'feat\.?|ft\.?|ver\.?|version|live|remix'), '')
        .replaceAll(RegExp(r'[^a-z0-9\u4e00-\u9fff]+'), '');
  }

  Future<File> _resolveCacheFile(Track track) async {
    final directory = await _resolveCacheDirectory();
    final key = _stableHash(track.path.toLowerCase());
    return File(p.join(directory.path, '$key.json'));
  }

  Future<Directory> _resolveCacheDirectory() async {
    final localAppData = Platform.environment['LOCALAPPDATA'];
    if (localAppData != null && localAppData.isNotEmpty) {
      return Directory(p.join(localAppData, _cacheDirName, _cacheSubDir));
    }

    final userProfile = Platform.environment['USERPROFILE'];
    if (userProfile != null && userProfile.isNotEmpty) {
      return Directory(
        p.join(userProfile, 'Documents', _cacheDirName, _cacheSubDir),
      );
    }

    return Directory(p.join(Directory.current.path, _cacheSubDir));
  }

  String _stableHash(String input) {
    var hash = 0xcbf29ce484222325;
    for (final codeUnit in utf8.encode(input)) {
      hash ^= codeUnit;
      hash = (hash * 0x100000001b3) & 0x7fffffffffffffff;
    }
    return hash.toRadixString(16);
  }
}

extension on LyricsDocument {
  LyricsDocument copyWithParsed({
    required String rawText,
    required bool isSynced,
  }) {
    return LyricsDocument(
      lines: lines,
      isSynced: isSynced,
      rawText: rawText,
      provider: provider,
      remoteId: remoteId,
      title: title,
      artist: artist,
      album: album,
      byteSize: byteSize,
    );
  }
}

Future<List<int>> consolidateHttpClientResponseBytes(HttpClientResponse response) async {
  final chunks = <int>[];
  await for (final chunk in response) {
    chunks.addAll(chunk);
  }
  return chunks;
}
