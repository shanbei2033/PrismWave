import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart' show rootBundle;
import 'package:path/path.dart' as p;

import '../models/online_recommendation.dart';
import 'netease_endpoints.dart';

const Duration _kPerRequestTimeout = Duration(seconds: 7);
const Duration _kCoverSearchTimeout = Duration(seconds: 4);

typedef OnlineHomeDebugLogger = void Function(String message, {bool force});

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
    this.recommendationsPendingGeneration = false,
  });

  final OnlineHomeData data;
  final bool usedCache;
  final DateTime cachedAt;
  final bool needsBackgroundRefresh;
  final bool recommendationsUnavailable;
  final bool recommendationsPendingGeneration;
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
  NeteaseHomeService({
    HttpClient? httpClient,
    OnlineHomeDebugLogger? debugLog,
    List<Uri>? remoteHomeUris,
    Directory? cacheDirectory,
    OnlineHomeData? bundledHomeOverride,
  }) : _debugLog = debugLog,
       _remoteHomeSources = remoteHomeUris == null
           ? _defaultRemoteHomeSources()
           : _remoteSourcesFromUris(remoteHomeUris),
       _cacheDirectoryOverride = cacheDirectory,
       _bundledHomeOverride = bundledHomeOverride,
       _httpClient =
           httpClient ??
           (HttpClient()..connectionTimeout = const Duration(seconds: 6));

  final HttpClient _httpClient;
  final OnlineHomeDebugLogger? _debugLog;
  final List<_RemoteHomeSource> _remoteHomeSources;
  final Directory? _cacheDirectoryOverride;
  final OnlineHomeData? _bundledHomeOverride;

  static const int _kSchemaVersion = 8;
  static const int _coverSearchConcurrency = 4;
  static const int _coverFallbackTrackLimit = 12;
  static const int _topPlaylistCoverFallbackTrackLimit = 40;
  static const String _bundledHomeAsset = 'assets/home/latest_home.json';
  static const Set<String> _requiredStyleSectionIds = <String>{
    'style-pop',
    'style-rock',
    'style-electronic',
    'style-hiphop',
    'style-rnb',
  };
  static final Uri _remoteHomeUri = Uri.https(
    'raw.githubusercontent.com',
    '/shanbei2033/prismwave-hits/main/home/latest_home.json',
  );
  static final Uri _remoteHomeCdnUri = Uri.https(
    'cdn.jsdelivr.net',
    '/gh/shanbei2033/prismwave-hits@main/home/latest_home.json',
  );
  static final Uri _remoteHomeGithubApiUri = Uri.https(
    'api.github.com',
    '/repos/shanbei2033/prismwave-hits/contents/home/latest_home.json',
    <String, String>{'ref': 'main'},
  );
  static const Map<String, String> _remoteHomeHeaders = <String, String>{
    HttpHeaders.userAgentHeader: 'Mozilla/5.0 PrismWave/1.0.0',
    HttpHeaders.acceptHeader: 'application/json',
  };
  static const Map<String, String> _githubApiHeaders = <String, String>{
    HttpHeaders.userAgentHeader: 'Mozilla/5.0 PrismWave/1.0.0',
    HttpHeaders.acceptHeader: 'application/vnd.github+json',
  };
  static const String _customHomeUrlsEnv = 'PRISMWAVE_HOME_URLS';
  static const String _customHomeMirrorsEnv = 'PRISMWAVE_HOME_MIRRORS';

  Future<OnlineHomeBundle> loadBundle({
    bool forceRefresh = false,
    bool allowStaleCache = false,
    bool allowLatestAvailable = false,
  }) async {
    if (!forceRefresh) {
      final cached = await loadCachedBundle(allowStale: allowStaleCache);
      if (cached != null) {
        return cached;
      }
    }

    try {
      final data = await _fetchFresh(
        allowLatestAvailable: allowLatestAvailable,
      );
      final cachedAt = DateTime.now().toUtc();
      await _writeCache(data, cachedAt);
      return OnlineHomeBundle(
        data: data,
        usedCache: false,
        cachedAt: cachedAt,
        recommendationsPendingGeneration: !isFreshData(data),
      );
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

  Future<OnlineHomeBundle?> loadYesterdayCachedBundle({
    bool pendingGeneration = false,
  }) async {
    final cached = await _loadCachedForDate(_yesterdayIsoDate());
    if (cached == null) return null;
    return OnlineHomeBundle(
      data: cached.data,
      usedCache: true,
      cachedAt: cached.cachedAt,
      recommendationsUnavailable: !pendingGeneration,
      recommendationsPendingGeneration: pendingGeneration,
    );
  }

  Future<OnlineHomeBundle?> loadBundledFallbackBundle() async {
    try {
      final data = await _loadBundledHomeData();
      if (data == null) return null;
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

  Future<OnlineHomeData?> _loadBundledHomeData() async {
    final override = _bundledHomeOverride;
    if (override != null) return override;
    final body = await rootBundle.loadString(_bundledHomeAsset);
    final decoded = jsonDecode(body);
    if (decoded is! Map<String, dynamic>) return null;
    return OnlineHomeData.fromJson(decoded);
  }

  bool isFreshBundle(OnlineHomeBundle bundle) => _isFresh(bundle);

  bool isFreshData(OnlineHomeData data) =>
      data.editionDate.trim() == _todayIsoDate();

  bool isPendingDailyGenerationData(OnlineHomeData data) {
    return data.editionDate.trim() == _yesterdayIsoDate() &&
        isDailyGenerationPendingWindow();
  }

  bool isDailyGenerationPendingWindow() => _beijingNow().hour < 10;

  bool needsMainlandCoverFallbacks(OnlineHomeData data) {
    bool sectionNeedsFallback(OnlineSection? section, {required int limit}) {
      if (section == null) return false;
      final scanCount = limit < section.tracks.length
          ? limit
          : section.tracks.length;
      for (var i = 0; i < scanCount; i++) {
        if (_needsMainlandCoverFallback(section.tracks[i].coverUrl)) {
          return true;
        }
      }
      return false;
    }

    if (sectionNeedsFallback(
      data.topPlaylist,
      limit: _topPlaylistCoverFallbackTrackLimit,
    )) {
      return true;
    }
    return data.sections.any(
      (section) =>
          sectionNeedsFallback(section, limit: _coverFallbackTrackLimit),
    );
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

  Future<OnlineHomeBundle> loadRemoteDailyBundle({
    bool allowLatestAvailable = false,
  }) async {
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
    final isFresh = isFreshData(data);
    if (!isFresh && !allowLatestAvailable) {
      throw OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        'Remote daily home payload is stale: ${data.editionDate}',
      );
    }
    return OnlineHomeBundle(
      data: data,
      usedCache: false,
      cachedAt: cachedAt,
      recommendationsPendingGeneration: !isFresh,
    );
  }

  Future<OnlineHomeBundle> refreshLiveHome({
    bool allowLatestAvailable = false,
  }) async {
    final remoteHome = await _loadRemoteDailyHome();
    if (remoteHome == null || !_isUsableDailyHome(remoteHome)) {
      throw const OnlineHomeException(
        OnlineHomeErrorKind.unavailable,
        'Remote daily home payload is unavailable',
      );
    }
    final data = _mergeDailyHome(remoteHome, const <OnlineAlbumCard>[]);
    final isFresh = isFreshData(data);
    if (!isFresh && !allowLatestAvailable) {
      final staleCachedAt = DateTime.now().toUtc();
      await _writeCache(data, staleCachedAt);
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
      recommendationsPendingGeneration: !isFresh,
    );
  }

  Future<OnlineHomeData> _fetchFresh({
    bool allowLatestAvailable = false,
  }) async {
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
      if (isFreshData(data) || allowLatestAvailable) return data;
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
    final stopwatch = Stopwatch()..start();
    _debug(
      'home.cover-fallback.start -> edition=${data.editionDate} '
      'top=${data.topPlaylist?.tracks.length ?? 0} '
      'sections=${data.sections.length}',
      force: true,
    );
    final topPlaylist = data.topPlaylist == null
        ? null
        : await _withSectionCoverFallbacks(
            data.topPlaylist!,
            lookupLimit: _topPlaylistCoverFallbackTrackLimit,
          );
    final sections = <OnlineSection>[];
    for (final section in data.sections) {
      sections.add(
        await _withSectionCoverFallbacks(
          section,
          lookupLimit: _coverFallbackTrackLimit,
        ),
      );
    }
    _debug(
      'home.cover-fallback.ready -> edition=${data.editionDate} '
      'elapsedMs=${stopwatch.elapsedMilliseconds}',
      force: true,
    );

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
    OnlineSection section, {
    required int lookupLimit,
  }) async {
    final tracks = section.tracks;
    if (tracks.isEmpty) return section;

    final indexesToLookup = <int>[];
    final scanCount = lookupLimit < tracks.length ? lookupLimit : tracks.length;
    for (var i = 0; i < scanCount; i++) {
      if (_needsMainlandCoverFallback(tracks[i].coverUrl)) {
        indexesToLookup.add(i);
      }
    }
    if (indexesToLookup.isEmpty) return section;

    final stopwatch = Stopwatch()..start();
    _debug(
      'home.cover-fallback.section.start -> id=${section.id} '
      'lookup=${indexesToLookup.length}/$scanCount',
    );
    final fallbackByIndex = <int, String>{};
    var cursor = 0;
    final workerCount = indexesToLookup.length < _coverSearchConcurrency
        ? indexesToLookup.length
        : _coverSearchConcurrency;
    final workers = List.generate(workerCount, (_) async {
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
    if (fallbackByIndex.isEmpty) {
      _debug(
        'home.cover-fallback.section.none -> id=${section.id} '
        'lookup=${indexesToLookup.length} '
        'elapsedMs=${stopwatch.elapsedMilliseconds}',
      );
      return section;
    }

    final patched = <OnlineTrackCandidate>[];
    for (var i = 0; i < tracks.length; i++) {
      patched.add(_copyTrackWithCover(tracks[i], fallbackByIndex[i]));
    }
    _debug(
      'home.cover-fallback.section.ready -> id=${section.id} '
      'patched=${fallbackByIndex.length}/${indexesToLookup.length} '
      'elapsedMs=${stopwatch.elapsedMilliseconds}',
      force: true,
    );
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
    return !_isMainlandFriendlyCoverHost(host);
  }

  bool _isMainlandFriendlyCoverHost(String host) {
    if (host.isEmpty) return false;
    return host.endsWith('music.126.net') ||
        host.endsWith('music.163.com') ||
        host.endsWith('y.qq.com') ||
        host.endsWith('qpic.cn') ||
        host.endsWith('gtimg.cn') ||
        host.endsWith('kuwo.cn') ||
        host.endsWith('migu.cn') ||
        host.endsWith('dmhmusic.com') ||
        host.endsWith('taihe.com');
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
        json = await _safeGetJson(
          neteaseSongSearchUri(query: query),
          timeout: _kCoverSearchTimeout,
        );
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
    if (_remoteHomeSources.isEmpty) return null;

    _debug(
      'home.remote.start -> sources=${_remoteHomeSources.map((source) => source.label).join(',')}',
      force: true,
    );
    final completer = Completer<_RemoteHomeAttempt?>();
    _RemoteHomeAttempt? latestAvailable;
    var remaining = _remoteHomeSources.length;

    for (final source in _remoteHomeSources) {
      unawaited(
        _fetchRemoteHomeSource(source).then((attempt) {
          final data = attempt.data;
          if (data != null) {
            if (isFreshData(data)) {
              if (!completer.isCompleted) completer.complete(attempt);
            } else {
              final newer = _newerRemoteHome(latestAvailable?.data, data);
              if (identical(newer, data)) {
                latestAvailable = attempt;
              }
            }
          }

          remaining -= 1;
          if (remaining == 0 && !completer.isCompleted) {
            completer.complete(latestAvailable);
          }
        }),
      );
    }

    final selected = await completer.future;
    final data = selected?.data;
    if (data == null) {
      _debug(
        'home.remote.all-failed -> sources=${_remoteHomeSources.length}',
        force: true,
      );
      return null;
    }

    _debug(
      'home.remote.selected -> source=${selected!.source.label} '
      'edition=${data.editionDate} fresh=${isFreshData(data)}',
      force: true,
    );
    return data;
  }

  Future<_RemoteHomeAttempt> _fetchRemoteHomeSource(
    _RemoteHomeSource source,
  ) async {
    final stopwatch = Stopwatch()..start();
    try {
      final json = await _safeGetJson(
        source.uri,
        headers: source.headers,
        timeout: source.timeout,
      );
      if (json == null) {
        _debug(
          'home.remote.source.failed -> source=${source.label} '
          'elapsedMs=${stopwatch.elapsedMilliseconds}',
          force: true,
        );
        return _RemoteHomeAttempt.failure(
          source: source,
          elapsedMs: stopwatch.elapsedMilliseconds,
        );
      }

      final payload = source.kind == _RemoteHomePayloadKind.githubContentsApi
          ? _decodeGithubContentsPayload(json)
          : json;
      if (payload == null) {
        _debug(
          'home.remote.source.invalid-payload -> source=${source.label} '
          'elapsedMs=${stopwatch.elapsedMilliseconds}',
          force: true,
        );
        return _RemoteHomeAttempt.failure(
          source: source,
          elapsedMs: stopwatch.elapsedMilliseconds,
        );
      }

      final data = OnlineHomeData.fromJson(payload);
      final normalized = await _normalizeRemoteDailyHome(data);
      if (normalized == null) {
        _debug(
          'home.remote.source.unusable -> source=${source.label} '
          'schema=${data.schemaVersion} edition=${data.editionDate} '
          'top=${data.topPlaylist?.tracks.length ?? 0} '
          'sections=${data.sections.length} '
          'elapsedMs=${stopwatch.elapsedMilliseconds}',
          force: true,
        );
        return _RemoteHomeAttempt.failure(
          source: source,
          elapsedMs: stopwatch.elapsedMilliseconds,
        );
      }
      if (!identical(normalized, data)) {
        _debug(
          'home.remote.source.compatible -> source=${source.label} '
          'remoteSchema=${data.schemaVersion} edition=${data.editionDate} '
          'styleSections=bundled',
          force: true,
        );
      }

      _debug(
        'home.remote.source.ready -> source=${source.label} '
        'edition=${normalized.editionDate} fresh=${isFreshData(normalized)} '
        'elapsedMs=${stopwatch.elapsedMilliseconds}',
        force: true,
      );
      return _RemoteHomeAttempt.success(
        source: source,
        data: normalized,
        elapsedMs: stopwatch.elapsedMilliseconds,
      );
    } on OnlineHomeException catch (error) {
      _debug(
        'home.remote.source.error -> source=${source.label} '
        'kind=${error.kind} elapsedMs=${stopwatch.elapsedMilliseconds}',
        force: error.kind == OnlineHomeErrorKind.noNetwork,
      );
      return _RemoteHomeAttempt.failure(
        source: source,
        elapsedMs: stopwatch.elapsedMilliseconds,
      );
    } catch (error) {
      _debug(
        'home.remote.source.error -> source=${source.label} '
        'elapsedMs=${stopwatch.elapsedMilliseconds} error=$error',
        force: true,
      );
      return _RemoteHomeAttempt.failure(
        source: source,
        elapsedMs: stopwatch.elapsedMilliseconds,
      );
    }
  }

  Map<String, dynamic>? _decodeGithubContentsPayload(
    Map<String, dynamic> json,
  ) {
    final encoding = json['encoding']?.toString().toLowerCase();
    final content = json['content'];
    if (encoding != 'base64' || content is! String || content.isEmpty) {
      return null;
    }
    try {
      final normalized = content.replaceAll(RegExp(r'\s+'), '');
      final body = utf8.decode(base64.decode(normalized));
      final decoded = jsonDecode(body);
      return decoded is Map<String, dynamic> ? decoded : null;
    } on FormatException {
      return null;
    }
  }

  OnlineHomeData _newerRemoteHome(
    OnlineHomeData? current,
    OnlineHomeData candidate,
  ) {
    if (current == null) return candidate;
    final dateCompare = candidate.editionDate.compareTo(current.editionDate);
    if (dateCompare > 0) return candidate;
    if (dateCompare < 0) return current;
    return candidate.generatedAt.isAfter(current.generatedAt)
        ? candidate
        : current;
  }

  Future<OnlineHomeData?> _normalizeRemoteDailyHome(OnlineHomeData data) async {
    if (_isUsableDailyHome(data)) return data;
    if (!_hasUsableTopPlaylist(data)) return null;

    OnlineHomeData? bundled;
    try {
      bundled = await _loadBundledHomeData();
    } catch (error) {
      _debug('home.remote.compat-bundled.failed -> error=$error', force: true);
    }
    final sections = bundled != null && _hasRequiredStyleSections(bundled)
        ? bundled.sections
        : _styleSectionsFromLegacyData(data);
    if (!_hasRequiredStyleSections(
      OnlineHomeData(
        schemaVersion: _kSchemaVersion,
        generatedAt: data.generatedAt,
        editionDate: data.editionDate,
        tags: data.tags,
        sections: sections,
        topPlaylist: data.topPlaylist,
        albumRecommendations: const <OnlineAlbumCard>[],
      ),
    )) {
      return null;
    }

    return OnlineHomeData(
      schemaVersion: _kSchemaVersion,
      generatedAt: data.generatedAt,
      editionDate: data.editionDate,
      tags: data.tags.isEmpty && bundled != null ? bundled.tags : data.tags,
      sections: sections,
      topPlaylist: data.topPlaylist,
      albumRecommendations: data.albumRecommendations.isEmpty && bundled != null
          ? bundled.albumRecommendations
          : data.albumRecommendations,
    );
  }

  List<OnlineSection> _styleSectionsFromLegacyData(OnlineHomeData data) {
    final pool = <OnlineTrackCandidate>[
      for (final section in data.sections) ...section.tracks,
      if (data.topPlaylist != null) ...data.topPlaylist!.tracks,
    ];
    if (pool.length < _requiredStyleSectionIds.length * 4) {
      return const <OnlineSection>[];
    }

    const styleTitles = <String, String>{
      'style-pop': 'Pop',
      'style-rock': 'Rock',
      'style-electronic': 'Electronic',
      'style-hiphop': 'Hip-Hop',
      'style-rnb': 'R&B',
    };
    final sections = <OnlineSection>[];
    var cursor = 0;
    for (final id in _requiredStyleSectionIds) {
      final title = styleTitles[id] ?? id;
      final tracks = <OnlineTrackCandidate>[];
      for (var i = 0; i < 12 && cursor < pool.length; i += 1) {
        tracks.add(pool[cursor]);
        cursor += 1;
      }
      if (tracks.length < 4) break;
      sections.add(
        OnlineSection(
          id: id,
          titleByLang: <String, String>{'en-US': title},
          subtitle: null,
          tracks: List.unmodifiable(tracks),
        ),
      );
    }
    return List.unmodifiable(sections);
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
    Duration timeout = _kPerRequestTimeout,
  }) async {
    try {
      final request = await _httpClient.getUrl(uri).timeout(timeout);
      (headers ?? kNeteaseHeaders).forEach(request.headers.set);
      final response = await request.close().timeout(timeout);
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
    if (!_hasUsableTopPlaylist(data)) return false;
    if (!_hasRequiredStyleSections(data)) return false;
    return true;
  }

  bool _hasUsableTopPlaylist(OnlineHomeData data) {
    final topPlaylist = data.topPlaylist;
    if (topPlaylist == null || topPlaylist.tracks.length < 100) return false;
    final tracksWithCover = topPlaylist.tracks.where(
      (track) => (track.coverUrl ?? '').trim().isNotEmpty,
    );
    return tracksWithCover.length >= 80;
  }

  bool _hasRequiredStyleSections(OnlineHomeData data) {
    if (data.sections.length < _requiredStyleSectionIds.length) return false;
    final ids = data.sections
        .where((section) => section.tracks.length >= 4)
        .map((section) => section.id)
        .toSet();
    return ids.containsAll(_requiredStyleSectionIds);
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
    final override = _cacheDirectoryOverride;
    if (override != null) return override;
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

  DateTime _beijingNow() =>
      DateTime.now().toUtc().add(const Duration(hours: 8));

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

  void _debug(String message, {bool force = false}) {
    _debugLog?.call('online.$message', force: force);
  }

  static List<_RemoteHomeSource> _defaultRemoteHomeSources() {
    return _dedupeRemoteSources(<_RemoteHomeSource>[
      ..._customRemoteHomeSources(),
      _RemoteHomeSource.direct(label: 'github-raw', uri: _remoteHomeUri),
      _RemoteHomeSource.direct(label: 'jsdelivr', uri: _remoteHomeCdnUri),
      _RemoteHomeSource.githubContentsApi(
        label: 'github-api',
        uri: _remoteHomeGithubApiUri,
      ),
    ]);
  }

  static List<_RemoteHomeSource> _customRemoteHomeSources() {
    final raw =
        Platform.environment[_customHomeUrlsEnv] ??
        Platform.environment[_customHomeMirrorsEnv];
    if (raw == null || raw.trim().isEmpty) return const <_RemoteHomeSource>[];

    final sources = <_RemoteHomeSource>[];
    final parts = raw
        .split(RegExp(r'[\s,;]+'))
        .map((part) => part.trim())
        .where((part) => part.isNotEmpty);
    for (final part in parts) {
      final uri = Uri.tryParse(part);
      if (uri == null || !uri.hasScheme || uri.host.isEmpty) continue;
      sources.add(
        _RemoteHomeSource.direct(
          label: 'custom-${sources.length + 1}',
          uri: uri,
        ),
      );
    }
    return sources;
  }

  static List<_RemoteHomeSource> _remoteSourcesFromUris(List<Uri> uris) {
    final sources = <_RemoteHomeSource>[];
    for (final uri in uris) {
      if (!uri.hasScheme || uri.host.isEmpty) continue;
      sources.add(
        _RemoteHomeSource.direct(
          label: 'custom-${sources.length + 1}',
          uri: uri,
        ),
      );
    }
    return _dedupeRemoteSources(sources);
  }

  static List<_RemoteHomeSource> _dedupeRemoteSources(
    Iterable<_RemoteHomeSource> sources,
  ) {
    final seen = <String>{};
    final result = <_RemoteHomeSource>[];
    for (final source in sources) {
      final key = source.uri.toString();
      if (seen.add(key)) result.add(source);
    }
    return List.unmodifiable(result);
  }
}

enum _RemoteHomePayloadKind { directJson, githubContentsApi }

class _RemoteHomeSource {
  const _RemoteHomeSource._({
    required this.label,
    required this.uri,
    required this.kind,
    required this.headers,
    required this.timeout,
  });

  factory _RemoteHomeSource.direct({required String label, required Uri uri}) {
    return _RemoteHomeSource._(
      label: label,
      uri: uri,
      kind: _RemoteHomePayloadKind.directJson,
      headers: NeteaseHomeService._remoteHomeHeaders,
      timeout: _kPerRequestTimeout,
    );
  }

  factory _RemoteHomeSource.githubContentsApi({
    required String label,
    required Uri uri,
  }) {
    return _RemoteHomeSource._(
      label: label,
      uri: uri,
      kind: _RemoteHomePayloadKind.githubContentsApi,
      headers: NeteaseHomeService._githubApiHeaders,
      timeout: _kPerRequestTimeout,
    );
  }

  final String label;
  final Uri uri;
  final _RemoteHomePayloadKind kind;
  final Map<String, String> headers;
  final Duration timeout;
}

class _RemoteHomeAttempt {
  const _RemoteHomeAttempt._({
    required this.source,
    required this.data,
    required this.elapsedMs,
  });

  factory _RemoteHomeAttempt.success({
    required _RemoteHomeSource source,
    required OnlineHomeData data,
    required int elapsedMs,
  }) {
    return _RemoteHomeAttempt._(
      source: source,
      data: data,
      elapsedMs: elapsedMs,
    );
  }

  factory _RemoteHomeAttempt.failure({
    required _RemoteHomeSource source,
    required int elapsedMs,
  }) {
    return _RemoteHomeAttempt._(
      source: source,
      data: null,
      elapsedMs: elapsedMs,
    );
  }

  final _RemoteHomeSource source;
  final OnlineHomeData? data;
  final int elapsedMs;
}
