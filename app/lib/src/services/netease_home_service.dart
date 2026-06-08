import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart' show rootBundle;
import 'package:path/path.dart' as p;

import '../models/online_recommendation.dart';
import 'netease_endpoints.dart';

const Duration _kPerRequestTimeout = Duration(seconds: 7);

enum OnlineHomeErrorKind {
  noNetwork,
  cloudTimeout,
  unavailable,
  invalidPayload,
}

class OnlineHomeException implements Exception {
  const OnlineHomeException(this.kind, [this.message]);

  final OnlineHomeErrorKind kind;
  final String? message;

  @override
  String toString() => message ?? 'OnlineHomeException($kind)';
}

class OnlineHomeBundle {
  const OnlineHomeBundle({
    required this.data,
    required this.usedCache,
    required this.cachedAt,
    this.needsBackgroundRefresh = false,
    this.recommendationsUnavailable = false,
  });

  final OnlineHomeData data;
  final bool usedCache;
  final DateTime cachedAt;
  final bool needsBackgroundRefresh;
  final bool recommendationsUnavailable;
}

/// Pulls home recommendations from the generated prismwave-hits daily payload.
/// Song sections come from:
///
/// - https://raw.githubusercontent.com/shanbei2033/prismwave-hits/main/home/latest_home.json
///
/// Results are cached by Beijing `editionDate`. The app uses today's cache
/// when present, fetches the remote payload when today's cache is missing, and
/// falls back only to yesterday's cache with a UI warning when the remote
/// payload is unavailable.
class NeteaseHomeService {
  NeteaseHomeService({HttpClient? httpClient})
    : _httpClient =
          httpClient ??
          (HttpClient()..connectionTimeout = const Duration(seconds: 6));

  final HttpClient _httpClient;

  static const int _kSchemaVersion = 7;
  static const int _coverSearchConcurrency = 4;
  static const String _bundledHomeAsset = 'assets/home/latest_home.json';
  static final Uri _remoteHomeUri = Uri.https(
    'raw.githubusercontent.com',
    '/shanbei2033/prismwave-hits/main/home/latest_home.json',
  );
  static const Map<String, String> _remoteHomeHeaders = <String, String>{
    HttpHeaders.userAgentHeader: 'Mozilla/5.0 PrismWave/1.0.0',
    HttpHeaders.acceptHeader: 'application/json',
  };

  Future<OnlineHomeBundle> loadBundle({
    bool forceRefresh = false,
    bool allowStaleCache = false,
  }) async {
    if (!forceRefresh) {
      final cached = await loadCachedBundle(allowStale: allowStaleCache);
      if (cached != null) {
        return cached;
      }
    }

    try {
      final data = await _fetchFresh();
      final cachedAt = DateTime.now().toUtc();
      await _writeCache(data, cachedAt);
      return OnlineHomeBundle(data: data, usedCache: false, cachedAt: cachedAt);
    } catch (error) {
      final cached = await loadYesterdayCachedBundle();
      if (cached != null) return cached;
      final bundled = await loadBundledFallbackBundle();
      if (bundled != null) return bundled;
      if (error is OnlineHomeException) rethrow;
      throw OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        error.toString(),
      );
    }
  }

  Future<OnlineHomeBundle?> loadCachedBundle({bool allowStale = false}) async {
    final cached = await _loadCachedForDate(_todayIsoDate());
    if (cached == null) return null;
    if (allowStale || _isFresh(cached)) return cached;
    return null;
  }

  Future<OnlineHomeBundle?> loadYesterdayCachedBundle() async {
    final cached = await _loadCachedForDate(_yesterdayIsoDate());
    if (cached == null) return null;
    return OnlineHomeBundle(
      data: cached.data,
      usedCache: true,
      cachedAt: cached.cachedAt,
      recommendationsUnavailable: true,
    );
  }

  Future<OnlineHomeBundle?> loadBundledFallbackBundle() async {
    try {
      final body = await rootBundle.loadString(_bundledHomeAsset);
      final decoded = jsonDecode(body);
      if (decoded is! Map<String, dynamic>) return null;
      final data = OnlineHomeData.fromJson(decoded);
      if (!_isUsableDailyHome(data)) return null;
      return OnlineHomeBundle(
        data: data,
        usedCache: true,
        cachedAt: data.generatedAt,
        recommendationsUnavailable: true,
      );
    } catch (_) {
      return null;
    }
  }

  bool isFreshBundle(OnlineHomeBundle bundle) => _isFresh(bundle);

  bool isFreshData(OnlineHomeData data) =>
      data.editionDate.trim() == _todayIsoDate();

  bool needsMainlandCoverFallbacks(OnlineHomeData data) {
    bool sectionNeedsFallback(OnlineSection? section) {
      if (section == null) return false;
      return section.tracks.any(
        (track) => _needsMainlandCoverFallback(track.coverUrl),
      );
    }

    if (sectionNeedsFallback(data.topPlaylist)) return true;
    return data.sections.any(sectionNeedsFallback);
  }

  Future<OnlineHomeBundle> enrichMainlandCoverFallbacks(
    OnlineHomeData data,
  ) async {
    final enriched = await _withMainlandCoverFallbacks(data);
    final cachedAt = DateTime.now().toUtc();
    await _writeCache(enriched, cachedAt);
    return OnlineHomeBundle(
      data: enriched,
      usedCache: false,
      cachedAt: cachedAt,
    );
  }

  Future<OnlineHomeBundle> loadRemoteDailyBundle() async {
    final remoteHome = await _loadRemoteDailyHome();
    if (remoteHome == null || !_isUsableDailyHome(remoteHome)) {
      throw const OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        'Remote daily home payload is unavailable',
      );
    }
    final data = _mergeDailyHome(remoteHome, const <OnlineAlbumCard>[]);
    final cachedAt = DateTime.now().toUtc();
    await _writeCache(data, cachedAt);
    if (!isFreshData(data)) {
      throw OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        'Remote daily home payload is stale: ${data.editionDate}',
      );
    }
    return OnlineHomeBundle(
      data: data,
      usedCache: false,
      cachedAt: cachedAt,
    );
  }

  Future<OnlineHomeBundle> refreshLiveHome() async {
    final remoteHome = await _loadRemoteDailyHome();
    if (remoteHome == null || !_isUsableDailyHome(remoteHome)) {
      throw const OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        'Remote daily home payload is unavailable',
      );
    }
    final data = _mergeDailyHome(remoteHome, const <OnlineAlbumCard>[]);
    if (!isFreshData(data)) {
      await _writeCache(data, DateTime.now().toUtc());
      throw OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        'Remote daily home payload is stale: ${data.editionDate}',
      );
    }
    final enriched = await _withMainlandCoverFallbacks(data);
    final cachedAt = DateTime.now().toUtc();
    await _writeCache(enriched, cachedAt);
    return OnlineHomeBundle(
      data: enriched,
      usedCache: false,
      cachedAt: cachedAt,
    );
  }

  Future<OnlineHomeData> _fetchFresh() async {
    final errors = <OnlineHomeException>[];
    final results = await Future.wait<Object?>([
      _optionalFetch(_loadRemoteDailyHome(), errors),
      _optionalListFetch(_loadNewAlbums(), errors),
    ]);
    final remoteHome = results[0] as OnlineHomeData?;
    final albums =
        results[1] as List<OnlineAlbumCard>? ?? const <OnlineAlbumCard>[];

    if (remoteHome != null && _isUsableDailyHome(remoteHome)) {
      final data = _mergeDailyHome(remoteHome, albums);
      await _writeCache(data, DateTime.now().toUtc());
      if (isFreshData(data)) return data;
      errors.add(
        OnlineHomeException(
          OnlineHomeErrorKind.unavailable,
          'Remote daily home payload is stale: ${data.editionDate}',
        ),
      );
    }

    final firstNoNetwork = errors.where(
      (error) => error.kind == OnlineHomeErrorKind.noNetwork,
    );
    if (firstNoNetwork.isNotEmpty) throw firstNoNetwork.first;
    throw const OnlineHomeException(
      OnlineHomeErrorKind.unavailable,
      'All online home requests failed',
    );
  }

  Future<OnlineHomeData> _withMainlandCoverFallbacks(
    OnlineHomeData data,
  ) async {
    final topPlaylist = data.topPlaylist == null
        ? null
        : await _withSectionCoverFallbacks(data.topPlaylist!);
    final sections = <OnlineSection>[];
    for (final section in data.sections) {
      sections.add(await _withSectionCoverFallbacks(section));
    }

    return OnlineHomeData(
      schemaVersion: data.schemaVersion,
      generatedAt: data.generatedAt,
      editionDate: data.editionDate,
      tags: data.tags,
      sections: List.unmodifiable(sections),
      topPlaylist: topPlaylist,
      albumRecommendations: data.albumRecommendations,
    );
  }

  Future<OnlineSection> _withSectionCoverFallbacks(
    OnlineSection section,
  ) async {
    final tracks = section.tracks;
    if (tracks.isEmpty) return section;

    final indexesToLookup = <int>[];
    for (var i = 0; i < tracks.length; i++) {
      if (_needsMainlandCoverFallback(tracks[i].coverUrl)) {
        indexesToLookup.add(i);
      }
    }
    if (indexesToLookup.isEmpty) return section;

    final fallbackByIndex = <int, String>{};
    var cursor = 0;
    final workers = List.generate(_coverSearchConcurrency, (_) async {
      while (cursor < indexesToLookup.length) {
        final index = indexesToLookup[cursor++];
        final track = tracks[index];
        final coverUrl = await _searchNeteaseCoverForTrack(track);
        if (coverUrl != null && coverUrl.isNotEmpty) {
          fallbackByIndex[index] = coverUrl;
        }
      }
    });
    await Future.wait(workers);
    if (fallbackByIndex.isEmpty) return section;

    final patched = <OnlineTrackCandidate>[];
    for (var i = 0; i < tracks.length; i++) {
      patched.add(_copyTrackWithCover(tracks[i], fallbackByIndex[i]));
    }
    return OnlineSection(
      id: section.id,
      titleByLang: section.titleByLang,
      subtitle: section.subtitle,
      tracks: List.unmodifiable(patched),
    );
  }

  bool _needsMainlandCoverFallback(String? coverUrl) {
    final trimmed = coverUrl?.trim() ?? '';
    if (trimmed.isEmpty) return true;
    return false;
  }

  OnlineTrackCandidate _copyTrackWithCover(
    OnlineTrackCandidate track,
    String? coverUrl,
  ) {
    if (coverUrl == null || coverUrl.isEmpty) return track;
    return OnlineTrackCandidate(
      title: track.title,
      artist: track.artist,
      album: track.album,
      durationMs: track.durationMs,
      coverUrl: coverUrl,
      audioUrl: track.audioUrl,
      audioProvider: track.audioProvider,
      providerTrackId: track.providerTrackId,
      sourceTags: track.sourceTags,
      canonicalKey: track.canonicalKey,
    );
  }

  Future<String?> _searchNeteaseCoverForTrack(
    OnlineTrackCandidate track,
  ) async {
    for (final query in _coverSearchQueries(track)) {
      final Map<String, dynamic>? json;
      try {
        json = await _safeGetJson(neteaseSongSearchUri(query: query));
      } on OnlineHomeException {
        return null;
      }
      if (json == null) continue;
      final result = json['result'];
      final songs = result is Map ? result['songs'] : null;
      if (songs is! List) continue;

      String? fallbackCover;
      var bestScore = -1;
      for (final raw in songs.whereType<Map>()) {
        final score = _scoreCoverCandidate(track, raw);
        final coverUrl = _coverUrlFromNeteaseSong(raw);
        if (coverUrl == null || coverUrl.isEmpty) continue;
        if (score > bestScore) {
          bestScore = score;
          fallbackCover = coverUrl;
        }
      }
      if (fallbackCover != null && bestScore >= 32) return fallbackCover;
    }
    return null;
  }

  List<String> _coverSearchQueries(OnlineTrackCandidate track) {
    final title = _simplifySearchText(track.title);
    final artist = _simplifySearchText(track.artist);
    final album = _simplifySearchText(track.album);
    final queries = <String>[];
    if (title.isNotEmpty && artist.isNotEmpty) queries.add('$artist $title');
    if (title.isNotEmpty && album.isNotEmpty) queries.add('$title $album');
    if (title.isNotEmpty) queries.add(title);
    return queries.toSet().take(3).toList(growable: false);
  }

  int _scoreCoverCandidate(OnlineTrackCandidate requested, Map raw) {
    final title = _normalizeForMatch(requested.title);
    final artist = _normalizeForMatch(requested.artist);
    final matchedTitle = _normalizeForMatch(raw['name']?.toString() ?? '');
    final artists = raw['artists'];
    final matchedArtists = artists is List
        ? artists
              .whereType<Map>()
              .map((item) => item['name']?.toString() ?? '')
              .join(' ')
        : '';
    final matchedArtist = _normalizeForMatch(matchedArtists);

    var score = 0;
    if (title.isNotEmpty && matchedTitle == title) {
      score += 52;
    } else if (title.isNotEmpty &&
        (matchedTitle.contains(title) || title.contains(matchedTitle))) {
      score += 28;
    }
    if (artist.isNotEmpty && matchedArtist == artist) {
      score += 34;
    } else if (artist.isNotEmpty &&
        (matchedArtist.contains(artist) || artist.contains(matchedArtist))) {
      score += 16;
    }
    final duration = (raw['duration'] as num?)?.toInt() ?? 0;
    if (duration > 0 && requested.durationMs > 0) {
      final diff = (duration - requested.durationMs).abs();
      if (diff <= 2500) {
        score += 12;
      } else if (diff <= 10000) {
        score += 6;
      }
    }
    return score;
  }

  String? _coverUrlFromNeteaseSong(Map raw) {
    final album = raw['album'];
    if (album is! Map) return null;
    return upgradeCoverUrl(album['picUrl'] as String?) ??
        upgradeCoverUrl(album['blurPicUrl'] as String?) ??
        neteaseCoverUrlFromPicId(album['picId']);
  }

  String _simplifySearchText(String value) {
    return value
        .replaceAll(RegExp(r'\s*\([^)]*\)\s*'), ' ')
        .replaceAll(RegExp(r'\s*\[[^\]]*\]\s*'), ' ')
        .replaceAll(RegExp(r'\s+'), ' ')
        .trim();
  }

  String _normalizeForMatch(String value) {
    return _simplifySearchText(
      value,
    ).toLowerCase().replaceAll(RegExp(r'[^a-z0-9\u4e00-\u9fff]+'), '');
  }

  Future<OnlineHomeData?> _loadRemoteDailyHome() async {
    final json = await _safeGetJson(
      _remoteHomeUri,
      headers: _remoteHomeHeaders,
    );
    if (json == null) return null;
    final data = OnlineHomeData.fromJson(json);
    if (!_isUsableDailyHome(data)) return null;
    return data;
  }

  OnlineHomeData _mergeDailyHome(
    OnlineHomeData dailyHome,
    List<OnlineAlbumCard> albums,
  ) {
    return OnlineHomeData(
      schemaVersion: _kSchemaVersion,
      generatedAt: dailyHome.generatedAt,
      editionDate: dailyHome.editionDate.isEmpty
          ? _todayIsoDate()
          : dailyHome.editionDate,
      tags: dailyHome.tags,
      sections: dailyHome.sections,
      topPlaylist: dailyHome.topPlaylist,
      albumRecommendations: albums.isEmpty
          ? dailyHome.albumRecommendations
          : List.unmodifiable(albums),
    );
  }

  Future<List<OnlineAlbumCard>> _loadNewAlbums({
    String area = 'ALL',
    int limit = 12,
    int offset = 0,
  }) async {
    final json = await _safeGetJson(
      neteaseNewAlbumsUri(area: area, limit: limit, offset: offset),
    );
    if (json == null) return const <OnlineAlbumCard>[];

    final albums = json['albums'];
    if (albums is! List) return const <OnlineAlbumCard>[];

    final cards = <OnlineAlbumCard>[];
    for (final raw in albums) {
      if (raw is! Map) continue;
      final map = raw.cast<String, dynamic>();
      final id = (map['id'] as num?)?.toInt();
      if (id == null || id <= 0) continue;
      final name = (map['name'] as String?)?.trim() ?? '';
      if (name.isEmpty) continue;

      final artistField = map['artist'];
      var artistName = '';
      if (artistField is Map) {
        artistName = (artistField['name'] as String?)?.trim() ?? '';
      }
      if (artistName.isEmpty) {
        final artists = map['artists'];
        if (artists is List && artists.isNotEmpty) {
          final first = artists.first;
          if (first is Map) {
            artistName = (first['name'] as String?)?.trim() ?? '';
          }
        }
      }

      final coverUrl =
          upgradeCoverUrl(map['picUrl'] as String?) ??
          upgradeCoverUrl(map['blurPicUrl'] as String?) ??
          await _loadAlbumCoverFallback(id);

      cards.add(
        OnlineAlbumCard(
          albumId: id,
          name: name,
          artist: artistName,
          coverUrl: coverUrl,
        ),
      );
    }
    return cards;
  }

  Future<String?> _loadAlbumCoverFallback(int albumId) async {
    final json = await _safeGetJson(neteaseAlbumDetailUri(albumId: albumId));
    if (json == null) return null;
    final album = json['album'];
    if (album is! Map) return null;
    return upgradeCoverUrl(album['picUrl'] as String?) ??
        upgradeCoverUrl(album['blurPicUrl'] as String?);
  }

  /// Pulls the fields we need from a NetEase playlist track. The shape varies
  /// slightly between `/api/v6/playlist/detail` (uses `al` / `ar` / `dt`) and
  /// `/api/personalized/newsong`'s embedded `song` (same compact form). The
  /// function accepts both.
  Map<String, dynamic>? _candidateFromPlaylistTrack(
    Map<String, dynamic> json, {
    String? fallbackPicUrl,
  }) {
    final id = (json['id'] as num?)?.toInt();
    final title = (json['name'] as String?)?.trim() ?? '';
    if (id == null || id <= 0 || title.isEmpty) return null;

    final ar = json['ar'] ?? json['artists'];
    var artistName = '';
    if (ar is List && ar.isNotEmpty) {
      final first = ar.first;
      if (first is Map) {
        artistName = (first['name'] as String?)?.trim() ?? '';
      }
    }
    if (artistName.isEmpty) return null;

    final al = json['al'] ?? json['album'];
    var albumName = '';
    String? coverUrl;
    if (al is Map) {
      albumName = (al['name'] as String?)?.trim() ?? '';
      coverUrl = al['picUrl'] as String?;
    }
    coverUrl ??= fallbackPicUrl;

    final duration =
        (json['dt'] as num?)?.toInt() ??
        (json['duration'] as num?)?.toInt() ??
        0;

    return <String, dynamic>{
      'title': title,
      'artist': artistName,
      'album': albumName,
      'durationMs': duration,
      'coverUrl': upgradeCoverUrl(coverUrl),
      'audioUrl': null,
      'audioProvider': 'netease',
      'providerTrackId': '$id',
      'sourceTags': const <String>['netease'],
    };
  }

  Future<T?> _optionalFetch<T>(
    Future<T?> future,
    List<OnlineHomeException> errors,
  ) async {
    try {
      return await future;
    } on OnlineHomeException catch (error) {
      errors.add(error);
      return null;
    }
  }

  Future<List<T>> _optionalListFetch<T>(
    Future<List<T>> future,
    List<OnlineHomeException> errors,
  ) async {
    try {
      return await future;
    } on OnlineHomeException catch (error) {
      errors.add(error);
      return <T>[];
    }
  }

  Future<Map<String, dynamic>?> _safeGetJson(
    Uri uri, {
    Map<String, String>? headers,
  }) async {
    try {
      final request = await _httpClient
          .getUrl(uri)
          .timeout(_kPerRequestTimeout);
      (headers ?? kNeteaseHeaders).forEach(request.headers.set);
      final response = await request.close().timeout(_kPerRequestTimeout);
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return null;
      }
      final body = await utf8.decoder.bind(response).join();
      final decoded = jsonDecode(body);
      if (decoded is! Map<String, dynamic>) return null;
      final code = decoded['code'];
      if (code is num && code.toInt() != 200) return null;
      return decoded;
    } on TimeoutException {
      return null;
    } on SocketException {
      throw const OnlineHomeException(OnlineHomeErrorKind.noNetwork);
    } on HttpException {
      return null;
    } on FormatException {
      return null;
    }
  }

  bool _isUsableDailyHome(OnlineHomeData data) {
    if (data.schemaVersion < _kSchemaVersion) return false;
    final topPlaylist = data.topPlaylist;
    if (topPlaylist == null || topPlaylist.tracks.length < 100) return false;
    final tracksWithCover = topPlaylist.tracks.where(
      (track) => (track.coverUrl ?? '').trim().isNotEmpty,
    );
    return tracksWithCover.length >= 80;
  }

  bool _isFresh(OnlineHomeBundle cached) {
    return cached.data.editionDate.trim() == _todayIsoDate();
  }

  Future<OnlineHomeBundle?> _loadCachedForDate(String editionDate) async {
    final file = await _resolveCacheFile('home-$editionDate.json');
    if (!file.existsSync()) return null;

    try {
      final data = await _readCachedDataFile(file, expectedDate: editionDate);
      if (data == null) return null;

      final stamp = await _resolveCacheFile('home-$editionDate.stamp');
      DateTime cachedAt = file.statSync().modified.toUtc();
      if (stamp.existsSync()) {
        final stampText = await stamp.readAsString();
        final parsed = DateTime.tryParse(stampText.trim());
        if (parsed != null) cachedAt = parsed.toUtc();
      }
      return OnlineHomeBundle(data: data, usedCache: true, cachedAt: cachedAt);
    } catch (_) {
      return null;
    }
  }

  Future<OnlineHomeData?> _readCachedDataFile(
    File file, {
    required String expectedDate,
  }) async {
    final body = await file.readAsString();
    final decoded = jsonDecode(body);
    if (decoded is! Map<String, dynamic>) return null;
    final cachedSchema = (decoded['schemaVersion'] as num?)?.toInt() ?? 0;
    if (cachedSchema != _kSchemaVersion) return null;
    final data = OnlineHomeData.fromJson(decoded);
    if (data.editionDate.trim() != expectedDate) return null;
    if (!_isUsableDailyHome(data)) return null;
    return data;
  }

  Future<void> _writeCache(OnlineHomeData data, DateTime cachedAt) async {
    final dateKey = data.editionDate.trim().isEmpty
        ? _todayIsoDate()
        : data.editionDate.trim();
    final archive = await _resolveCacheFile('home-$dateKey.json');
    await archive.parent.create(recursive: true);
    final body = jsonEncode(data.toJson());
    await archive.writeAsString(body, flush: true);
    final archiveStamp = await _resolveCacheFile('home-$dateKey.stamp');
    await archiveStamp.writeAsString(cachedAt.toIso8601String(), flush: true);

    final latest = await _resolveCacheFile('home.json');
    await latest.writeAsString(body, flush: true);
    final latestStamp = await _resolveCacheFile('home.stamp');
    await latestStamp.writeAsString(cachedAt.toIso8601String(), flush: true);
  }

  Future<File> _resolveCacheFile(String fileName) async {
    final directory = await _resolveCacheDirectory();
    return File(p.join(directory.path, fileName));
  }

  Future<Directory> _resolveCacheDirectory() async {
    final localAppData = Platform.environment['LOCALAPPDATA'];
    if (localAppData != null && localAppData.isNotEmpty) {
      return Directory(p.join(localAppData, 'PrismWave', 'online_home_cache'));
    }
    final userProfile = Platform.environment['USERPROFILE'];
    if (userProfile != null && userProfile.isNotEmpty) {
      return Directory(
        p.join(userProfile, 'Documents', 'PrismWave', 'online_home_cache'),
      );
    }
    return Directory(p.join(Directory.current.path, 'online_home_cache'));
  }

  String _todayIsoDate() {
    final now = _beijingNow();
    final y = now.year.toString().padLeft(4, '0');
    final m = now.month.toString().padLeft(2, '0');
    final d = now.day.toString().padLeft(2, '0');
    return '$y-$m-$d';
  }

  String _yesterdayIsoDate() {
    final yesterday = _beijingNow().subtract(const Duration(days: 1));
    final y = yesterday.year.toString().padLeft(4, '0');
    final m = yesterday.month.toString().padLeft(2, '0');
    final d = yesterday.day.toString().padLeft(2, '0');
    return '$y-$m-$d';
  }

  DateTime _beijingNow() => DateTime.now().toUtc().add(
    const Duration(hours: 8),
  );

  /// Fetches album detail and returns its full track list as candidates ready
  /// to be passed into `OnlineController.playOnlineTrack`.
  Future<List<Map<String, dynamic>>> loadAlbumTracks(int albumId) async {
    final json = await _safeGetJson(neteaseAlbumDetailUri(albumId: albumId));
    if (json == null) return const <Map<String, dynamic>>[];
    final songs = json['songs'];
    if (songs is! List) return const <Map<String, dynamic>>[];
    final result = <Map<String, dynamic>>[];
    for (final raw in songs) {
      if (raw is! Map) continue;
      final c = _candidateFromPlaylistTrack(raw.cast<String, dynamic>());
      if (c != null) result.add(c);
    }
    return result;
  }

  /// Warms up the HTTP connection pool to reduce first-request latency.
  /// Call this early (e.g., during controller initialization) to avoid
  /// cold-start delays when the user first plays a track.
  Future<void> warmUp() async {
    try {
      // Make a lightweight request to establish the connection
      await _safeGetJson(neteaseToplistUri());
    } catch (_) {
      // Silently ignore errors - this is best-effort optimization
    }
  }

  void dispose() {
    _httpClient.close(force: true);
  }
}
