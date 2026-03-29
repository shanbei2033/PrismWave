import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:path/path.dart' as p;

import '../models/online_cover_search_result.dart';
import '../models/track.dart';

class OnlineCoverService {
  OnlineCoverService();

  static const String _appleSearchHost = 'itunes.apple.com';
  static const String _musicBrainzHost = 'musicbrainz.org';
  static const String _coverArchiveHost = 'coverartarchive.org';
  static const String _cacheDirName = 'PrismWave';
  static const String _cacheSubDir = 'cover_cache';
  static const String _searchCacheSubDir = 'search_cache';
  static const Duration _searchCacheTtl = Duration(days: 7);

  final HttpClient _httpClient = HttpClient()
    ..connectionTimeout = const Duration(seconds: 8);

  DateTime _lastMusicBrainzRequest = DateTime.fromMillisecondsSinceEpoch(0);

  Future<List<OnlineCoverSearchResult>> searchCoversForTrack(
    Track track, {
    required String query,
  }) async {
    final normalizedQuery = query.trim().isEmpty ? track.title : query.trim();
    final cached = await _loadCachedSearchResults(
      track,
      query: normalizedQuery,
    );
    if (cached.isNotEmpty) {
      return cached;
    }

    final appleResults = await _searchAppleArtwork(
      track,
      query: normalizedQuery,
    );
    final enoughAppleResults =
        appleResults.length >= 6 ||
        (appleResults.isNotEmpty && appleResults.first.score >= 84);

    final fallbackResults = enoughAppleResults
        ? const <OnlineCoverSearchResult>[]
        : await _searchMusicBrainzArtwork(track, query: normalizedQuery);

    final merged = _mergeAndRankResults(
      track,
      normalizedQuery,
      <OnlineCoverSearchResult>[
        ...appleResults,
        ...fallbackResults,
      ],
    );

    if (merged.isNotEmpty) {
      await _saveCachedSearchResults(
        track,
        query: normalizedQuery,
        results: merged,
      );
    }

    return merged;
  }

  Future<({String filePath, Uint8List bytes})> cacheCoverForTrack(
    Track track,
    OnlineCoverSearchResult cover,
  ) async {
    final primaryUrl = cover.thumbnailUrl.isNotEmpty
        ? cover.thumbnailUrl
        : cover.fullImageUrl;
    final bytes =
        await _downloadBytes(primaryUrl) ??
        await _downloadBytes(cover.fullImageUrl);
    if (bytes == null || bytes.isEmpty) {
      throw Exception('Failed to download cover image.');
    }

    final extension = _guessImageExtension(
      contentType: null,
      url: primaryUrl,
      bytes: bytes,
    );
    final directory = await _resolveCacheDirectory();
    await directory.create(recursive: true);
    final key = _stableHash(track.path.toLowerCase());
    final file = File(p.join(directory.path, '$key$extension'));
    await file.writeAsBytes(bytes, flush: true);
    return (filePath: file.path, bytes: bytes);
  }

  Future<List<OnlineCoverSearchResult>> _searchAppleArtwork(
    Track track, {
    required String query,
  }) async {
    final searchTerms = <String>[
      _composeAppleTerm(track, query),
      query,
      if (track.album.trim().isNotEmpty) '${track.album} ${track.artist}',
    ];
    final gathered = <OnlineCoverSearchResult>[];
    final seen = <String>{};

    for (final term in searchTerms) {
      final trimmed = term.trim();
      if (trimmed.isEmpty) continue;

      final uri = Uri.https(_appleSearchHost, '/search', {
        'term': trimmed,
        'media': 'music',
        'entity': 'song',
        'limit': '14',
        'lang': 'zh_cn',
      });
      final payload = await _requestJson(uri);
      if (payload is! Map<String, dynamic>) continue;
      final results = payload['results'];
      if (results is! List) continue;

      for (final raw in results.whereType<Map>()) {
        final map = Map<String, dynamic>.from(raw);
        final artwork100 = map['artworkUrl100']?.toString() ?? '';
        final title = map['trackName']?.toString().trim() ?? '';
        final artist = map['artistName']?.toString().trim() ?? '';
        if (artwork100.isEmpty || title.isEmpty || artist.isEmpty) continue;

        final fullUrl = _upgradeAppleArtworkUrl(artwork100, size: 1200);
        final thumbUrl = _upgradeAppleArtworkUrl(artwork100, size: 300);
        final dedupeKey = '$title|$artist|$fullUrl';
        if (!seen.add(dedupeKey)) continue;

        gathered.add(
          OnlineCoverSearchResult(
            id: 'apple:${map['trackId'] ?? map['collectionId'] ?? fullUrl}',
            title: title,
            artist: artist,
            album: map['collectionName']?.toString().trim() ?? '',
            thumbnailUrl: thumbUrl,
            fullImageUrl: fullUrl,
            source: 'apple',
          ),
        );
      }

      if (gathered.length >= 10) break;
    }

    return _mergeAndRankResults(track, query, gathered);
  }

  Future<List<OnlineCoverSearchResult>> _searchMusicBrainzArtwork(
    Track track, {
    required String query,
  }) async {
    final groups = await _searchReleaseGroups(
      _composeMusicBrainzTerm(track, query),
    );
    if (groups.isEmpty) return const <OnlineCoverSearchResult>[];

    final limited = groups.take(8).toList(growable: false);
    final covers = await Future.wait(
      limited.map(
        (group) => _resolveCoverForReleaseGroup(
          id: group.id,
          title: group.title,
          artist: group.artist,
          album: group.album,
        ),
      ),
    );

    return _mergeAndRankResults(
      track,
      query,
      covers.whereType<OnlineCoverSearchResult>().toList(growable: false),
    );
  }

  Future<List<_ReleaseGroupCandidate>> _searchReleaseGroups(String query) async {
    await _waitForMusicBrainzSlot();
    final uri = Uri.https(_musicBrainzHost, '/ws/2/release-group', {
      'query': query,
      'fmt': 'json',
      'limit': '10',
    });
    final payload = await _requestJson(uri);
    if (payload is! Map<String, dynamic>) {
      return const <_ReleaseGroupCandidate>[];
    }

    final rawGroups = payload['release-groups'];
    if (rawGroups is! List) return const <_ReleaseGroupCandidate>[];

    return rawGroups
        .whereType<Map>()
        .map((raw) => Map<String, dynamic>.from(raw))
        .map(_ReleaseGroupCandidate.fromMusicBrainzJson)
        .where((group) => group.id.isNotEmpty && group.title.isNotEmpty)
        .toList(growable: false);
  }

  Future<OnlineCoverSearchResult?> _resolveCoverForReleaseGroup({
    required String id,
    required String title,
    required String artist,
    required String album,
  }) async {
    final uri = Uri.https(_coverArchiveHost, '/release-group/$id');
    final payload = await _requestJson(uri);
    if (payload is! Map<String, dynamic>) return null;

    final images = payload['images'];
    if (images is! List) return null;

    for (final image in images.whereType<Map>()) {
      final item = Map<String, dynamic>.from(image);
      final approved = item['approved'] != false;
      final isFront = item['front'] == true;
      if (!approved || !isFront) continue;

      final thumbnails = item['thumbnails'];
      String? thumb;
      if (thumbnails is Map) {
        thumb =
            thumbnails['500']?.toString() ??
            thumbnails['250']?.toString() ??
            thumbnails['small']?.toString();
      }
      final imageUrl = item['image']?.toString();
      if ((thumb ?? '').isEmpty || (imageUrl ?? '').isEmpty) {
        continue;
      }

      return OnlineCoverSearchResult(
        id: id,
        title: title,
        artist: artist,
        album: album,
        thumbnailUrl: thumb!,
        fullImageUrl: imageUrl!,
        source: 'musicbrainz',
      );
    }

    return null;
  }

  List<OnlineCoverSearchResult> _mergeAndRankResults(
    Track track,
    String query,
    List<OnlineCoverSearchResult> results,
  ) {
    if (results.isEmpty) return const <OnlineCoverSearchResult>[];

    final deduped = <String, OnlineCoverSearchResult>{};
    for (final cover in results) {
      final key =
          '${_normalize(cover.title)}|${_normalize(cover.artist)}|${cover.fullImageUrl}';
      deduped.putIfAbsent(key, () => cover);
    }

    final scored = deduped.values
        .map(
          (cover) => cover.copyWith(
            score: _scoreResult(cover, query: query, track: track),
          ),
        )
        .toList(growable: false)
      ..sort((a, b) {
        final scoreCompare = b.score.compareTo(a.score);
        if (scoreCompare != 0) return scoreCompare;
        final titleCompare = a.title.compareTo(b.title);
        if (titleCompare != 0) return titleCompare;
        return a.artist.compareTo(b.artist);
      });

    return scored.take(18).toList(growable: false);
  }

  Future<void> _waitForMusicBrainzSlot() async {
    final now = DateTime.now();
    final delta = now.difference(_lastMusicBrainzRequest);
    const minInterval = Duration(milliseconds: 1100);
    if (delta < minInterval) {
      await Future<void>.delayed(minInterval - delta);
    }
    _lastMusicBrainzRequest = DateTime.now();
  }

  Future<dynamic> _requestJson(Uri uri) async {
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

  Future<Uint8List?> _downloadBytes(String url) async {
    try {
      final uri = Uri.parse(url);
      final request = await _httpClient.getUrl(uri);
      request.headers.set(
        HttpHeaders.userAgentHeader,
        'PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)',
      );
      final response = await request.close();
      if (response.statusCode < 200 || response.statusCode >= 300) {
        return null;
      }
      final bytes = await consolidateHttpClientResponseBytes(response);
      return Uint8List.fromList(bytes);
    } catch (_) {
      return null;
    }
  }

  int _scoreResult(
    OnlineCoverSearchResult result, {
    required String query,
    required Track track,
  }) {
    var score = 0;
    final queryKey = _normalize(query);
    final titleKey = _normalize(track.title);
    final artistKey = _normalize(track.artist);
    final albumKey = _normalize(track.album);
    final resultTitleKey = _normalize(result.title);
    final resultArtistKey = _normalize(result.artist);
    final resultAlbumKey = _normalize(result.album);

    if (titleKey.isNotEmpty && titleKey == resultTitleKey) {
      score += 52;
    } else if (titleKey.isNotEmpty &&
        (resultTitleKey.contains(titleKey) || titleKey.contains(resultTitleKey))) {
      score += 30;
    }

    if (artistKey.isNotEmpty && artistKey == resultArtistKey) {
      score += 38;
    } else if (artistKey.isNotEmpty &&
        (resultArtistKey.contains(artistKey) ||
            artistKey.contains(resultArtistKey))) {
      score += 18;
    }

    if (albumKey.isNotEmpty && albumKey == resultAlbumKey) {
      score += 18;
    } else if (albumKey.isNotEmpty &&
        (resultAlbumKey.contains(albumKey) || albumKey.contains(resultAlbumKey))) {
      score += 8;
    }

    if (queryKey.isNotEmpty &&
        (resultTitleKey.contains(queryKey) ||
            queryKey.contains(resultTitleKey) ||
            resultAlbumKey.contains(queryKey))) {
      score += 10;
    }

    if (result.source == 'apple') {
      score += 12;
    } else if (result.source == 'musicbrainz') {
      score += 4;
    }

    return score;
  }

  String _composeAppleTerm(Track track, String query) {
    final parts = <String>[
      query,
      if (track.artist.trim().isNotEmpty &&
          !_normalize(query).contains(_normalize(track.artist)))
        track.artist,
    ];
    return parts.join(' ').trim();
  }

  String _composeMusicBrainzTerm(Track track, String query) {
    final parts = <String>[
      query,
      if (track.artist.trim().isNotEmpty) track.artist,
      if (track.album.trim().isNotEmpty) track.album,
    ];
    return parts.join(' ').trim();
  }

  String _upgradeAppleArtworkUrl(String input, {int size = 1200}) {
    return input.replaceFirstMapped(
      RegExp(r'(\d{2,4})x(\d{2,4})bb'),
      (_) => '${size}x${size}bb',
    );
  }

  String _normalize(String input) {
    return input
        .toLowerCase()
        .replaceAll(RegExp(r'\[[^\]]*\]'), '')
        .replaceAll(RegExp(r'\([^)]*\)'), '')
        .replaceAll(RegExp(r'feat\.?|ft\.?|ver\.?|version|live|remix'), '')
        .replaceAll(RegExp(r'[^a-z0-9\u4e00-\u9fff]+'), '');
  }

  Future<List<OnlineCoverSearchResult>> _loadCachedSearchResults(
    Track track, {
    required String query,
  }) async {
    final file = await _resolveSearchCacheFile(track, query: query);
    if (!file.existsSync()) return const <OnlineCoverSearchResult>[];

    try {
      final raw = jsonDecode(await file.readAsString()) as Map<String, dynamic>;
      final timestamp = DateTime.tryParse(raw['savedAt']?.toString() ?? '');
      if (timestamp == null ||
          DateTime.now().difference(timestamp) > _searchCacheTtl) {
        return const <OnlineCoverSearchResult>[];
      }
      final list = raw['results'];
      if (list is! List) return const <OnlineCoverSearchResult>[];
      return list
          .whereType<Map>()
          .map(
            (item) => OnlineCoverSearchResult.fromJson(
              Map<String, dynamic>.from(item),
            ),
          )
          .where(
            (item) =>
                item.thumbnailUrl.trim().isNotEmpty &&
                item.fullImageUrl.trim().isNotEmpty,
          )
          .toList(growable: false);
    } catch (_) {
      return const <OnlineCoverSearchResult>[];
    }
  }

  Future<void> _saveCachedSearchResults(
    Track track, {
    required String query,
    required List<OnlineCoverSearchResult> results,
  }) async {
    if (results.isEmpty) return;
    final file = await _resolveSearchCacheFile(track, query: query);
    await file.parent.create(recursive: true);
    final payload = <String, dynamic>{
      'savedAt': DateTime.now().toIso8601String(),
      'results': results.map((item) => item.toJson()).toList(growable: false),
    };
    await file.writeAsString(jsonEncode(payload), flush: true);
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

  Future<File> _resolveSearchCacheFile(
    Track track, {
    required String query,
  }) async {
    final directory = await _resolveCacheDirectory();
    final searchDirectory = Directory(p.join(directory.path, _searchCacheSubDir));
    final key = _stableHash(
      '${track.path.toLowerCase()}::${_normalize(query)}::${_normalize(track.artist)}::${_normalize(track.album)}',
    );
    return File(p.join(searchDirectory.path, '$key.json'));
  }

  String _stableHash(String input) {
    var hash = 0xcbf29ce484222325;
    for (final codeUnit in utf8.encode(input)) {
      hash ^= codeUnit;
      hash = (hash * 0x100000001b3) & 0x7fffffffffffffff;
    }
    return hash.toRadixString(16);
  }

  String _guessImageExtension({
    required String? contentType,
    required String url,
    required Uint8List bytes,
  }) {
    final lowerUrl = url.toLowerCase();
    if (lowerUrl.endsWith('.png')) return '.png';
    if (lowerUrl.endsWith('.webp')) return '.webp';
    if (contentType == 'image/png') return '.png';
    if (contentType == 'image/webp') return '.webp';
    if (bytes.length >= 12 &&
        bytes[0] == 0x52 &&
        bytes[1] == 0x49 &&
        bytes[2] == 0x46 &&
        bytes[3] == 0x46 &&
        bytes[8] == 0x57 &&
        bytes[9] == 0x45 &&
        bytes[10] == 0x42 &&
        bytes[11] == 0x50) {
      return '.webp';
    }
    if (bytes.length >= 8 &&
        bytes[0] == 0x89 &&
        bytes[1] == 0x50 &&
        bytes[2] == 0x4E &&
        bytes[3] == 0x47) {
      return '.png';
    }
    return '.jpg';
  }
}

class _ReleaseGroupCandidate {
  const _ReleaseGroupCandidate({
    required this.id,
    required this.title,
    required this.artist,
    required this.album,
  });

  final String id;
  final String title;
  final String artist;
  final String album;

  factory _ReleaseGroupCandidate.fromMusicBrainzJson(
    Map<String, dynamic> json,
  ) {
    final artistCredits = json['artist-credit'];
    final artists = <String>[];
    if (artistCredits is List) {
      for (final item in artistCredits.whereType<Map>()) {
        final map = Map<String, dynamic>.from(item);
        final name = map['name']?.toString().trim();
        if (name != null && name.isNotEmpty) {
          artists.add(name);
        }
      }
    }

    return _ReleaseGroupCandidate(
      id: json['id']?.toString() ?? '',
      title: json['title']?.toString().trim() ?? '',
      artist: artists.join(', '),
      album: json['title']?.toString().trim() ?? '',
    );
  }
}

Future<List<int>> consolidateHttpClientResponseBytes(
  HttpClientResponse response,
) async {
  final builder = BytesBuilder(copy: false);
  await for (final chunk in response) {
    builder.add(chunk);
  }
  return builder.takeBytes();
}
