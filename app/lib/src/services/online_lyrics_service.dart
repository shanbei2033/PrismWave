import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:fast_gbk/fast_gbk.dart';
import 'package:path/path.dart' as p;

import '../models/lyrics_document.dart';
import '../models/online_lyrics_search_result.dart';
import '../models/track.dart';
import 'lyrics_reader.dart';

class OnlineLyricsService {
  OnlineLyricsService();

  static const String _provider = 'lrclib';
  static const String _host = 'lrclib.net';
  static const String _qqProvider = 'qqmusic';
  static const String _qqHost = 'api.ygking.top';
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
    final results = await searchLyricsForTrack(
      track,
      query: track.title,
      durationHint: durationHint,
    );
    if (results.isNotEmpty) {
      final document = _toDocument(results.first, durationHint: durationHint);
      if (document != null && !document.isEmpty) return document;
    }
    return null;
  }

  Future<List<OnlineLyricsSearchResult>> searchLyricsForTrack(
    Track track, {
    required String query,
    Duration? durationHint,
  }) async {
    final lrclibFuture = _searchLrclib(track, query: query, durationHint: durationHint);
    final qqFuture = _searchQq(track, query: query, durationHint: durationHint);
    final resultGroups = await Future.wait([lrclibFuture, qqFuture]);
    final merged = <OnlineLyricsSearchResult>[
      ...resultGroups[0],
      ...resultGroups[1],
    ];
    merged.sort((a, b) {
      final scoreCompare = b.score.compareTo(a.score);
      if (scoreCompare != 0) return scoreCompare;
      if (a.hasTimedSegments != b.hasTimedSegments) {
        return a.hasTimedSegments ? -1 : 1;
      }
      return a.byteSize.compareTo(b.byteSize);
    });
    return _deduplicateResults(merged);
  }

  Future<List<OnlineLyricsSearchResult>> _searchLrclib(
    Track track, {
    required String query,
    Duration? durationHint,
  }) async {
    final exact = await _getExactLyrics(track, durationHint: durationHint);
    final results = await _search(
      <String, String>{'q': query.trim()},
    );
    final pool = <OnlineLyricsSearchResult>[
      if (exact != null && !exact.isEmpty)
        OnlineLyricsSearchResult(
          id: exact.remoteId ?? 0,
          title: exact.title ?? track.title,
          artist: exact.artist ?? track.artist,
          album: exact.album ?? track.album,
          durationSeconds: durationHint?.inSeconds.toDouble() ?? 0,
          instrumental: false,
          syncedLyrics: exact.isSynced ? exact.rawText : null,
          plainLyrics: exact.isSynced ? null : exact.rawText,
          provider: exact.provider ?? _provider,
          hasTimedSegments: exact.hasTimedSegments,
        ),
      ...results,
    ];

    return pool
        .where((item) => item.hasLyrics && !item.instrumental)
        .map(
          (item) => item.copyWith(
            hasTimedSegments: _toDocument(item, durationHint: durationHint)?.hasTimedSegments ?? item.hasTimedSegments,
            score: _scoreResult(
              item,
              query: query,
              track: track,
              durationHint: durationHint,
            ),
          ),
        )
        .toList(growable: false);
  }

  Future<List<OnlineLyricsSearchResult>> _searchQq(
    Track track, {
    required String query,
    Duration? durationHint,
  }) async {
    final raw = await _requestJsonFromHost(
      _qqHost,
      '/api/search',
      <String, String>{
        'keyword': query.trim(),
        'type': 'song',
        'num': '8',
      },
    );
    final items = _extractQqSearchItems(raw);
    if (items.isEmpty) return const <OnlineLyricsSearchResult>[];

    final candidates = items
        .map((item) => _parseQqSearchCandidate(item))
        .whereType<_QqSongCandidate>()
        .toList(growable: false);
    if (candidates.isEmpty) return const <OnlineLyricsSearchResult>[];

    final narrowed = candidates
        .map(
          (candidate) => (
            candidate: candidate,
            score: _scoreQqCandidate(
              candidate,
              query: query,
              track: track,
              durationHint: durationHint,
            ),
          ),
        )
        .where((entry) => entry.score > 0)
        .toList(growable: false)
      ..sort((a, b) => b.score.compareTo(a.score));

    final top = narrowed.take(5).map((entry) => entry.candidate).toList(growable: false);
    final fetched = await Future.wait(
      top.map((candidate) => _fetchQqLyricsCandidate(candidate, durationHint: durationHint)),
    );

    return fetched
        .whereType<OnlineLyricsSearchResult>()
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
        .toList(growable: false);
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
    return _requestJsonFromHost(_host, path, params);
  }

  Future<dynamic> _requestJsonFromHost(
    String host,
    String path,
    Map<String, String> params,
  ) async {
    final uri = Uri.https(host, path, params);
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
      final body = host == _qqHost
          ? gbk.decode(bytes, allowMalformed: true)
          : utf8.decode(bytes, allowMalformed: true);
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
    final enriched = result.copyWith(
      hasTimedSegments: parsed.hasTimedSegments,
    );
    return enriched.toLyricsDocument(parsed.lines).copyWithParsed(
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
    if (result.hasTimedSegments) score += 24;

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

  List<dynamic> _extractQqSearchItems(dynamic raw) {
    if (raw is List) return raw;
    if (raw is Map) {
      final data = raw['data'];
      if (data is List) return data;
      if (data is Map) {
        if (data['list'] is List) return data['list'] as List;
        if (data['song'] is List) return data['song'] as List;
        if (data['song']['list'] is List) {
          return data['song']['list'] as List;
        }
      }
      if (raw['result'] is List) return raw['result'] as List;
    }
    return const <dynamic>[];
  }

  _QqSongCandidate? _parseQqSearchCandidate(dynamic raw) {
    if (raw is! Map) return null;
    final map = Map<String, dynamic>.from(raw);
    final mid = map['mid']?.toString() ??
        map['songmid']?.toString() ??
        map['musicrid']?.toString();
    if (mid == null || mid.isEmpty) return null;

    final title = map['name']?.toString() ??
        map['songname']?.toString() ??
        map['title']?.toString() ??
        '';
    if (title.trim().isEmpty) return null;

    final artist = _parseQqArtists(map);
    final album = _parseQqAlbum(map);
    final durationSeconds = _parseQqDurationSeconds(map);
    return _QqSongCandidate(
      mid: mid,
      title: title,
      artist: artist,
      album: album,
      durationSeconds: durationSeconds,
    );
  }

  Future<OnlineLyricsSearchResult?> _fetchQqLyricsCandidate(
    _QqSongCandidate candidate, {
    Duration? durationHint,
  }) async {
    final raw = await _requestJsonFromHost(
      _qqHost,
      '/api/lyric',
      <String, String>{
        'mid': candidate.mid,
        'qrc': '1',
        'trans': '1',
      },
    );
    if (raw is! Map) return null;
    final map = Map<String, dynamic>.from(raw);
    final data = map['data'] is Map ? Map<String, dynamic>.from(map['data']) : null;
    final lyric = map['qrc']?.toString() ??
        map['lyric']?.toString() ??
        data?['qrc']?.toString() ??
        data?['lyric']?.toString();
    if (lyric == null || lyric.trim().isEmpty) return null;

    final parsed = parseLyricsDocument(lyric, durationHint: durationHint);
    if (parsed == null || parsed.isEmpty) return null;

    return OnlineLyricsSearchResult(
      id: candidate.mid.hashCode,
      title: candidate.title,
      artist: candidate.artist,
      album: candidate.album,
      durationSeconds: candidate.durationSeconds,
      instrumental: false,
      syncedLyrics: lyric,
      plainLyrics: null,
      provider: _qqProvider,
      hasTimedSegments: parsed.hasTimedSegments,
    );
  }

  int _scoreQqCandidate(
    _QqSongCandidate candidate, {
    required String query,
    required Track track,
    Duration? durationHint,
  }) {
    return _scoreResult(
      OnlineLyricsSearchResult(
        id: candidate.mid.hashCode,
        title: candidate.title,
        artist: candidate.artist,
        album: candidate.album,
        durationSeconds: candidate.durationSeconds,
        instrumental: false,
        syncedLyrics: '',
        plainLyrics: null,
        provider: _qqProvider,
      ),
      query: query,
      track: track,
      durationHint: durationHint,
    );
  }

  String _parseQqArtists(Map<String, dynamic> map) {
    final singer = map['singer'];
    if (singer is List) {
      final names = singer
          .whereType<Map>()
          .map((item) => item['name']?.toString() ?? '')
          .where((name) => name.trim().isNotEmpty)
          .toList(growable: false);
      if (names.isNotEmpty) return names.join(' / ');
    }

    return map['artist']?.toString() ??
        map['singername']?.toString() ??
        map['author_simple']?.toString() ??
        'Unknown Artist';
  }

  String _parseQqAlbum(Map<String, dynamic> map) {
    final album = map['album'];
    if (album is Map) {
      final name = album['name']?.toString();
      if (name != null && name.trim().isNotEmpty) return name;
    }
    return map['albumName']?.toString() ??
        map['albumname']?.toString() ??
        map['album_name']?.toString() ??
        '';
  }

  double _parseQqDurationSeconds(Map<String, dynamic> map) {
    final raw = (map['interval'] as num?)?.toDouble() ??
        (map['duration'] as num?)?.toDouble() ??
        (map['songtime'] as num?)?.toDouble() ??
        0;
    if (raw > 10000) return raw / 1000;
    return raw;
  }

  List<OnlineLyricsSearchResult> _deduplicateResults(
    List<OnlineLyricsSearchResult> results,
  ) {
    final bestByKey = <String, OnlineLyricsSearchResult>{};
    for (final result in results) {
      final key =
          '${result.provider}|${_normalize(result.title)}|${_normalize(result.artist)}';
      final current = bestByKey[key];
      if (current == null) {
        bestByKey[key] = result;
        continue;
      }
      if (result.score > current.score ||
          (result.score == current.score &&
              result.hasTimedSegments &&
              !current.hasTimedSegments)) {
        bestByKey[key] = result;
      }
    }
    return bestByKey.values.toList(growable: false)
      ..sort((a, b) => b.score.compareTo(a.score));
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

class _QqSongCandidate {
  const _QqSongCandidate({
    required this.mid,
    required this.title,
    required this.artist,
    required this.album,
    required this.durationSeconds,
  });

  final String mid;
  final String title;
  final String artist;
  final String album;
  final double durationSeconds;
}
