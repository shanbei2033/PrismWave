import 'dart:async';
import 'dart:convert';
import 'dart:io';

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
  });

  final OnlineHomeData data;
  final bool usedCache;
  final DateTime cachedAt;
  final bool needsBackgroundRefresh;
}

/// Pulls home recommendations from the generated prismwave-hits daily payload,
/// then supplements it with NetEase album cards. Song sections come from:
///
/// - https://raw.githubusercontent.com/shanbei2033/prismwave-hits/main/home/latest_home.json
///
/// If the generated payload is unavailable, the service falls back to the
/// original direct music.163.com endpoints:
///
/// - top-100 banner    → /api/v6/playlist/detail (热歌榜 id=3778678, n=100)
/// - new albums        → /api/album/new (12 cards, no embedded tracks)
/// - hot songs         → /api/personalized/newsong (24 inlined songs)
/// - 7 style segments  → /api/playlist/list (find hot playlist per cat) →
///                       /api/v6/playlist/detail (n=20)
///
/// Network results are cached on disk by `editionDate`; a stale cache is served
/// on any failure.
class NeteaseHomeService {
  NeteaseHomeService({HttpClient? httpClient})
    : _httpClient =
          httpClient ??
          (HttpClient()..connectionTimeout = const Duration(seconds: 6));

  final HttpClient _httpClient;

  static const int _topChartPlaylistId = 3778678; // 云音乐热歌榜
  static const int _topChartTrackCount = 100;
  static const int _kSchemaVersion = 6;
  static const int _coverSearchConcurrency = 4;
  static const int _manualRefreshAlbumPageSize = 12;
  static const int _manualRefreshAlbumPageCount = 4;
  static const int _manualRefreshNewSongOffsetLimit = 36;
  static const int _manualRefreshPlaylistOffsetLimit = 16;
  static const List<String> _manualRefreshAlbumAreas = <String>[
    'ALL',
    'ZH',
    'EA',
    'KR',
    'JP',
  ];
  static final Uri _remoteHomeUri = Uri.https(
    'raw.githubusercontent.com',
    '/shanbei2033/prismwave-hits/main/home/latest_home.json',
  );
  static const Map<String, String> _remoteHomeHeaders = <String, String>{
    HttpHeaders.userAgentHeader: 'Mozilla/5.0 PrismWave/1.0.0',
    HttpHeaders.acceptHeader: 'application/json',
  };

  /// Pinned style buckets. The order here is the order they render on home.
  /// Categories use NetEase Cloud Music's Chinese category names; the API
  /// understands them as URL-encoded UTF-8.
  static const List<_StyleBucket> _styleBuckets = <_StyleBucket>[
    _StyleBucket(
      id: 'style-pop',
      category: '流行',
      titleZh: '流行',
      titleZhTw: '流行',
      titleEn: 'Pop',
    ),
    _StyleBucket(
      id: 'style-rock',
      category: '摇滚',
      titleZh: '摇滚',
      titleZhTw: '搖滾',
      titleEn: 'Rock',
    ),
    _StyleBucket(
      id: 'style-electronic',
      category: '电子',
      titleZh: '电子',
      titleZhTw: '電子',
      titleEn: 'Electronic',
    ),
    _StyleBucket(
      id: 'style-indie',
      category: '独立',
      titleZh: '独立',
      titleZhTw: '獨立',
      titleEn: 'Indie',
    ),
    _StyleBucket(
      id: 'style-hiphop',
      category: '说唱',
      titleZh: '嘻哈',
      titleZhTw: '嘻哈',
      titleEn: 'Hip-Hop',
    ),
    _StyleBucket(
      id: 'style-rnb',
      category: 'R&B/Soul',
      titleZh: 'R&B / 灵魂乐',
      titleZhTw: 'R&B / 靈魂樂',
      titleEn: 'R&B / Soul',
    ),
    _StyleBucket(
      id: 'style-folk',
      category: '民谣',
      titleZh: '民谣',
      titleZhTw: '民謠',
      titleEn: 'Folk',
    ),
  ];

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
      final cached = await _loadCached();
      if (cached != null) return cached;
      if (error is OnlineHomeException) rethrow;
      throw OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        error.toString(),
      );
    }
  }

  Future<OnlineHomeBundle?> loadCachedBundle({bool allowStale = false}) async {
    final cached = await _loadCached();
    if (cached == null) return null;
    if (allowStale || _isFresh(cached)) return cached;
    return null;
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
    if (remoteHome == null || !_hasSongPayload(remoteHome)) {
      throw const OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        'Remote daily home payload is unavailable',
      );
    }
    final data = _mergeDailyHome(remoteHome, const <OnlineAlbumCard>[]);
    final cachedAt = DateTime.now().toUtc();
    await _writeCache(data, cachedAt);
    return OnlineHomeBundle(
      data: data,
      usedCache: false,
      cachedAt: cachedAt,
      needsBackgroundRefresh: true,
    );
  }

  Future<OnlineHomeBundle> refreshRecommendations({
    OnlineSection? preserveTopPlaylist,
  }) async {
    final data = await _fetchManualRefreshHome(
      preserveTopPlaylist: preserveTopPlaylist,
    );
    final cachedAt = DateTime.now().toUtc();
    await _writeCache(data, cachedAt);
    return OnlineHomeBundle(data: data, usedCache: false, cachedAt: cachedAt);
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

    if (remoteHome != null && _hasSongPayload(remoteHome)) {
      return _mergeDailyHome(remoteHome, albums);
    }

    try {
      return await _fetchNeteaseFallbackHome(preloadedAlbums: albums);
    } on OnlineHomeException catch (error) {
      errors.add(error);
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
    final host = Uri.tryParse(trimmed)?.host.toLowerCase() ?? '';
    if (host.endsWith('music.126.net') || host.endsWith('music.163.com')) {
      return false;
    }
    if (host.endsWith('dmhmusic.com') || host.endsWith('taihe.com')) {
      return false;
    }
    return true;
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
    if (!_hasSongPayload(data)) return null;
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
      albumRecommendations: List.unmodifiable(albums),
    );
  }

  Future<OnlineHomeData> _fetchNeteaseFallbackHome({
    required List<OnlineAlbumCard> preloadedAlbums,
  }) async {
    final topChartFuture = _loadTopChart();
    final newSongsFuture = _loadNewSongsSection();
    final styleFutures = _styleBuckets.map(_loadStyleSection).toList();

    final results = await Future.wait<Object?>([
      topChartFuture,
      newSongsFuture,
      ...styleFutures,
    ]);

    final topChart = results[0] as OnlineSection?;
    final newSongs = results[1] as OnlineSection?;
    final styleSections = results
        .sublist(2)
        .whereType<OnlineSection>()
        .toList(growable: false);

    final sections = <OnlineSection>[?newSongs, ...styleSections];

    if (topChart == null && preloadedAlbums.isEmpty && sections.isEmpty) {
      throw const OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        'All NetEase home requests failed',
      );
    }

    return OnlineHomeData(
      schemaVersion: _kSchemaVersion,
      generatedAt: DateTime.now().toUtc(),
      editionDate: _todayIsoDate(),
      tags: const <OnlineTag>[],
      sections: List.unmodifiable(sections),
      topPlaylist: topChart,
      albumRecommendations: List.unmodifiable(preloadedAlbums),
    );
  }

  Future<OnlineHomeData> _fetchManualRefreshHome({
    required OnlineSection? preserveTopPlaylist,
  }) async {
    final errors = <OnlineHomeException>[];
    final seed = DateTime.now().millisecondsSinceEpoch;
    final albumArea =
        _manualRefreshAlbumAreas[(seed ~/ 997) %
            _manualRefreshAlbumAreas.length];
    final albumOffset =
        ((seed ~/ 1543) % _manualRefreshAlbumPageCount) *
        _manualRefreshAlbumPageSize;
    final newSongOffset = (seed ~/ 811) % _manualRefreshNewSongOffsetLimit;

    final results = await Future.wait<Object?>([
      _optionalListFetch(
        _loadNewAlbums(
          area: albumArea,
          limit: _manualRefreshAlbumPageSize,
          offset: albumOffset,
        ),
        errors,
      ),
      _optionalFetch(_loadNewSongsSection(offset: newSongOffset), errors),
      ..._styleBuckets.asMap().entries.map((entry) {
        final playlistOffset =
            ((seed ~/ (1237 + entry.key * 97)) %
            _manualRefreshPlaylistOffsetLimit);
        return _optionalFetch(
          _loadStyleSection(entry.value, playlistOffset: playlistOffset),
          errors,
        );
      }),
    ]);

    final albums =
        results[0] as List<OnlineAlbumCard>? ?? const <OnlineAlbumCard>[];
    final newSongs = results[1] as OnlineSection?;
    final styleSections = results
        .sublist(2)
        .whereType<OnlineSection>()
        .toList(growable: false);
    final sections = <OnlineSection>[?newSongs, ...styleSections];

    if (sections.isEmpty && albums.isEmpty) {
      final firstNoNetwork = errors.where(
        (error) => error.kind == OnlineHomeErrorKind.noNetwork,
      );
      if (firstNoNetwork.isNotEmpty) throw firstNoNetwork.first;
      throw const OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        'All manual home refresh requests failed',
      );
    }

    return OnlineHomeData(
      schemaVersion: _kSchemaVersion,
      generatedAt: DateTime.now().toUtc(),
      editionDate: _todayIsoDate(),
      tags: const <OnlineTag>[],
      sections: List.unmodifiable(sections),
      topPlaylist: preserveTopPlaylist,
      albumRecommendations: List.unmodifiable(albums),
    );
  }

  Future<OnlineSection?> _loadTopChart() async {
    final json = await _safeGetJson(
      neteasePlaylistDetailUri(
        playlistId: _topChartPlaylistId,
        n: _topChartTrackCount,
      ),
    );
    if (json == null) return null;

    final playlist = json['playlist'];
    if (playlist is! Map) return null;
    final tracks = playlist['tracks'];
    if (tracks is! List) return null;

    final candidates = <Map<String, dynamic>>[];
    for (var i = 0; i < tracks.length; i++) {
      final raw = tracks[i];
      if (raw is! Map) continue;
      final track = _candidateFromPlaylistTrack(raw.cast<String, dynamic>());
      if (track != null) candidates.add(track);
    }
    if (candidates.isEmpty) return null;

    return OnlineSection.fromJson(
      _sectionJson(
        id: 'netease-top-chart',
        titleZh: '今日趋势',
        titleZhTw: '今日趨勢',
        titleEn: "Today's Trending",
        subtitle: (playlist['updateFrequency'] as String?) ?? '',
        tracks: candidates,
      ),
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

  Future<OnlineSection?> _loadNewSongsSection({
    int limit = 24,
    int offset = 0,
  }) async {
    final requestLimit = limit + offset;
    final json = await _safeGetJson(
      neteasePersonalizedNewSongUri(limit: requestLimit),
    );
    if (json == null) return null;

    final result = json['result'];
    if (result is! List) return null;

    final candidates = <Map<String, dynamic>>[];
    final windowed = result.skip(offset).take(limit).toList();
    final selected = windowed.isEmpty && offset > 0
        ? result.take(limit)
        : windowed;

    for (final raw in selected) {
      if (raw is! Map) continue;
      final entry = raw.cast<String, dynamic>();
      final song = entry['song'];
      if (song is! Map) continue;

      final track = _candidateFromPlaylistTrack(
        song.cast<String, dynamic>(),
        fallbackPicUrl: entry['picUrl'] as String?,
      );
      if (track != null) candidates.add(track);
    }
    if (candidates.isEmpty) return null;

    return OnlineSection.fromJson(
      _sectionJson(
        id: 'netease-new-songs',
        titleZh: '歌曲推荐',
        titleZhTw: '歌曲推薦',
        titleEn: 'New Songs For You',
        subtitle: '',
        tracks: candidates,
      ),
    );
  }

  Future<OnlineSection?> _loadStyleSection(
    _StyleBucket bucket, {
    int playlistOffset = 0,
  }) async {
    final listJson = await _safeGetJson(
      neteasePlaylistByCategoryUri(
        category: bucket.category,
        limit: 1,
        offset: playlistOffset,
      ),
    );
    if (listJson == null) return null;
    final playlists = listJson['playlists'];
    if (playlists is! List || playlists.isEmpty) return null;
    final first = playlists.first;
    if (first is! Map) return null;
    final playlistId = (first['id'] as num?)?.toInt();
    if (playlistId == null || playlistId <= 0) return null;

    final detail = await _safeGetJson(
      neteasePlaylistDetailUri(playlistId: playlistId, n: 20),
    );
    if (detail == null) return null;
    final playlist = detail['playlist'];
    if (playlist is! Map) return null;
    final tracks = playlist['tracks'];
    if (tracks is! List) return null;

    final candidates = <Map<String, dynamic>>[];
    for (final raw in tracks) {
      if (raw is! Map) continue;
      final c = _candidateFromPlaylistTrack(raw.cast<String, dynamic>());
      if (c != null) candidates.add(c);
    }
    if (candidates.isEmpty) return null;

    return OnlineSection.fromJson(
      _sectionJson(
        id: bucket.id,
        titleZh: bucket.titleZh,
        titleZhTw: bucket.titleZhTw,
        titleEn: bucket.titleEn,
        subtitle: (first['name'] as String?) ?? '',
        tracks: candidates,
      ),
    );
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

  Map<String, dynamic> _sectionJson({
    required String id,
    required String titleZh,
    required String titleZhTw,
    required String titleEn,
    required String subtitle,
    required List<Map<String, dynamic>> tracks,
  }) {
    return <String, dynamic>{
      'id': id,
      'title': <String, String>{
        'zh-Hans': titleZh,
        'zh-Hant': titleZhTw,
        'en-US': titleEn,
      },
      if (subtitle.isNotEmpty) 'subtitle': subtitle,
      'tracks': tracks,
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

  bool _hasSongPayload(OnlineHomeData data) {
    return data.sections.isNotEmpty || data.topPlaylist != null;
  }

  bool _isFresh(OnlineHomeBundle cached) {
    return cached.data.editionDate.trim() == _todayIsoDate();
  }

  Future<OnlineHomeBundle?> _loadCached() async {
    final file = await _resolveCacheFile('home.json');
    if (!file.existsSync()) return null;

    try {
      final body = await file.readAsString();
      final decoded = jsonDecode(body);
      if (decoded is! Map<String, dynamic>) return null;
      final cachedSchema = (decoded['schemaVersion'] as num?)?.toInt() ?? 0;
      if (cachedSchema != _kSchemaVersion) return null;
      final data = OnlineHomeData.fromJson(decoded);

      final stamp = await _resolveCacheFile('home.stamp');
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

  Future<void> _writeCache(OnlineHomeData data, DateTime cachedAt) async {
    final file = await _resolveCacheFile('home.json');
    await file.parent.create(recursive: true);
    await file.writeAsString(jsonEncode(data.toJson()), flush: true);
    final stamp = await _resolveCacheFile('home.stamp');
    await stamp.writeAsString(cachedAt.toIso8601String(), flush: true);
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
    final now = DateTime.now().toUtc();
    final y = now.year.toString().padLeft(4, '0');
    final m = now.month.toString().padLeft(2, '0');
    final d = now.day.toString().padLeft(2, '0');
    return '$y-$m-$d';
  }

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

class _StyleBucket {
  const _StyleBucket({
    required this.id,
    required this.category,
    required this.titleZh,
    required this.titleZhTw,
    required this.titleEn,
  });

  final String id;
  final String category;
  final String titleZh;
  final String titleZhTw;
  final String titleEn;
}
