import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:shared_preferences/shared_preferences.dart';
import 'package:youtube_explode_dart/youtube_explode_dart.dart';

import '../models/hits_manifest.dart';

class HitsResolvedAudioSource {
  const HitsResolvedAudioSource({
    required this.playbackUrl,
    this.provider,
    this.providerTrackId,
    this.suggestedFileExtension,
    this.coverUrl,
    this.playbackHeaders,
  });

  final String playbackUrl;
  final String? provider;
  final String? providerTrackId;
  final String? suggestedFileExtension;
  final String? coverUrl;
  final Map<String, String>? playbackHeaders;
}

class _ResolvedTrackCacheEntry {
  const _ResolvedTrackCacheEntry._({this.source, this.failedAt});

  const _ResolvedTrackCacheEntry.success(HitsResolvedAudioSource source)
    : this._(source: source);

  const _ResolvedTrackCacheEntry.failure(DateTime failedAt)
    : this._(failedAt: failedAt);

  final HitsResolvedAudioSource? source;
  final DateTime? failedAt;
}

enum _HitsSourceRegion {
  mainlandChina,
  greaterChina,
  global,
}

class _HitsRoutingProfile {
  const _HitsRoutingProfile({
    required this.region,
    required this.countryCode,
    required this.detectedAt,
    required this.providerStats,
  });

  final _HitsSourceRegion region;
  final String countryCode;
  final DateTime detectedAt;
  final Map<String, Map<String, dynamic>> providerStats;

  List<String> orderProviders(Iterable<String> providerIds) {
    final providers = providerIds
        .map((item) => item.trim().toLowerCase())
        .where((item) => item.isNotEmpty)
        .toSet()
        .toList(growable: false);
    final baseOrder = switch (region) {
      _HitsSourceRegion.mainlandChina => <String>[
        'bilibili',
        'bilivideo',
        'migu',
        'kuwo',
        'kugou',
        'qq',
        'netease',
        'youtube',
        'audius',
      ],
      _HitsSourceRegion.greaterChina => <String>[
        'bilibili',
        'bilivideo',
        'qq',
        'netease',
        'kuwo',
        'kugou',
        'migu',
        'youtube',
        'audius',
      ],
      _HitsSourceRegion.global => <String>[
        'youtube',
        'bilibili',
        'bilivideo',
        'netease',
        'qq',
        'kuwo',
        'kugou',
        'migu',
        'audius',
      ],
    };
    final baseIndex = <String, int>{
      for (var i = 0; i < baseOrder.length; i += 1) baseOrder[i]: i,
    };

    providers.sort((left, right) {
      final leftScore = _providerScore(left, baseIndex[left] ?? 999);
      final rightScore = _providerScore(right, baseIndex[right] ?? 999);
      return rightScore.compareTo(leftScore);
    });
    return providers;
  }

  int get concurrentWaveSize {
    switch (region) {
      case _HitsSourceRegion.mainlandChina:
      case _HitsSourceRegion.greaterChina:
        return 2;
      case _HitsSourceRegion.global:
        return 2;
    }
  }

  double _providerScore(String provider, int baseIndex) {
    final stats = providerStats[provider] ?? const <String, dynamic>{};
    final successCount = (stats['successCount'] as num?)?.toInt() ?? 0;
    final failureCount = (stats['failureCount'] as num?)?.toInt() ?? 0;
    final consecutiveFailures =
        (stats['consecutiveFailures'] as num?)?.toInt() ?? 0;
    final averageLatencyMs =
        (stats['averageLatencyMs'] as num?)?.toDouble() ?? 0;
    final attempts = successCount + failureCount;
    final successRate = attempts == 0 ? 0 : successCount / attempts;
    return (220 - (baseIndex * 14)).toDouble() +
        (successRate * 48) -
        (consecutiveFailures * 18) -
        averageLatencyMs.clamp(0, 8000) / 240;
  }
}

class HitsAudioResolverService {
  HitsAudioResolverService({HttpClient? httpClient, YoutubeExplode? youtube})
    : _httpClient =
          httpClient ??
          (HttpClient()..connectionTimeout = const Duration(seconds: 6)),
      _youtube = youtube ?? YoutubeExplode();

  static const String _audiusProvider = 'audius';
  static const String _youtubeProvider = 'youtube';
  static const String _bilibiliProvider = 'bilibili';
  static const String _biliVideoProvider = 'bilivideo';
  static const String _neteaseProvider = 'netease';
  static const String _kuwoProvider = 'kuwo';
  static const String _miguProvider = 'migu';
  static const String _qqProvider = 'qq';
  static const String _kugouProvider = 'kugou';
  static const String _audiusHost = 'api.audius.co';
  static const String _audiusBasePath = '/v1';
  static const String _prefGeoPayload = 'hits.sourceRegionPayload';
  static const String _prefProviderStats = 'hits.sourceProviderStats';
  static const Duration _routingProfileTtl = Duration(hours: 24);
  static const Duration _geoProbeTimeout = Duration(milliseconds: 1200);
  static const int _searchLimit = 8;
  static const int _minMatchScore = 82;
  static const int _youtubeSearchQueryLimit = 3;
  static const int _youtubeCandidateLimit = 8;
  static const int _youtubeResolveLimit = 3;
  static const int _youtubeMinMatchScore = 54;
  static const int _bilibiliSearchQueryLimit = 3;
  static const int _bilibiliCandidateLimit = 8;
  static const int _bilibiliResolveLimit = 3;
  static const Duration _failedResolveCacheTtl = Duration(seconds: 45);
  static const int _bilibiliMinMatchScore = 42;
  static const Set<String> _preferredYoutubeContainers = {'m4a', 'mp4'};
  static const Map<String, String> _bilibiliHeaders = <String, String>{
    'User-Agent': 'Mozilla/5.0 PrismWave/1.0.0',
    'Referer': 'https://www.bilibili.com/',
    'Accept': 'application/json',
  };
  static const Map<String, String> _neteaseHeaders = <String, String>{
    'User-Agent': 'Mozilla/5.0 PrismWave/1.0.0',
    'Referer': 'https://music.163.com/',
    'Accept': 'application/json',
  };
  static const int _neteaseSearchQueryLimit = 3;
  static const int _neteaseCandidateLimit = 8;
  static const int _neteaseResolveLimit = 3;
  static const int _neteaseMinMatchScore = 44;
  static const Map<String, String> _kuwoHeaders = <String, String>{
    'User-Agent': 'Mozilla/5.0 PrismWave/1.0.0',
    'Referer': 'https://www.kuwo.cn/',
    'Accept': 'application/json',
  };
  static const int _kuwoSearchQueryLimit = 3;
  static const int _kuwoCandidateLimit = 8;
  static const int _kuwoResolveLimit = 3;
  static const int _kuwoMinMatchScore = 44;
  static const Map<String, String> _miguHeaders = <String, String>{
    'User-Agent': 'Mozilla/5.0 PrismWave/1.0.0',
    'Referer': 'https://m.music.migu.cn/',
    'Accept': 'application/json',
  };
  static const int _miguSearchQueryLimit = 3;
  static const int _miguCandidateLimit = 8;
  static const int _miguResolveLimit = 3;
  static const int _miguMinMatchScore = 44;
  static const Map<String, String> _qqHeaders = <String, String>{
    'User-Agent': 'Mozilla/5.0 PrismWave/1.0.0',
    'Referer': 'https://y.qq.com/',
    'Accept': 'application/json',
  };
  static const int _qqSearchQueryLimit = 3;
  static const int _qqCandidateLimit = 8;
  static const int _qqResolveLimit = 3;
  static const int _qqMinMatchScore = 44;
  static const Map<String, String> _kugouHeaders = <String, String>{
    'User-Agent': 'Mozilla/5.0 PrismWave/1.0.0',
    'Referer': 'https://m.kugou.com/',
    'Accept': 'application/json',
  };
  static const int _kugouSearchQueryLimit = 3;
  static const int _kugouCandidateLimit = 8;
  static const int _kugouResolveLimit = 3;
  static const int _kugouMinMatchScore = 44;
  static const Set<String> _variantKeywords = {
    'remix',
    'cover',
    'edit',
    'live',
    'mashup',
    'bootleg',
    'version',
    'vip',
    'flip',
    'instrumental',
    'karaoke',
    'nightcore',
    'rework',
  };
  static final List<YoutubeApiClient> _youtubeClients = <YoutubeApiClient>[
    YoutubeApiClient.androidMusic,
    YoutubeApiClient.ios,
    YoutubeApiClient.androidVr,
  ];

  final HttpClient _httpClient;
  final YoutubeExplode _youtube;
  SharedPreferences? _preferences;
  _HitsRoutingProfile? _routingProfile;
  Future<_HitsRoutingProfile>? _pendingRoutingProfile;
  Future<void>? _pendingRoutingRefresh;
  final Map<String, _ResolvedTrackCacheEntry> _cache =
      <String, _ResolvedTrackCacheEntry>{};
  final Map<String, Future<HitsResolvedAudioSource?>> _pending =
      <String, Future<HitsResolvedAudioSource?>>{};
  String? _kuwoCsrfToken;
  DateTime? _kuwoCsrfTokenFetchedAt;
  static const Duration _kuwoCsrfTokenTtl = Duration(minutes: 12);

  Future<void> warmUp() async {
    await _loadRoutingProfile();
  }

  Future<HitsResolvedAudioSource?> resolveTrack(HitsScheduleTrack track) {
    final cacheKey = track.stationTrackId;
    final cachedEntry = _cache[cacheKey];
    if (cachedEntry != null) {
      final cachedSource = cachedEntry.source;
      if (cachedSource != null) {
        return Future<HitsResolvedAudioSource?>.value(cachedSource);
      }
      final failedAt = cachedEntry.failedAt;
      if (failedAt != null &&
          DateTime.now().toUtc().difference(failedAt) <
              _failedResolveCacheTtl) {
        return Future<HitsResolvedAudioSource?>.value(null);
      }
      _cache.remove(cacheKey);
    }

    final pending = _pending[cacheKey];
    if (pending != null) {
      return pending;
    }

    final future =
        Future<HitsResolvedAudioSource?>(() async {
          try {
            return await _resolveTrackInternal(track);
          } catch (_) {
            return null;
          }
        }).then((resolved) {
          _cache[cacheKey] = resolved == null
              ? _ResolvedTrackCacheEntry.failure(DateTime.now().toUtc())
              : _ResolvedTrackCacheEntry.success(resolved);
          _pending.remove(cacheKey);
          return resolved;
        });

    _pending[cacheKey] = future;
    return future;
  }

  Future<HitsResolvedAudioSource?> _resolveTrackInternal(
    HitsScheduleTrack track,
  ) async {
    final direct = _resolveDirectSource(track);
    if (direct != null) {
      return direct;
    }

    final pinned = await _resolvePinnedProviderSource(track);
    if (pinned != null) {
      return pinned;
    }

    final routingProfile = await _loadRoutingProfile();
    final orderedProviders = routingProfile.orderProviders(const <String>[
      _bilibiliProvider,
      _biliVideoProvider,
      _youtubeProvider,
      _audiusProvider,
      _neteaseProvider,
      _kuwoProvider,
      _miguProvider,
      _qqProvider,
      _kugouProvider,
    ]);

    var start = 0;
    while (start < orderedProviders.length) {
      final currentWave = orderedProviders
          .skip(start)
          .take(routingProfile.concurrentWaveSize)
          .toList(growable: false);
      final resolved = await _firstSuccessful(
        currentWave.map((provider) => _resolveMeasured(track, provider)),
      );
      if (resolved != null) {
        return resolved;
      }
      start += routingProfile.concurrentWaveSize;
    }

    return null;
  }

  HitsResolvedAudioSource? _resolveDirectSource(HitsScheduleTrack track) {
    final playbackUrl = track.audioUrl?.toString().trim() ?? '';
    if (playbackUrl.isEmpty) {
      return null;
    }

    return HitsResolvedAudioSource(
      playbackUrl: playbackUrl,
      provider: track.audioProvider.trim().isEmpty ? null : track.audioProvider,
      providerTrackId: track.providerTrackId.trim().isEmpty
          ? null
          : track.providerTrackId,
      suggestedFileExtension: _extensionFromUrl(playbackUrl),
      coverUrl: track.coverUrl?.toString(),
    );
  }

  Future<HitsResolvedAudioSource?> _resolvePinnedProviderSource(
    HitsScheduleTrack track,
  ) async {
    final provider = track.audioProvider.trim().toLowerCase();
    final providerTrackId = track.providerTrackId.trim();
    if (providerTrackId.isEmpty) {
      return null;
    }

    if (provider == _audiusProvider) {
      return HitsResolvedAudioSource(
        playbackUrl: _audiusStreamEndpoint(providerTrackId),
        provider: _audiusProvider,
        providerTrackId: providerTrackId,
        suggestedFileExtension: '.mp3',
        coverUrl: track.coverUrl?.toString(),
      );
    }

    if (provider == _youtubeProvider) {
      return _resolveYouTubeById(providerTrackId, track);
    }

    if (provider == _bilibiliProvider || provider == _biliVideoProvider) {
      return _resolveBilibiliStream(
        bvid: providerTrackId,
        coverUrl: track.coverUrl?.toString(),
        provider: provider,
      );
    }

    if (provider == _neteaseProvider) {
      return _resolveNeteaseStream(
        songId: int.tryParse(providerTrackId) ?? 0,
        coverUrl: track.coverUrl?.toString(),
      );
    }

    if (provider == _kuwoProvider) {
      return _resolveKuwoStream(mid: providerTrackId);
    }

    if (provider == _miguProvider) {
      return _resolveMiguStream(
        copyrightId: providerTrackId,
        coverUrl: track.coverUrl?.toString(),
      );
    }

    if (provider == _qqProvider) {
      return _resolveQQStream(
        songMid: providerTrackId,
        coverUrl: track.coverUrl?.toString(),
      );
    }

    if (provider == _kugouProvider) {
      return _resolveKugouStream(
        fileHash: providerTrackId,
        coverUrl: track.coverUrl?.toString(),
      );
    }

    return null;
  }

  Future<HitsResolvedAudioSource?> _resolveMeasured(
    HitsScheduleTrack track,
    String provider,
  ) async {
    final stopwatch = Stopwatch()..start();
    final resolved = await _resolveProvider(track, provider);
    stopwatch.stop();
    unawaited(
      _recordProviderResult(
        provider: provider,
        success: resolved != null,
        latency: stopwatch.elapsed,
      ),
    );
    return resolved;
  }

  Future<HitsResolvedAudioSource?> _resolveProvider(
    HitsScheduleTrack track,
    String provider,
  ) async {
    switch (provider) {
      case _bilibiliProvider:
      case _biliVideoProvider:
        return _searchBilibili(track, provider: provider);
      case _youtubeProvider:
        return _searchYouTube(track);
      case _audiusProvider:
        return _searchAudius(track);
      case _neteaseProvider:
        return _searchNetease(track);
      case _kuwoProvider:
        return _searchKuwo(track);
      case _miguProvider:
        return _searchMigu(track);
      case _qqProvider:
        return _searchQQ(track);
      case _kugouProvider:
        return _searchKugou(track);
      default:
        return null;
    }
  }

  Future<HitsResolvedAudioSource?> _searchBilibili(
    HitsScheduleTrack track, {
    required String provider,
  }) async {
    final candidateByBvid = <String, _BilibiliCandidate>{};

    for (final query in _buildBilibiliSearchQueries(
      track,
      provider: provider,
    ).take(_bilibiliSearchQueryLimit)) {
      final results = await _searchBilibiliByQuery(query);
      for (final candidate in results.take(_bilibiliCandidateLimit)) {
        final scored = candidate.copyWith(
          score: _scoreBilibiliCandidateMatch(
            requestedTrack: track,
            matched: candidate,
            provider: provider,
          ),
        );
        final existing = candidateByBvid[scored.bvid];
        if (existing == null || scored.score > existing.score) {
          candidateByBvid[scored.bvid] = scored;
        }
      }

      if (candidateByBvid.values.any(
        (candidate) => candidate.score >= _bilibiliMinMatchScore + 18,
      )) {
        break;
      }
    }

    final rankedCandidates = candidateByBvid.values.toList(growable: false)
      ..sort((left, right) => right.score.compareTo(left.score));

    for (final candidate in rankedCandidates.take(_bilibiliResolveLimit)) {
      if (candidate.score < _bilibiliMinMatchScore) {
        break;
      }
      final resolved = await _resolveBilibiliStream(
        bvid: candidate.bvid,
        coverUrl: candidate.coverUrl,
        provider: provider,
      );
      if (resolved != null) {
        return resolved;
      }
    }

    return null;
  }

  Future<List<_BilibiliCandidate>> _searchBilibiliByQuery(String query) async {
    final trimmedQuery = query.trim();
    if (trimmedQuery.isEmpty) {
      return const <_BilibiliCandidate>[];
    }

    final uri = Uri.https('api.bilibili.com', '/x/web-interface/search/type', {
      'search_type': 'video',
      'keyword': trimmedQuery,
      'page': '1',
    });

    try {
      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 3));
      _bilibiliHeaders.forEach(request.headers.set);
      final response = await request.close().timeout(const Duration(seconds: 4));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return const <_BilibiliCandidate>[];
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final data = decoded is Map ? decoded['data'] : null;
      final results = data is Map ? data['result'] : null;
      if (results is! List) {
        return const <_BilibiliCandidate>[];
      }

      return results
          .whereType<Map>()
          .map(
            (item) =>
                _BilibiliCandidate.fromJson(Map<String, dynamic>.from(item)),
          )
          .whereType<_BilibiliCandidate>()
          .toList(growable: false);
    } catch (_) {
      return const <_BilibiliCandidate>[];
    }
  }

  Future<HitsResolvedAudioSource?> _resolveBilibiliStream({
    required String bvid,
    required String provider,
    String? coverUrl,
  }) async {
    try {
      final cidRequest = await _httpClient
          .getUrl(
            Uri.https('api.bilibili.com', '/x/player/pagelist', {
              'bvid': bvid,
              'jsonp': 'jsonp',
            }),
          )
          .timeout(const Duration(seconds: 3));
      _bilibiliHeaders.forEach(cidRequest.headers.set);
      final cidResponse = await cidRequest.close().timeout(
        const Duration(seconds: 4),
      );
      if (cidResponse.statusCode < 200 || cidResponse.statusCode >= 300) {
        await cidResponse.drain<void>();
        return null;
      }
      final cidDecoded = jsonDecode(await utf8.decoder.bind(cidResponse).join());
      final cidItems = cidDecoded is Map ? cidDecoded['data'] : null;
      if (cidItems is! List || cidItems.isEmpty) {
        return null;
      }
      final cid = ((cidItems.first as Map)['cid'] as num?)?.toInt();
      if (cid == null) {
        return null;
      }

      final playRequest = await _httpClient
          .getUrl(
            Uri.https('api.bilibili.com', '/x/player/playurl', {
              'bvid': bvid,
              'cid': '$cid',
              'qn': '64',
              'fnval': '16',
              'fourk': '0',
            }),
          )
          .timeout(const Duration(seconds: 4));
      playRequest.headers.set(
        'User-Agent',
        _bilibiliHeaders['User-Agent']!,
      );
      playRequest.headers.set('Referer', 'https://www.bilibili.com/video/$bvid');
      playRequest.headers.set('Accept', 'application/json');
      final playResponse = await playRequest.close().timeout(
        const Duration(seconds: 5),
      );
      if (playResponse.statusCode < 200 || playResponse.statusCode >= 300) {
        await playResponse.drain<void>();
        return null;
      }

      final playDecoded = jsonDecode(
        await utf8.decoder.bind(playResponse).join(),
      );
      final data = playDecoded is Map ? playDecoded['data'] : null;
      final dash = data is Map ? data['dash'] : null;
      final audioItems = dash is Map ? dash['audio'] : null;
      String playbackUrl = '';
      String? extension;
      if (audioItems is List && audioItems.isNotEmpty) {
        final audioList = audioItems
            .whereType<Map>()
            .map((item) => Map<String, dynamic>.from(item))
            .toList(growable: false)
          ..sort((left, right) {
            final leftBandwidth = (left['bandwidth'] as num?)?.toInt() ?? 0;
            final rightBandwidth = (right['bandwidth'] as num?)?.toInt() ?? 0;
            return rightBandwidth.compareTo(leftBandwidth);
          });
        for (final item in audioList) {
          final primary = (item['baseUrl'] ?? item['base_url'] ?? '')
              .toString()
              .trim();
          if (primary.isNotEmpty) {
            playbackUrl = primary;
            extension = _extensionFromUrl(primary) ?? '.m4a';
            break;
          }
          final backup = item['backupUrl'] ?? item['backup_url'];
          if (backup is List) {
            for (final candidate in backup) {
              final url = candidate.toString().trim();
              if (url.isNotEmpty) {
                playbackUrl = url;
                extension = _extensionFromUrl(url) ?? '.m4a';
                break;
              }
            }
          }
          if (playbackUrl.isNotEmpty) {
            break;
          }
        }
      }
      if (playbackUrl.isEmpty) {
        final durlItems = data is Map ? data['durl'] : null;
        if (durlItems is List) {
          for (final item in durlItems.whereType<Map>()) {
            final url = (item['url'] ?? '').toString().trim();
            if (url.isNotEmpty) {
              playbackUrl = url;
              extension = _extensionFromUrl(url);
              break;
            }
            final backup = item['backup_url'];
            if (backup is List) {
              for (final candidate in backup) {
                final backupUrl = candidate.toString().trim();
                if (backupUrl.isNotEmpty) {
                  playbackUrl = backupUrl;
                  extension = _extensionFromUrl(backupUrl);
                  break;
                }
              }
            }
            if (playbackUrl.isNotEmpty) {
              break;
            }
          }
        }
      }
      if (playbackUrl.isEmpty) {
        return null;
      }

      return HitsResolvedAudioSource(
        playbackUrl: playbackUrl,
        provider: provider,
        providerTrackId: bvid,
        suggestedFileExtension: extension ?? '.m4a',
        coverUrl: coverUrl,
        playbackHeaders: <String, String>{
          'Referer': 'https://www.bilibili.com/video/$bvid',
        },
      );
    } catch (_) {
      return null;
    }
  }

  Future<HitsResolvedAudioSource?> _searchYouTube(
    HitsScheduleTrack track,
  ) async {
    final candidateById = <String, _YoutubeSearchCandidate>{};

    for (final query in _buildSearchQueries(
      track,
    ).take(_youtubeSearchQueryLimit)) {
      final results = await _searchYouTubeByQuery(query);
      for (final video in results.take(_youtubeCandidateLimit)) {
        final candidate = _YoutubeSearchCandidate.fromVideo(video);
        if (candidate == null) {
          continue;
        }
        final scored = candidate.copyWith(
          score: _scoreYouTubeCandidateMatch(
            requestedTrack: track,
            matched: candidate,
          ),
        );
        final existing = candidateById[scored.videoId];
        if (existing == null || scored.score > existing.score) {
          candidateById[scored.videoId] = scored;
        }
      }

      if (candidateById.values.any(
        (candidate) => candidate.score >= _youtubeMinMatchScore + 18,
      )) {
        break;
      }
    }

    final rankedCandidates = candidateById.values.toList(growable: false)
      ..sort((left, right) => right.score.compareTo(left.score));

    for (final candidate in rankedCandidates.take(_youtubeResolveLimit)) {
      if (candidate.score < _youtubeMinMatchScore) {
        break;
      }
      final resolved = await _resolveYouTubeById(candidate.videoId, track);
      if (resolved != null) {
        return resolved;
      }
    }

    return null;
  }

  Future<List<Video>> _searchYouTubeByQuery(String query) async {
    final trimmedQuery = query.trim();
    if (trimmedQuery.isEmpty) {
      return const <Video>[];
    }

    try {
      final results = await _youtube.search.search(trimmedQuery);
      return results.toList(growable: false);
    } catch (_) {
      return const <Video>[];
    }
  }

  Future<HitsResolvedAudioSource?> _resolveYouTubeById(
    String videoId,
    HitsScheduleTrack track,
  ) async {
    try {
      final manifest = await _youtube.videos.streams.getManifest(
        videoId,
        ytClients: _youtubeClients,
      );

      final audioOnly = _selectPreferredAudioOnlyStream(manifest.audioOnly);
      if (audioOnly != null) {
        return HitsResolvedAudioSource(
          playbackUrl: audioOnly.url.toString(),
          provider: _youtubeProvider,
          providerTrackId: videoId,
          suggestedFileExtension: '.${audioOnly.container.name}',
          coverUrl: track.coverUrl?.toString(),
        );
      }

      final muxed = _selectPreferredMuxedAudioStream(manifest.audio);
      if (muxed != null) {
        return HitsResolvedAudioSource(
          playbackUrl: muxed.url.toString(),
          provider: _youtubeProvider,
          providerTrackId: videoId,
          suggestedFileExtension: '.${muxed.container.name}',
          coverUrl: track.coverUrl?.toString(),
        );
      }
    } catch (_) {
      return null;
    }

    return null;
  }

  AudioOnlyStreamInfo? _selectPreferredAudioOnlyStream(
    Iterable<AudioOnlyStreamInfo> streams,
  ) {
    final pool = streams.toList(growable: false);
    if (pool.isEmpty) {
      return null;
    }
    final preferred = pool
        .where(
          (stream) => _preferredYoutubeContainers.contains(
            stream.container.name.toLowerCase(),
          ),
        )
        .toList(growable: false);
    final candidates = preferred.isNotEmpty ? preferred : pool;
    candidates.sort((left, right) {
      return right.bitrate.bitsPerSecond.compareTo(left.bitrate.bitsPerSecond);
    });
    return candidates.first;
  }

  AudioStreamInfo? _selectPreferredMuxedAudioStream(
    Iterable<AudioStreamInfo> streams,
  ) {
    final pool = streams.toList(growable: false);
    if (pool.isEmpty) {
      return null;
    }
    final preferred = pool
        .where(
          (stream) => _preferredYoutubeContainers.contains(
            stream.container.name.toLowerCase(),
          ),
        )
        .toList(growable: false);
    final candidates = preferred.isNotEmpty ? preferred : pool;
    candidates.sort((left, right) {
      return right.bitrate.bitsPerSecond.compareTo(left.bitrate.bitsPerSecond);
    });
    return candidates.first;
  }

  Future<HitsResolvedAudioSource?> _searchAudius(
    HitsScheduleTrack track,
  ) async {
    _AudiusCandidate? bestMatch;
    var bestScore = -9999;

    for (final query in _buildSearchQueries(track)) {
      final rows = await _searchAudiusByQuery(query);
      for (final row in rows) {
        final candidate = _AudiusCandidate.fromJson(row);
        if (candidate == null) {
          continue;
        }
        final score = _scoreAudiusCandidateMatch(
          requestedTrack: track,
          matched: candidate,
        );
        if (score > bestScore) {
          bestScore = score;
          bestMatch = candidate;
        }
      }
      if (bestScore >= _minMatchScore + 18) {
        break;
      }
    }

    if (bestMatch == null || bestScore < _minMatchScore) {
      return null;
    }

    return HitsResolvedAudioSource(
      playbackUrl: _audiusStreamEndpoint(bestMatch.id),
      provider: _audiusProvider,
      providerTrackId: bestMatch.id,
      suggestedFileExtension: '.mp3',
      coverUrl: track.coverUrl?.toString(),
    );
  }

  Future<List<Map<String, dynamic>>> _searchAudiusByQuery(String query) async {
    final trimmedQuery = query.trim();
    if (trimmedQuery.isEmpty) {
      return const <Map<String, dynamic>>[];
    }

    final uri = Uri.https(_audiusHost, '$_audiusBasePath/tracks/search', {
      'query': trimmedQuery,
      'limit': '$_searchLimit',
    });

    try {
      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 6));
      request.headers.set(HttpHeaders.acceptHeader, 'application/json');
      request.headers.set(
        HttpHeaders.userAgentHeader,
        'PrismWave/HITS (+https://github.com/shanbei2033/PrismWave)',
      );
      final response = await request.close().timeout(const Duration(seconds: 6));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return const <Map<String, dynamic>>[];
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final data = decoded is Map ? decoded['data'] : null;
      if (data is! List) {
        return const <Map<String, dynamic>>[];
      }

      return data
          .whereType<Map>()
          .map((item) => item.cast<String, dynamic>())
          .toList(growable: false);
    } on TimeoutException {
      return const <Map<String, dynamic>>[];
    } on SocketException {
      return const <Map<String, dynamic>>[];
    } on HttpException {
      return const <Map<String, dynamic>>[];
    } on FormatException {
      return const <Map<String, dynamic>>[];
    } catch (_) {
      return const <Map<String, dynamic>>[];
    }
  }

  Future<HitsResolvedAudioSource?> _searchNetease(
    HitsScheduleTrack track,
  ) async {
    final candidateBySongId = <int, _NeteaseCandidate>{};

    for (final query in _buildSearchQueries(track).take(_neteaseSearchQueryLimit)) {
      final results = await _searchNeteaseByQuery(query);
      for (final candidate in results.take(_neteaseCandidateLimit)) {
        final scored = candidate.copyWith(
          score: _scoreNeteaseCandidateMatch(
            requestedTrack: track,
            matched: candidate,
          ),
        );
        final existing = candidateBySongId[scored.songId];
        if (existing == null || scored.score > existing.score) {
          candidateBySongId[scored.songId] = scored;
        }
      }

      if (candidateBySongId.values.any(
        (candidate) => candidate.score >= _neteaseMinMatchScore + 18,
      )) {
        break;
      }
    }

    final rankedCandidates = candidateBySongId.values.toList(growable: false)
      ..sort((left, right) => right.score.compareTo(left.score));

    for (final candidate in rankedCandidates.take(_neteaseResolveLimit)) {
      if (candidate.score < _neteaseMinMatchScore) {
        break;
      }
      final resolved = await _resolveNeteaseStream(
        songId: candidate.songId,
        coverUrl: candidate.coverUrl,
      );
      if (resolved != null) {
        return resolved;
      }
    }

    return null;
  }

  Future<List<_NeteaseCandidate>> _searchNeteaseByQuery(String query) async {
    final trimmedQuery = query.trim();
    if (trimmedQuery.isEmpty) {
      return const <_NeteaseCandidate>[];
    }

    final uri = Uri.https('music.163.com', '/api/search/get', {
      's': trimmedQuery,
      'type': '1',
      'limit': '$_neteaseSearchQueryLimit',
    });

    try {
      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 3));
      _neteaseHeaders.forEach(request.headers.set);
      final response = await request.close().timeout(const Duration(seconds: 4));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return const <_NeteaseCandidate>[];
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final result = decoded is Map ? decoded['result'] : null;
      final songs = result is Map ? result['songs'] : null;
      if (songs is! List) {
        return const <_NeteaseCandidate>[];
      }

      return songs
          .whereType<Map>()
          .map(
            (item) =>
                _NeteaseCandidate.fromJson(Map<String, dynamic>.from(item)),
          )
          .whereType<_NeteaseCandidate>()
          .toList(growable: false);
    } catch (_) {
      return const <_NeteaseCandidate>[];
    }
  }

  Future<HitsResolvedAudioSource?> _resolveNeteaseStream({
    required int songId,
    String? coverUrl,
  }) async {
    if (songId <= 0) {
      return null;
    }

    try {
      final url =
          'https://music.163.com/song/media/outer/url?id=$songId.mp3';

      final request = await _httpClient
          .getUrl(Uri.parse(url))
          .timeout(const Duration(seconds: 4));
      request.headers.set('User-Agent', _neteaseHeaders['User-Agent']!);
      request.headers.set('Referer', 'https://music.163.com/');
      final response = await request.close().timeout(const Duration(seconds: 5));

      final statusCode = response.statusCode;
      await response.drain<void>();

      if (statusCode >= 200 && statusCode < 400) {
        return HitsResolvedAudioSource(
          playbackUrl: url,
          provider: _neteaseProvider,
          providerTrackId: '$songId',
          suggestedFileExtension: '.mp3',
          coverUrl: coverUrl,
          playbackHeaders: <String, String>{
            'Referer': 'https://music.163.com/',
          },
        );
      }

      return null;
    } catch (_) {
      return null;
    }
  }

  int _scoreNeteaseCandidateMatch({
    required HitsScheduleTrack requestedTrack,
    required _NeteaseCandidate matched,
  }) {
    final requestedTitle = _normalizeText(requestedTrack.title);
    final simplifiedTitle = _normalizeText(
      _simplifyTrackTitleForSearch(_stripSearchDecorations(requestedTrack.title)),
    );
    final requestedArtist = _normalizeText(requestedTrack.artist);
    final matchedTitle = _normalizeText(matched.title);
    final matchedArtist = _normalizeText(matched.artist);

    var score = 0;
    score += _stringMatchScore(
      requestedTitle,
      matchedTitle,
      exact: 56,
      partial: 26,
    );
    if (simplifiedTitle.isNotEmpty && simplifiedTitle != requestedTitle) {
      score += _stringMatchScore(
        simplifiedTitle,
        matchedTitle,
        exact: 34,
        partial: 18,
      );
    }
    score += _stringMatchScore(
      requestedArtist,
      matchedArtist,
      exact: 32,
      partial: 14,
    );
    score += _durationScore(
      requestedTrack.duration.inMilliseconds,
      matched.durationMs,
    );
    score -= _variantPenalty(
      sourceTitle: matched.title,
      requestedTitle: requestedTrack.title,
    );
    return score;
  }

  Future<String?> _fetchKuwoCsrfToken() async {
    final cached = _kuwoCsrfToken;
    final fetchedAt = _kuwoCsrfTokenFetchedAt;
    if (cached != null &&
        fetchedAt != null &&
        DateTime.now().toUtc().difference(fetchedAt) < _kuwoCsrfTokenTtl) {
      return cached;
    }

    try {
      final request = await _httpClient
          .getUrl(Uri.https('www.kuwo.cn', '/'))
          .timeout(const Duration(seconds: 3));
      request.headers.set('User-Agent', _kuwoHeaders['User-Agent']!);
      final response = await request.close().timeout(const Duration(seconds: 3));
      await response.drain<void>();

      final cookies = response.cookies.where(
        (cookie) => cookie.name == 'kw_token',
      );
      if (cookies.isNotEmpty) {
        _kuwoCsrfToken = cookies.first.value;
        _kuwoCsrfTokenFetchedAt = DateTime.now().toUtc();
        return _kuwoCsrfToken;
      }
    } catch (_) {}

    return cached;
  }

  Future<HitsResolvedAudioSource?> _searchKuwo(
    HitsScheduleTrack track,
  ) async {
    final csrfToken = await _fetchKuwoCsrfToken();

    final candidateByMid = <String, _KuwoCandidate>{};

    for (final query in _buildSearchQueries(track).take(_kuwoSearchQueryLimit)) {
      final results = await _searchKuwoByQuery(query, csrfToken: csrfToken ?? '');
      for (final candidate in results.take(_kuwoCandidateLimit)) {
        final scored = candidate.copyWith(
          score: _scoreKuwoCandidateMatch(
            requestedTrack: track,
            matched: candidate,
          ),
        );
        final existing = candidateByMid[scored.mid];
        if (existing == null || scored.score > existing.score) {
          candidateByMid[scored.mid] = scored;
        }
      }

      if (candidateByMid.values.any(
        (candidate) => candidate.score >= _kuwoMinMatchScore + 18,
      )) {
        break;
      }
    }

    final rankedCandidates = candidateByMid.values.toList(growable: false)
      ..sort((left, right) => right.score.compareTo(left.score));

    for (final candidate in rankedCandidates.take(_kuwoResolveLimit)) {
      if (candidate.score < _kuwoMinMatchScore) {
        break;
      }
      final resolved = await _resolveKuwoStream(
        mid: candidate.mid,
        csrfToken: csrfToken ?? '',
      );
      if (resolved != null) {
        return resolved;
      }
    }

    return null;
  }

  Future<List<_KuwoCandidate>> _searchKuwoByQuery(
    String query, {
    required String csrfToken,
  }) async {
    final trimmedQuery = query.trim();
    if (trimmedQuery.isEmpty) {
      return const <_KuwoCandidate>[];
    }

    final uri = Uri.https(
      'www.kuwo.cn',
      '/api/www/search/searchMusicBykeyWord',
      {'key': trimmedQuery, 'pn': '1', 'rn': '15'},
    );

    try {
      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 3));
      _kuwoHeaders.forEach(request.headers.set);
      request.headers.set('csrf', csrfToken);
      request.headers.set(HttpHeaders.cookieHeader, 'kw_token=$csrfToken');
      final response = await request.close().timeout(const Duration(seconds: 4));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return const <_KuwoCandidate>[];
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final data = decoded is Map ? decoded['data'] : null;
      final list = data is Map ? data['list'] : null;
      if (list is! List) {
        return const <_KuwoCandidate>[];
      }

      return list
          .whereType<Map>()
          .map(
            (item) => _KuwoCandidate.fromJson(Map<String, dynamic>.from(item)),
          )
          .whereType<_KuwoCandidate>()
          .toList(growable: false);
    } catch (_) {
      return const <_KuwoCandidate>[];
    }
  }

  Future<HitsResolvedAudioSource?> _resolveKuwoStream({
    required String mid,
    String? csrfToken,
  }) async {
    final token = (csrfToken ?? await _fetchKuwoCsrfToken() ?? '').trim();
    if (mid.isEmpty) {
      return null;
    }

    try {
      final uri = Uri.https(
        'www.kuwo.cn',
        '/api/v1/www/music/playUrl',
        {'mid': mid, 'type': 'music'},
      );

      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 3));
      _kuwoHeaders.forEach(request.headers.set);
      if (token.isNotEmpty) {
        request.headers.set('csrf', token);
        request.headers.set(HttpHeaders.cookieHeader, 'kw_token=$token');
      }
      final response = await request.close().timeout(const Duration(seconds: 5));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return null;
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final data = decoded is Map ? decoded['data'] : null;
      final playbackUrl = (data is Map ? data['url'] : null)?.toString().trim() ?? '';
      if (playbackUrl.isEmpty || playbackUrl == 'null') {
        return null;
      }

      return HitsResolvedAudioSource(
        playbackUrl: playbackUrl,
        provider: _kuwoProvider,
        providerTrackId: mid,
        suggestedFileExtension: _extensionFromUrl(playbackUrl) ?? '.mp3',
      );
    } catch (_) {
      return null;
    }
  }

  int _scoreKuwoCandidateMatch({
    required HitsScheduleTrack requestedTrack,
    required _KuwoCandidate matched,
  }) {
    final requestedTitle = _normalizeText(requestedTrack.title);
    final simplifiedTitle = _normalizeText(
      _simplifyTrackTitleForSearch(_stripSearchDecorations(requestedTrack.title)),
    );
    final requestedArtist = _normalizeText(requestedTrack.artist);
    final matchedTitle = _normalizeText(matched.title);
    final matchedArtist = _normalizeText(matched.artist);

    var score = 0;
    score += _stringMatchScore(
      requestedTitle,
      matchedTitle,
      exact: 56,
      partial: 26,
    );
    if (simplifiedTitle.isNotEmpty && simplifiedTitle != requestedTitle) {
      score += _stringMatchScore(
        simplifiedTitle,
        matchedTitle,
        exact: 34,
        partial: 18,
      );
    }
    score += _stringMatchScore(
      requestedArtist,
      matchedArtist,
      exact: 32,
      partial: 14,
    );
    score += _durationScore(
      requestedTrack.duration.inMilliseconds,
      matched.durationMs,
    );
    score -= _variantPenalty(
      sourceTitle: matched.title,
      requestedTitle: requestedTrack.title,
    );
    return score;
  }

  Future<HitsResolvedAudioSource?> _searchMigu(
    HitsScheduleTrack track,
  ) async {
    final candidateByCopyrightId = <String, _MiguCandidate>{};

    for (final query in _buildSearchQueries(track).take(_miguSearchQueryLimit)) {
      final results = await _searchMiguByQuery(query);
      for (final candidate in results.take(_miguCandidateLimit)) {
        final scored = candidate.copyWith(
          score: _scoreMiguCandidateMatch(
            requestedTrack: track,
            matched: candidate,
          ),
        );
        final existing = candidateByCopyrightId[scored.copyrightId];
        if (existing == null || scored.score > existing.score) {
          candidateByCopyrightId[scored.copyrightId] = scored;
        }
      }

      if (candidateByCopyrightId.values.any(
        (candidate) => candidate.score >= _miguMinMatchScore + 18,
      )) {
        break;
      }
    }

    final rankedCandidates = candidateByCopyrightId.values.toList(growable: false)
      ..sort((left, right) => right.score.compareTo(left.score));

    for (final candidate in rankedCandidates.take(_miguResolveLimit)) {
      if (candidate.score < _miguMinMatchScore) {
        break;
      }
      final resolved = await _resolveMiguStream(
        copyrightId: candidate.copyrightId,
        coverUrl: candidate.coverUrl,
      );
      if (resolved != null) {
        return resolved;
      }
    }

    return null;
  }

  Future<List<_MiguCandidate>> _searchMiguByQuery(String query) async {
    final trimmedQuery = query.trim();
    if (trimmedQuery.isEmpty) {
      return const <_MiguCandidate>[];
    }

    final uri = Uri.https(
      'm.music.migu.cn',
      '/migu/remoting/scr_search_tag',
      {'keyword': trimmedQuery, 'type': '2', 'pgc': '1', 'rows': '15'},
    );

    try {
      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 3));
      _miguHeaders.forEach(request.headers.set);
      final response = await request.close().timeout(const Duration(seconds: 4));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return const <_MiguCandidate>[];
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final data = decoded is Map ? decoded['data'] : null;
      final musics = data is Map ? data['musics'] : null;
      if (musics is! List) {
        return const <_MiguCandidate>[];
      }

      return musics
          .whereType<Map>()
          .map(
            (item) =>
                _MiguCandidate.fromJson(Map<String, dynamic>.from(item)),
          )
          .whereType<_MiguCandidate>()
          .toList(growable: false);
    } catch (_) {
      return const <_MiguCandidate>[];
    }
  }

  Future<HitsResolvedAudioSource?> _resolveMiguStream({
    required String copyrightId,
    String? coverUrl,
  }) async {
    if (copyrightId.isEmpty) {
      return null;
    }

    try {
      final uri = Uri.https(
        'm.music.migu.cn',
        '/migu/remoting/cms_audio_play',
        {'copyrightId': copyrightId},
      );

      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 3));
      _miguHeaders.forEach(request.headers.set);
      final response = await request.close().timeout(const Duration(seconds: 5));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return null;
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final data = decoded is Map ? decoded['data'] : null;
      final playbackUrl = (data is Map ? data['playUrl'] : null)?.toString().trim();
      final effectiveUrl = playbackUrl ?? '';
      if (effectiveUrl.isEmpty) {
        return null;
      }

      return HitsResolvedAudioSource(
        playbackUrl: effectiveUrl,
        provider: _miguProvider,
        providerTrackId: copyrightId,
        suggestedFileExtension: _extensionFromUrl(effectiveUrl) ?? '.mp3',
        coverUrl: coverUrl,
      );
    } catch (_) {
      return null;
    }
  }

  int _scoreMiguCandidateMatch({
    required HitsScheduleTrack requestedTrack,
    required _MiguCandidate matched,
  }) {
    final requestedTitle = _normalizeText(requestedTrack.title);
    final simplifiedTitle = _normalizeText(
      _simplifyTrackTitleForSearch(_stripSearchDecorations(requestedTrack.title)),
    );
    final requestedArtist = _normalizeText(requestedTrack.artist);
    final matchedTitle = _normalizeText(matched.title);
    final matchedArtist = _normalizeText(matched.artist);

    var score = 0;
    score += _stringMatchScore(
      requestedTitle,
      matchedTitle,
      exact: 56,
      partial: 26,
    );
    if (simplifiedTitle.isNotEmpty && simplifiedTitle != requestedTitle) {
      score += _stringMatchScore(
        simplifiedTitle,
        matchedTitle,
        exact: 34,
        partial: 18,
      );
    }
    score += _stringMatchScore(
      requestedArtist,
      matchedArtist,
      exact: 32,
      partial: 14,
    );
    score += _durationScore(
      requestedTrack.duration.inMilliseconds,
      matched.durationMs,
    );
    score -= _variantPenalty(
      sourceTitle: matched.title,
      requestedTitle: requestedTrack.title,
    );
    return score;
  }

  Future<HitsResolvedAudioSource?> _searchQQ(
    HitsScheduleTrack track,
  ) async {
    final candidateBySongMid = <String, _QQCandidate>{};

    for (final query in _buildSearchQueries(track).take(_qqSearchQueryLimit)) {
      final results = await _searchQQByQuery(query);
      for (final candidate in results.take(_qqCandidateLimit)) {
        final scored = candidate.copyWith(
          score: _scoreQQCandidateMatch(
            requestedTrack: track,
            matched: candidate,
          ),
        );
        final existing = candidateBySongMid[scored.songMid];
        if (existing == null || scored.score > existing.score) {
          candidateBySongMid[scored.songMid] = scored;
        }
      }

      if (candidateBySongMid.values.any(
        (candidate) => candidate.score >= _qqMinMatchScore + 18,
      )) {
        break;
      }
    }

    final rankedCandidates = candidateBySongMid.values.toList(growable: false)
      ..sort((left, right) => right.score.compareTo(left.score));

    for (final candidate in rankedCandidates.take(_qqResolveLimit)) {
      if (candidate.score < _qqMinMatchScore) {
        break;
      }
      final resolved = await _resolveQQStream(
        songMid: candidate.songMid,
        coverUrl: candidate.coverUrl,
      );
      if (resolved != null) {
        return resolved;
      }
    }

    return null;
  }

  Future<List<_QQCandidate>> _searchQQByQuery(String query) async {
    final trimmedQuery = query.trim();
    if (trimmedQuery.isEmpty) {
      return const <_QQCandidate>[];
    }

    final uri = Uri.https('c.y.qq.com', '/soso/fcgi-bin/client_search_cp', {
      'ct': '24',
      'qqmusic_ver': '1298',
      'new_json': '1',
      'remoteplace': 'txt.yqq.song',
      'searchid': '1',
      't': '0',
      'aggr': '1',
      'cr': '1',
      'catZhida': '1',
      'lossless': '0',
      'flag_qc': '0',
      'p': '1',
      'n': '10',
      'w': trimmedQuery,
      'format': 'json',
    });

    try {
      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 3));
      _qqHeaders.forEach(request.headers.set);
      final response = await request.close().timeout(const Duration(seconds: 4));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return const <_QQCandidate>[];
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final data = decoded is Map ? decoded['data'] : null;
      final song = data is Map ? data['song'] : null;
      final list = song is Map ? song['list'] : null;
      if (list is! List) {
        return const <_QQCandidate>[];
      }

      return list
          .whereType<Map>()
          .map(
            (item) => _QQCandidate.fromJson(Map<String, dynamic>.from(item)),
          )
          .whereType<_QQCandidate>()
          .toList(growable: false);
    } catch (_) {
      return const <_QQCandidate>[];
    }
  }

  Future<HitsResolvedAudioSource?> _resolveQQStream({
    required String songMid,
    String? coverUrl,
  }) async {
    if (songMid.isEmpty) {
      return null;
    }

    final purl = await _fetchQQMusicVKey(songMid);
    if (purl == null) {
      return null;
    }

    final playbackUrl = _qqStreamUrl(purl);
    if (playbackUrl == null) {
      return null;
    }

    return HitsResolvedAudioSource(
      playbackUrl: playbackUrl,
      provider: _qqProvider,
      providerTrackId: songMid,
      suggestedFileExtension: '.m4a',
      coverUrl: coverUrl,
    );
  }

  Future<String?> _fetchQQMusicVKey(String songMid) async {
    try {
      final params = {
        'req_0': {
          'module': 'vkey.GetVkeyServer',
          'method': 'CgiGetVkey',
          'param': {
            'guid': '0',
            'songmid': [songMid],
            'songtype': [0],
            'uin': '0',
            'loginflag': 0,
            'platform': '20',
          },
        },
      };

      final uri = Uri.https(
        'u.y.qq.com',
        '/cgi-bin/musicu.fcg',
        {'format': 'json', 'data': jsonEncode(params)},
      );

      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 4));
      _qqHeaders.forEach(request.headers.set);
      final response = await request.close().timeout(const Duration(seconds: 4));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return null;
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final req0 = decoded is Map ? decoded['req_0'] : null;
      final reqData = req0 is Map ? req0['data'] : null;
      final midurlinfo = reqData is Map ? reqData['midurlinfo'] : null;
      if (midurlinfo is! List || midurlinfo.isEmpty) {
        return null;
      }

      return (midurlinfo.first as Map?)?.cast<String, dynamic>()['purl']?.toString().trim() ?? '';
    } catch (_) {
      return null;
    }
  }

  String? _qqStreamUrl(String purl) {
    final trimmed = purl.trim();
    if (trimmed.isEmpty) {
      return null;
    }

    if (trimmed.startsWith('http://') || trimmed.startsWith('https://')) {
      return trimmed;
    }

    return 'http://ws.stream.qqmusic.qq.com/$trimmed';
  }

  int _scoreQQCandidateMatch({
    required HitsScheduleTrack requestedTrack,
    required _QQCandidate matched,
  }) {
    final requestedTitle = _normalizeText(requestedTrack.title);
    final simplifiedTitle = _normalizeText(
      _simplifyTrackTitleForSearch(_stripSearchDecorations(requestedTrack.title)),
    );
    final requestedArtist = _normalizeText(requestedTrack.artist);
    final matchedTitle = _normalizeText(matched.title);
    final matchedArtist = _normalizeText(matched.artist);

    var score = 0;
    score += _stringMatchScore(
      requestedTitle,
      matchedTitle,
      exact: 56,
      partial: 26,
    );
    if (simplifiedTitle.isNotEmpty && simplifiedTitle != requestedTitle) {
      score += _stringMatchScore(
        simplifiedTitle,
        matchedTitle,
        exact: 34,
        partial: 18,
      );
    }
    score += _stringMatchScore(
      requestedArtist,
      matchedArtist,
      exact: 32,
      partial: 14,
    );
    score += _durationScore(
      requestedTrack.duration.inMilliseconds,
      matched.durationMs,
    );
    score -= _variantPenalty(
      sourceTitle: matched.title,
      requestedTitle: requestedTrack.title,
    );
    return score;
  }

  Future<HitsResolvedAudioSource?> _searchKugou(
    HitsScheduleTrack track,
  ) async {
    final candidateByHash = <String, _KugouCandidate>{};

    for (final query in _buildSearchQueries(track).take(_kugouSearchQueryLimit)) {
      final results = await _searchKugouByQuery(query);
      for (final candidate in results.take(_kugouCandidateLimit)) {
        final scored = candidate.copyWith(
          score: _scoreKugouCandidateMatch(
            requestedTrack: track,
            matched: candidate,
          ),
        );
        final existing = candidateByHash[scored.fileHash];
        if (existing == null || scored.score > existing.score) {
          candidateByHash[scored.fileHash] = scored;
        }
      }

      if (candidateByHash.values.any(
        (candidate) => candidate.score >= _kugouMinMatchScore + 18,
      )) {
        break;
      }
    }

    final rankedCandidates = candidateByHash.values.toList(growable: false)
      ..sort((left, right) => right.score.compareTo(left.score));

    for (final candidate in rankedCandidates.take(_kugouResolveLimit)) {
      if (candidate.score < _kugouMinMatchScore) {
        break;
      }
      final resolved = await _resolveKugouStream(
        fileHash: candidate.fileHash,
        coverUrl: candidate.coverUrl,
      );
      if (resolved != null) {
        return resolved;
      }
    }

    return null;
  }

  Future<List<_KugouCandidate>> _searchKugouByQuery(String query) async {
    final trimmedQuery = query.trim();
    if (trimmedQuery.isEmpty) {
      return const <_KugouCandidate>[];
    }

    final uri = Uri.https('m.kugou.com', '/api/v3/search/song', {
      'keyword': trimmedQuery,
      'page': '1',
      'pagesize': '15',
    });

    try {
      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 3));
      _kugouHeaders.forEach(request.headers.set);
      final response = await request.close().timeout(const Duration(seconds: 4));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return const <_KugouCandidate>[];
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final data = decoded is Map ? decoded['data'] : null;
      final info = data is Map ? data['info'] : null;
      if (info is! List) {
        return const <_KugouCandidate>[];
      }

      return info
          .whereType<Map>()
          .map(
            (item) =>
                _KugouCandidate.fromJson(Map<String, dynamic>.from(item)),
          )
          .whereType<_KugouCandidate>()
          .toList(growable: false);
    } catch (_) {
      return const <_KugouCandidate>[];
    }
  }

  Future<HitsResolvedAudioSource?> _resolveKugouStream({
    required String fileHash,
    String? coverUrl,
  }) async {
    if (fileHash.isEmpty) {
      return null;
    }

    try {
      final uri = Uri.https('m.kugou.com', '/app/i/getSongInfo.php', {
        'cmd': 'playInfo',
        'hash': fileHash,
      });

      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 4));
      _kugouHeaders.forEach(request.headers.set);
      final response = await request.close().timeout(const Duration(seconds: 5));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return null;
      }

      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      final playbackUrl = (decoded is Map ? decoded['url'] : null)?.toString().trim() ?? '';
      if (playbackUrl.isEmpty) {
        return null;
      }

      return HitsResolvedAudioSource(
        playbackUrl: playbackUrl,
        provider: _kugouProvider,
        providerTrackId: fileHash,
        suggestedFileExtension: _extensionFromUrl(playbackUrl) ?? '.mp3',
        coverUrl: coverUrl,
      );
    } catch (_) {
      return null;
    }
  }

  int _scoreKugouCandidateMatch({
    required HitsScheduleTrack requestedTrack,
    required _KugouCandidate matched,
  }) {
    final requestedTitle = _normalizeText(requestedTrack.title);
    final simplifiedTitle = _normalizeText(
      _simplifyTrackTitleForSearch(_stripSearchDecorations(requestedTrack.title)),
    );
    final requestedArtist = _normalizeText(requestedTrack.artist);
    final matchedTitle = _normalizeText(matched.title);
    final matchedArtist = _normalizeText(matched.artist);

    var score = 0;
    score += _stringMatchScore(
      requestedTitle,
      matchedTitle,
      exact: 56,
      partial: 26,
    );
    if (simplifiedTitle.isNotEmpty && simplifiedTitle != requestedTitle) {
      score += _stringMatchScore(
        simplifiedTitle,
        matchedTitle,
        exact: 34,
        partial: 18,
      );
    }
    score += _stringMatchScore(
      requestedArtist,
      matchedArtist,
      exact: 32,
      partial: 14,
    );
    score += _durationScore(
      requestedTrack.duration.inMilliseconds,
      matched.durationMs,
    );
    score -= _variantPenalty(
      sourceTitle: matched.title,
      requestedTitle: requestedTrack.title,
    );
    return score;
  }

  List<String> _buildSearchQueries(HitsScheduleTrack track) {
    final queries = <String>{};

    void add(String value) {
      final trimmed = value.trim();
      if (trimmed.isNotEmpty) {
        queries.add(trimmed);
      }
    }

    final cleanTitle = _stripSearchDecorations(track.title);
    final cleanArtist = _stripSearchDecorations(track.artist);
    final simplifiedTitle = _simplifyTrackTitleForSearch(cleanTitle);

    add('$simplifiedTitle $cleanArtist');
    add(simplifiedTitle);
    add(track.searchQuery);
    add('${track.title} ${track.artist}');
    add('$cleanTitle $cleanArtist');
    add(cleanTitle);
    add(track.title);

    final titleVariants = <String>{track.title, cleanTitle, ...track.titleVariants};
    final artistVariants = <String>{
      track.artist,
      cleanArtist,
      ...track.artistVariants,
    };
    for (final title in titleVariants) {
      for (final artist in artistVariants) {
        add('$title $artist');
      }
    }

    return queries.take(8).toList(growable: false);
  }

  List<String> _buildBilibiliSearchQueries(
    HitsScheduleTrack track, {
    required String provider,
  }) {
    final queries = <String>{..._buildSearchQueries(track)};
    final cleanTitle = _stripSearchDecorations(track.title);
    final cleanArtist = _stripSearchDecorations(track.artist);
    final simplifiedTitle = _simplifyTrackTitleForSearch(cleanTitle);

    void add(String value) {
      final trimmed = value.trim();
      if (trimmed.isNotEmpty) {
        queries.add(trimmed);
      }
    }

    if (provider == _bilibiliProvider) {
      add('$simplifiedTitle $cleanArtist 官方音频');
      add('$simplifiedTitle $cleanArtist 歌曲');
      add('$simplifiedTitle $cleanArtist 官方');
    } else {
      add('$simplifiedTitle $cleanArtist mv');
      add('$simplifiedTitle $cleanArtist 现场');
      add('$simplifiedTitle $cleanArtist 视频');
    }

    return queries.take(8).toList(growable: false);
  }
  int _scoreAudiusCandidateMatch({
    required HitsScheduleTrack requestedTrack,
    required _AudiusCandidate matched,
  }) {
    final requestedTitle = _normalizeText(requestedTrack.title);
    final requestedArtist = _normalizeText(requestedTrack.artist);
    final matchedTitle = _normalizeText(matched.title);
    final matchedArtist = _normalizeText(matched.artist);
    final rawTitle = _normalizeText(matched.rawTitle);
    final uploader = _normalizeText(matched.uploader);

    var score = 0;
    score += _stringMatchScore(
      requestedTitle,
      matchedTitle,
      exact: 60,
      partial: 28,
    );
    if (score < 24) {
      score += _stringMatchScore(
        requestedTitle,
        rawTitle,
        exact: 40,
        partial: 18,
      );
    }
    score += _max(
      _stringMatchScore(
        requestedArtist,
        matchedArtist,
        exact: 44,
        partial: 18,
      ),
      _stringMatchScore(requestedArtist, uploader, exact: 18, partial: 8),
    );
    score += _durationScore(
      requestedTrack.duration.inMilliseconds,
      matched.durationMs,
    );
    if (!matched.isStreamable || !matched.isAvailable) {
      score -= 1000;
    }
    score -= _variantPenalty(
      sourceTitle: matched.rawTitle,
      requestedTitle: requestedTrack.title,
    );
    return score;
  }

  int _scoreYouTubeCandidateMatch({
    required HitsScheduleTrack requestedTrack,
    required _YoutubeSearchCandidate matched,
  }) {
    final requestedTitle = _normalizeText(requestedTrack.title);
    final simplifiedTitle = _normalizeText(
      _simplifyTrackTitleForSearch(_stripSearchDecorations(requestedTrack.title)),
    );
    final requestedArtist = _normalizeText(requestedTrack.artist);
    final matchedTitle = _normalizeText(matched.title);
    final matchedAuthor = _normalizeText(matched.author);

    var score = 0;
    score += _stringMatchScore(
      requestedTitle,
      matchedTitle,
      exact: 64,
      partial: 30,
    );
    if (simplifiedTitle.isNotEmpty && simplifiedTitle != requestedTitle) {
      score += _stringMatchScore(
        simplifiedTitle,
        matchedTitle,
        exact: 40,
        partial: 20,
      );
    }
    score += _stringMatchScore(
      requestedArtist,
      matchedAuthor,
      exact: 38,
      partial: 16,
    );
    score += _stringMatchScore(
      requestedArtist,
      matchedTitle,
      exact: 20,
      partial: 10,
    );
    score += _durationScore(
      requestedTrack.duration.inMilliseconds,
      matched.durationMs,
    );
    if (matched.isLive) {
      score -= 80;
    }
    score -= _variantPenalty(
      sourceTitle: matched.title,
      requestedTitle: requestedTrack.title,
    );
    score -= _youtubeSoftPenalty(title: matched.title, author: matched.author);
    if (_looksLikeOfficialYouTubeSource(matched.author, matched.title)) {
      score += 8;
    }
    return score;
  }

  int _scoreBilibiliCandidateMatch({
    required HitsScheduleTrack requestedTrack,
    required _BilibiliCandidate matched,
    required String provider,
  }) {
    final requestedTitle = _normalizeText(requestedTrack.title);
    final simplifiedTitle = _normalizeText(
      _simplifyTrackTitleForSearch(_stripSearchDecorations(requestedTrack.title)),
    );
    final requestedArtist = _normalizeText(requestedTrack.artist);
    final matchedTitle = _normalizeText(matched.title);
    final matchedAuthor = _normalizeText(matched.author);

    var score = 0;
    score += _stringMatchScore(
      requestedTitle,
      matchedTitle,
      exact: 56,
      partial: 28,
    );
    if (simplifiedTitle.isNotEmpty && simplifiedTitle != requestedTitle) {
      score += _stringMatchScore(
        simplifiedTitle,
        matchedTitle,
        exact: 34,
        partial: 18,
      );
    }
    score += _stringMatchScore(
      requestedArtist,
      matchedAuthor,
      exact: 24,
      partial: 10,
    );
    score += _stringMatchScore(
      requestedArtist,
      matchedTitle,
      exact: 18,
      partial: 8,
    );
    score += _durationScore(
      requestedTrack.duration.inMilliseconds,
      matched.durationMs,
    );
    if (_looksLikePreferredBilibiliPublisher(
      matched.author,
      matched.title,
      provider: provider,
    )) {
      score += provider == _bilibiliProvider ? 12 : 6;
    }
    score -= _variantPenalty(
      sourceTitle: matched.title,
      requestedTitle: requestedTrack.title,
    );
    score -= _videoSoftPenalty(
      title: matched.title,
      author: matched.author,
      provider: provider,
    );
    if (provider == _bilibiliProvider &&
        _looksLikeAudioFocusedBilibiliResult(matched.title, matched.author)) {
      score += 10;
    }
    if (provider == _biliVideoProvider &&
        _looksLikeVideoFocusedBilibiliResult(matched.title, matched.author)) {
      score += 8;
    }
    return score;
  }


  int _durationScore(int requestedDurationMs, int matchedDurationMs) {
    if (requestedDurationMs <= 0 || matchedDurationMs <= 0) {
      return 0;
    }
    final deltaSeconds = (requestedDurationMs - matchedDurationMs).abs() / 1000.0;
    if (deltaSeconds <= 2) return 18;
    if (deltaSeconds <= 5) return 12;
    if (deltaSeconds <= 10) return 6;
    if (deltaSeconds <= 20) return 2;
    return 0;
  }

  int _stringMatchScore(
    String left,
    String right, {
    required int exact,
    required int partial,
  }) {
    if (left.isEmpty || right.isEmpty) {
      return 0;
    }
    if (left == right) {
      return exact;
    }
    if (left.contains(right) || right.contains(left)) {
      return partial;
    }
    return _tokenOverlapScore(left, right);
  }

  int _tokenOverlapScore(String left, String right) {
    final leftTokens = left
        .split(' ')
        .where((item) => item.trim().isNotEmpty)
        .toSet();
    final rightTokens = right
        .split(' ')
        .where((item) => item.trim().isNotEmpty)
        .toSet();
    if (leftTokens.isEmpty || rightTokens.isEmpty) {
      return 0;
    }
    final overlap =
        leftTokens.intersection(rightTokens).length /
        _max(leftTokens.length, rightTokens.length);
    if (overlap >= 0.8) return 14;
    if (overlap >= 0.55) return 8;
    return 0;
  }

  int _variantPenalty({
    required String sourceTitle,
    required String requestedTitle,
  }) {
    final sourceVariants = _detectVariantKeywords(sourceTitle);
    final requestedVariants = _detectVariantKeywords(requestedTitle);
    final extra = sourceVariants.difference(requestedVariants);
    if (extra.isEmpty) {
      return 0;
    }
    return extra.length == 1 ? 28 : 40;
  }

  int _youtubeSoftPenalty({required String title, required String author}) {
    final normalizedTitle = _normalizeText(title);
    final normalizedAuthor = _normalizeText(author);
    var penalty = 0;
    if (normalizedTitle.contains('lyrics') ||
        normalizedTitle.contains('lyric')) {
      penalty += 6;
    }
    if (normalizedTitle.contains('visualizer')) {
      penalty += 6;
    }
    if (normalizedTitle.contains('sped up') ||
        normalizedTitle.contains('spedup')) {
      penalty += 18;
    }
    if (normalizedTitle.contains('slowed') ||
        normalizedTitle.contains('reverb')) {
      penalty += 18;
    }
    if (normalizedTitle.contains('8d')) {
      penalty += 18;
    }
    if (normalizedAuthor.contains('karaoke')) {
      penalty += 22;
    }
    return penalty;
  }

  int _videoSoftPenalty({
    required String title,
    required String author,
    required String provider,
  }) {
    final normalizedTitle = _normalizeText(title);
    final normalizedAuthor = _normalizeText(author);
    var penalty = 0;
    if (normalizedTitle.contains('lyrics') ||
        normalizedTitle.contains('lyric')) {
      penalty += 8;
    }
    if (normalizedTitle.contains('karaoke') ||
        normalizedTitle.contains('伴奏')) {
      penalty += 22;
    }
    if (normalizedTitle.contains('cover') ||
        normalizedTitle.contains('翻唱')) {
      penalty += 18;
    }
    if (normalizedAuthor.contains('搬运') || normalizedAuthor.contains('教程')) {
      penalty += 10;
    }
    if (normalizedTitle.contains('剪辑')) {
      penalty += 10;
    }
    if (provider == _bilibiliProvider) {
      if (normalizedTitle.contains('live') || normalizedTitle.contains('现场')) {
        penalty += 18;
      }
      if (normalizedTitle.contains('mv') || normalizedTitle.contains('video')) {
        penalty += 6;
      }
    } else {
      if (normalizedTitle.contains('live') || normalizedTitle.contains('现场')) {
        penalty += 4;
      }
    }
    return penalty;
  }

  bool _looksLikeAudioFocusedBilibiliResult(String title, String author) {
    final normalizedTitle = _normalizeText(title);
    final normalizedAuthor = _normalizeText(author);
    return normalizedTitle.contains('官方音频') ||
        normalizedTitle.contains('歌曲') ||
        normalizedTitle.contains('音频') ||
        normalizedAuthor.contains('音乐');
  }

  bool _looksLikeVideoFocusedBilibiliResult(String title, String author) {
    final normalizedTitle = _normalizeText(title);
    final normalizedAuthor = _normalizeText(author);
    return normalizedTitle.contains('mv') ||
        normalizedTitle.contains('视频') ||
        normalizedTitle.contains('现场') ||
        normalizedTitle.contains('live') ||
        normalizedAuthor.contains('视频');
  }

  bool _looksLikeOfficialYouTubeSource(String author, String title) {
    final normalizedAuthor = _normalizeText(author);
    final normalizedTitle = _normalizeText(title);
    return normalizedAuthor.contains('topic') ||
        normalizedAuthor.contains('vevo') ||
        normalizedTitle.contains('official audio') ||
        normalizedTitle.contains('official video');
  }

  bool _looksLikePreferredBilibiliPublisher(
    String author,
    String title, {
    required String provider,
  }) {
    final normalizedAuthor = _normalizeText(author);
    final normalizedTitle = _normalizeText(title);
    final commonPreferred = normalizedAuthor.contains('索尼音乐中国') ||
        normalizedAuthor.contains('bilibili音乐') ||
        normalizedTitle.contains('官方') ||
        normalizedTitle.contains('official');
    if (provider == _bilibiliProvider) {
      return commonPreferred ||
          normalizedTitle.contains('官方音频') ||
          normalizedTitle.contains('歌曲');
    }
    return commonPreferred ||
        normalizedTitle.contains('mv') ||
        normalizedTitle.contains('现场') ||
        normalizedTitle.contains('视频');
  }

  Set<String> _detectVariantKeywords(String value) {
    final normalized = _normalizeText(value);
    final detected = <String>{};
    for (final keyword in _variantKeywords) {
      final normalizedKeyword = _normalizeText(keyword);
      if (normalizedKeyword.isNotEmpty &&
          normalized.contains(normalizedKeyword)) {
        detected.add(normalizedKeyword);
      }
    }
    return detected;
  }

  String _normalizeText(String value) {
    var normalized = value.toLowerCase();
    normalized = normalized.replaceAll(RegExp(r'<[^>]+>'), ' ');
    normalized = normalized.replaceAll(RegExp(r'\([^)]*\)'), ' ');
    normalized = normalized.replaceAll(RegExp(r'\[[^\]]*\]'), ' ');
    normalized = normalized.replaceAll(
      RegExp(r'\b(feat|ft|with)\b', caseSensitive: false),
      ' ',
    );
    normalized = normalized.replaceAll(RegExp(r'_+'), ' ');
    normalized = normalized.replaceAll(
      RegExp(r'[^a-z0-9\u4e00-\u9fff]+', caseSensitive: false),
      ' ',
    );
    return normalized
        .split(RegExp(r'\s+'))
        .where((item) => item.trim().isNotEmpty)
        .join(' ');
  }

  String _stripSearchDecorations(String value) {
    return value
        .replaceAll(RegExp(r'<[^>]+>'), ' ')
        .replaceAll(RegExp(r'\[[^\]]*\]'), ' ')
        .replaceAll(RegExp(r'\([^)]*\)'), ' ')
        .replaceAll(RegExp(r'\b(feat|ft|with)\b', caseSensitive: false), ' ')
        .replaceAll(RegExp(r'\s+'), ' ')
        .trim();
  }

  String _simplifyTrackTitleForSearch(String value) {
    final trimmed = value.trim();
    if (trimmed.isEmpty) {
      return trimmed;
    }
    final separatorMatch = RegExp(r'\s[-:|/]\s').firstMatch(trimmed);
    if (separatorMatch != null) {
      final left = trimmed.substring(0, separatorMatch.start).trim();
      final right = trimmed.substring(separatorMatch.end).trim();
      if (left.isNotEmpty &&
          right.isNotEmpty &&
          _detectVariantKeywords(right).isNotEmpty) {
        return left;
      }
    }

    final simplified = trimmed
        .replaceAll(
          RegExp(
            r'\b(remix|edit|version|mix|live|vip|karaoke|instrumental|rework)\b',
            caseSensitive: false,
          ),
          ' ',
        )
        .replaceAll(RegExp(r'\s+'), ' ')
        .trim();
    return simplified.isEmpty ? trimmed : simplified;
  }

  String _audiusStreamEndpoint(String trackId) {
    final encodedTrackId = Uri.encodeComponent(trackId.trim());
    return 'https://$_audiusHost$_audiusBasePath/tracks/$encodedTrackId/stream';
  }

  String? _extensionFromUrl(String value) {
    final uri = Uri.tryParse(value);
    final path = uri?.path ?? value;
    final lastDot = path.lastIndexOf('.');
    if (lastDot < 0 || lastDot == path.length - 1) {
      return null;
    }
    final extension = path.substring(lastDot).trim().toLowerCase();
    if (!extension.startsWith('.') || extension.length > 8) {
      return null;
    }
    return extension;
  }

  Future<_HitsRoutingProfile> _loadRoutingProfile() async {
    final cached = _routingProfile;
    if (cached != null) {
      if (_isRoutingProfileStale(cached)) {
        unawaited(_refreshRoutingProfileInBackground());
      }
      return cached;
    }

    final pending = _pendingRoutingProfile;
    if (pending != null) {
      return pending;
    }

    final future = _loadRoutingProfileInternal().whenComplete(() {
      _pendingRoutingProfile = null;
    });
    _pendingRoutingProfile = future;
    return future;
  }

  Future<_HitsRoutingProfile> _loadRoutingProfileInternal() async {
    final prefs = await _prefs;
    final providerStats = _decodeProviderStats(
      prefs.getString(_prefProviderStats),
    );
    final cached = _decodeRoutingProfile(
      prefs.getString(_prefGeoPayload),
      providerStats: providerStats,
    );
    if (cached != null) {
      _routingProfile = cached;
      if (_isRoutingProfileStale(cached)) {
        unawaited(_refreshRoutingProfileInBackground());
      }
      return cached;
    }

    final regionInfo = await _probeSourceRegion();
    final profile = _HitsRoutingProfile(
      region: regionInfo.$1,
      countryCode: regionInfo.$2,
      detectedAt: DateTime.now().toUtc(),
      providerStats: providerStats,
    );
    _routingProfile = profile;
    await _persistRoutingProfile(profile);
    return profile;
  }

  Future<void> _refreshRoutingProfileInBackground() async {
    final pending = _pendingRoutingRefresh;
    if (pending != null) {
      return pending;
    }

    final future = _refreshRoutingProfileInternal().whenComplete(() {
      _pendingRoutingRefresh = null;
    });
    _pendingRoutingRefresh = future;
    return future;
  }

  Future<void> _refreshRoutingProfileInternal() async {
    final regionInfo = await _probeSourceRegion();
    final profile = _HitsRoutingProfile(
      region: regionInfo.$1,
      countryCode: regionInfo.$2,
      detectedAt: DateTime.now().toUtc(),
      providerStats: _routingProfile?.providerStats ?? const {},
    );
    _routingProfile = profile;
    await _persistRoutingProfile(profile);
  }

  Future<void> _recordProviderResult({
    required String provider,
    required bool success,
    required Duration latency,
  }) async {
    final key = provider.trim().toLowerCase();
    if (key.isEmpty) {
      return;
    }

    final profile = await _loadRoutingProfile();
    final currentStats = <String, Map<String, dynamic>>{
      for (final entry in profile.providerStats.entries)
        entry.key: Map<String, dynamic>.from(entry.value),
    };
    final previous = Map<String, dynamic>.from(currentStats[key] ?? const {});
    final successCount = (previous['successCount'] as num?)?.toInt() ?? 0;
    final failureCount = (previous['failureCount'] as num?)?.toInt() ?? 0;
    final consecutiveFailures =
        (previous['consecutiveFailures'] as num?)?.toInt() ?? 0;
    final averageLatencyMs =
        (previous['averageLatencyMs'] as num?)?.toDouble() ?? 0;
    final attempts = successCount + failureCount;
    final now = DateTime.now().toUtc().toIso8601String();

    currentStats[key] = <String, dynamic>{
      'successCount': success ? successCount + 1 : successCount,
      'failureCount': success ? failureCount : failureCount + 1,
      'consecutiveFailures': success ? 0 : consecutiveFailures + 1,
      'averageLatencyMs': attempts == 0
          ? latency.inMilliseconds.toDouble()
          : ((averageLatencyMs * attempts) + latency.inMilliseconds) /
              (attempts + 1),
      'lastSuccessAt': success ? now : previous['lastSuccessAt'],
      'lastFailureAt': success ? previous['lastFailureAt'] : now,
    };

    final nextProfile = _HitsRoutingProfile(
      region: profile.region,
      countryCode: profile.countryCode,
      detectedAt: profile.detectedAt,
      providerStats: currentStats,
    );
    _routingProfile = nextProfile;

    final prefs = await _prefs;
    await prefs.setString(_prefProviderStats, jsonEncode(currentStats));
  }

  Future<(_HitsSourceRegion, String)> _probeSourceRegion() async {
    final countryCode =
        await _probeCountryCodeFromIp() ?? _countryCodeFromLocale();
    return (_regionFromCountryCode(countryCode), countryCode);
  }

  Future<String?> _probeCountryCodeFromIp() async {
    final probes = <Future<String?>>[
      _requestCountryCode(Uri.parse('https://ipapi.co/json/'), 'country_code'),
      _requestCountryCode(Uri.parse('https://ipinfo.io/json'), 'country'),
      _requestCountryCode(Uri.parse('https://api.ip.sb/geoip'), 'country_code'),
    ];

    final pending = probes.toList(growable: true);
    while (pending.isNotEmpty) {
      final wrapped = pending
          .map((future) async => (future: future, value: await future))
          .toList(growable: false);
      final settled = await Future.any(wrapped);
      pending.remove(settled.future);
      final normalized = _normalizeCountryCode(settled.value);
      if (normalized != null) {
        return normalized;
      }
    }
    return null;
  }

  Future<String?> _requestCountryCode(Uri uri, String field) async {
    try {
      final request = await _httpClient
          .getUrl(uri)
          .timeout(_geoProbeTimeout);
      request.headers.set(
        HttpHeaders.userAgentHeader,
        'PrismWave/HITS Region Probe',
      );
      request.headers.set(HttpHeaders.acceptHeader, 'application/json');
      final response = await request.close().timeout(_geoProbeTimeout);
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return null;
      }
      final decoded = jsonDecode(await utf8.decoder.bind(response).join());
      if (decoded is! Map<String, dynamic>) {
        return null;
      }
      return _normalizeCountryCode(decoded[field]?.toString());
    } catch (_) {
      return null;
    }
  }

  _HitsRoutingProfile? _decodeRoutingProfile(
    String? raw, {
    required Map<String, Map<String, dynamic>> providerStats,
  }) {
    if ((raw ?? '').trim().isEmpty) {
      return null;
    }

    try {
      final decoded = jsonDecode(raw!) as Map<String, dynamic>;
      final detectedAt = DateTime.tryParse(
        decoded['detectedAt']?.toString() ?? '',
      );
      final regionName = decoded['region']?.toString() ?? '';
      final region = _HitsSourceRegion.values.where(
        (item) => item.name == regionName,
      );
      if (detectedAt == null || region.isEmpty) {
        return null;
      }

      return _HitsRoutingProfile(
        region: region.first,
        countryCode:
            _normalizeCountryCode(decoded['countryCode']?.toString()) ?? '',
        detectedAt: detectedAt,
        providerStats: providerStats,
      );
    } catch (_) {
      return null;
    }
  }

  Map<String, Map<String, dynamic>> _decodeProviderStats(String? raw) {
    if ((raw ?? '').trim().isEmpty) {
      return const <String, Map<String, dynamic>>{};
    }

    try {
      final decoded = jsonDecode(raw!);
      if (decoded is! Map) {
        return const <String, Map<String, dynamic>>{};
      }

      final result = <String, Map<String, dynamic>>{};
      for (final entry in decoded.entries) {
        final key = entry.key.toString().trim().toLowerCase();
        final value = entry.value;
        if (key.isEmpty || value is! Map) {
          continue;
        }
        result[key] = Map<String, dynamic>.from(value.cast<String, dynamic>());
      }
      return result;
    } catch (_) {
      return const <String, Map<String, dynamic>>{};
    }
  }

  Future<void> _persistRoutingProfile(_HitsRoutingProfile profile) async {
    final prefs = await _prefs;
    await prefs.setString(
      _prefGeoPayload,
      jsonEncode(<String, dynamic>{
        'region': profile.region.name,
        'countryCode': profile.countryCode,
        'detectedAt': profile.detectedAt.toIso8601String(),
      }),
    );
    await prefs.setString(_prefProviderStats, jsonEncode(profile.providerStats));
  }

  bool _isRoutingProfileStale(_HitsRoutingProfile profile) {
    return DateTime.now().toUtc().difference(profile.detectedAt) >
        _routingProfileTtl;
  }

  String _countryCodeFromLocale() {
    final locale = Platform.localeName.trim().toUpperCase();
    if (locale.endsWith('_CN') || locale.endsWith('-CN')) return 'CN';
    if (locale.endsWith('_HK') || locale.endsWith('-HK')) return 'HK';
    if (locale.endsWith('_MO') || locale.endsWith('-MO')) return 'MO';
    if (locale.endsWith('_TW') || locale.endsWith('-TW')) return 'TW';
    return '';
  }

  _HitsSourceRegion _regionFromCountryCode(String code) {
    switch (code) {
      case 'CN':
        return _HitsSourceRegion.mainlandChina;
      case 'HK':
      case 'MO':
      case 'TW':
        return _HitsSourceRegion.greaterChina;
      default:
        return _HitsSourceRegion.global;
    }
  }

  String? _normalizeCountryCode(String? value) {
    final normalized = (value ?? '').trim().toUpperCase();
    if (normalized.length != 2) {
      return null;
    }
    return normalized;
  }

  Future<SharedPreferences> get _prefs async {
    return _preferences ??= await SharedPreferences.getInstance();
  }

  Future<T?> _firstSuccessful<T>(Iterable<Future<T?>> futures) async {
    final pending = futures.toList(growable: true);
    while (pending.isNotEmpty) {
      final wrapped = pending
          .map((future) async {
            try {
              return (future: future, value: await future);
            } catch (_) {
              return (future: future, value: null as T?);
            }
          })
          .toList(growable: false);
      final settled = await Future.any(wrapped);
      pending.remove(settled.future);
      if (settled.value != null) {
        return settled.value;
      }
    }
    return null;
  }

  int _max(int left, int right) => left >= right ? left : right;

  void dispose() {
    _httpClient.close(force: true);
    _youtube.close();
  }
}

class _AudiusCandidate {
  const _AudiusCandidate({
    required this.id,
    required this.title,
    required this.artist,
    required this.rawTitle,
    required this.uploader,
    required this.durationMs,
    required this.isStreamable,
    required this.isAvailable,
  });

  final String id;
  final String title;
  final String artist;
  final String rawTitle;
  final String uploader;
  final int durationMs;
  final bool isStreamable;
  final bool isAvailable;

  static _AudiusCandidate? fromJson(Map<String, dynamic> json) {
    final id = (json['id'] as String? ?? '').trim();
    final rawTitle = (json['title'] as String? ?? '').trim();
    final uploader = _extractUploaderName(json);
    if (id.isEmpty || rawTitle.isEmpty) {
      return null;
    }

    final inferred = _inferTitleArtist(rawTitle: rawTitle, uploader: uploader);
    if (inferred.title.isEmpty || inferred.artist.isEmpty) {
      return null;
    }

    final durationSeconds = _safeInt(json['duration']);
    return _AudiusCandidate(
      id: id,
      title: inferred.title,
      artist: inferred.artist,
      rawTitle: rawTitle,
      uploader: uploader,
      durationMs: _clampDurationMs(
        durationSeconds > 0 ? durationSeconds * 1000 : 210000,
      ),
      isStreamable: json['is_streamable'] != false,
      isAvailable: json['is_available'] != false,
    );
  }

  static String _extractUploaderName(Map<String, dynamic> json) {
    final user = json['user'];
    if (user is! Map) {
      return '';
    }
    return (user['name'] as String? ?? '').trim();
  }

  static ({String title, String artist}) _inferTitleArtist({
    required String rawTitle,
    required String uploader,
  }) {
    final title = rawTitle.trim();
    final artist = uploader.trim();
    if (!title.contains(' - ')) {
      return (title: title, artist: artist.isEmpty ? 'Unknown Artist' : artist);
    }

    final parts = title.split(' - ');
    if (parts.length < 2) {
      return (title: title, artist: artist.isEmpty ? 'Unknown Artist' : artist);
    }

    final left = parts.first.trim();
    final right = parts.skip(1).join(' - ').trim();
    final uploaderKey = _normalizeForSplit(uploader);
    final leftKey = _normalizeForSplit(left);
    var shouldSplit = false;
    if (uploaderKey.isNotEmpty &&
        (uploaderKey.contains(leftKey) || leftKey.contains(uploaderKey))) {
      shouldSplit = true;
    }
    if (left.contains(',') || left.contains('&')) {
      shouldSplit = true;
    }
    if (shouldSplit && left.isNotEmpty && right.isNotEmpty) {
      return (title: right, artist: left);
    }

    return (title: title, artist: artist.isEmpty ? 'Unknown Artist' : artist);
  }

  static String _normalizeForSplit(String value) {
    return value
        .toLowerCase()
        .replaceAll(
          RegExp(r'[^a-z0-9\u4e00-\u9fff]+', caseSensitive: false),
          ' ',
        )
        .split(RegExp(r'\s+'))
        .where((item) => item.trim().isNotEmpty)
        .join(' ');
  }

  static int _safeInt(Object? value) {
    return int.tryParse(value?.toString() ?? '') ?? 0;
  }

  static int _clampDurationMs(int value) {
    if (value < 120000) return 120000;
    if (value > 420000) return 420000;
    return value;
  }
}

class _YoutubeSearchCandidate {
  const _YoutubeSearchCandidate({
    required this.videoId,
    required this.title,
    required this.author,
    required this.durationMs,
    required this.isLive,
    required this.score,
  });

  final String videoId;
  final String title;
  final String author;
  final int durationMs;
  final bool isLive;
  final int score;

  static _YoutubeSearchCandidate? fromVideo(Video video) {
    final videoId = video.id.value.trim();
    final title = video.title.trim();
    final author = video.author.trim();
    if (videoId.isEmpty || title.isEmpty) {
      return null;
    }

    return _YoutubeSearchCandidate(
      videoId: videoId,
      title: title,
      author: author,
      durationMs: video.duration?.inMilliseconds ?? 0,
      isLive: video.isLive,
      score: 0,
    );
  }

  _YoutubeSearchCandidate copyWith({int? score}) {
    return _YoutubeSearchCandidate(
      videoId: videoId,
      title: title,
      author: author,
      durationMs: durationMs,
      isLive: isLive,
      score: score ?? this.score,
    );
  }
}

class _BilibiliCandidate {
  const _BilibiliCandidate({
    required this.bvid,
    required this.title,
    required this.author,
    required this.durationMs,
    required this.coverUrl,
    required this.score,
  });

  final String bvid;
  final String title;
  final String author;
  final int durationMs;
  final String coverUrl;
  final int score;

  static _BilibiliCandidate? fromJson(Map<String, dynamic> json) {
    final bvid = (json['bvid'] as String? ?? '').trim();
    final title = (json['title'] as String? ?? '')
        .replaceAll(RegExp(r'<[^>]+>'), ' ')
        .replaceAll(RegExp(r'\s+'), ' ')
        .trim();
    if (bvid.isEmpty || title.isEmpty) {
      return null;
    }

    final pic = (json['pic'] as String? ?? '').trim();
    final durationMs = _parseDuration(json['duration']?.toString() ?? '');
    return _BilibiliCandidate(
      bvid: bvid,
      title: title,
      author: (json['author'] as String? ?? '').trim(),
      durationMs: durationMs,
      coverUrl: pic.startsWith('//') ? 'https:$pic' : pic,
      score: 0,
    );
  }

  _BilibiliCandidate copyWith({int? score}) {
    return _BilibiliCandidate(
      bvid: bvid,
      title: title,
      author: author,
      durationMs: durationMs,
      coverUrl: coverUrl,
      score: score ?? this.score,
    );
  }

  static int _parseDuration(String value) {
    final parts = value
        .split(':')
        .map((item) => int.tryParse(item.trim()) ?? 0)
        .toList(growable: false);
    if (parts.length == 2) {
      return ((parts[0] * 60) + parts[1]) * 1000;
    }
    if (parts.length == 3) {
      return ((parts[0] * 3600) + (parts[1] * 60) + parts[2]) * 1000;
    }
    return parts.isEmpty ? 0 : parts.first * 1000;
  }
}

class _KuwoCandidate {
  const _KuwoCandidate({
    required this.mid,
    required this.title,
    required this.artist,
    required this.durationMs,
    required this.score,
  });

  final String mid;
  final String title;
  final String artist;
  final int durationMs;
  final int score;

  static _KuwoCandidate? fromJson(Map<String, dynamic> json) {
    final mid = (json['rid'] as num?)?.toInt();
    final title = (json['name'] as String? ?? '').trim();
    if (mid == null || title.isEmpty) {
      return null;
    }

    final durationSec = (json['duration'] as num?)?.toInt() ?? 0;
    return _KuwoCandidate(
      mid: '$mid',
      title: title,
      artist: (json['artist'] as String? ?? '').trim(),
      durationMs: durationSec > 0 ? durationSec * 1000 : 210000,
      score: 0,
    );
  }

  _KuwoCandidate copyWith({int? score}) {
    return _KuwoCandidate(
      mid: mid,
      title: title,
      artist: artist,
      durationMs: durationMs,
      score: score ?? this.score,
    );
  }
}

class _MiguCandidate {
  const _MiguCandidate({
    required this.copyrightId,
    required this.title,
    required this.artist,
    required this.durationMs,
    this.coverUrl,
    required this.score,
  });

  final String copyrightId;
  final String title;
  final String artist;
  final int durationMs;
  final String? coverUrl;
  final int score;

  static _MiguCandidate? fromJson(Map<String, dynamic> json) {
    final copyrightId = (json['copyrightId'] as String? ?? json['id'] as String? ?? '').trim();
    final title = (json['songName'] as String? ?? json['title'] as String? ?? '').trim();
    if (copyrightId.isEmpty || title.isEmpty) {
      return null;
    }

    final durationSec = _miguParseDuration(json['duration']?.toString() ?? '');
    final coverUrl = (json['cover'] as String? ?? json['picUrl'] as String?)?.trim() ?? '';

    return _MiguCandidate(
      copyrightId: copyrightId,
      title: title,
      artist: (json['singerName'] as String? ?? json['artist'] as String? ?? '').trim(),
      durationMs: durationSec > 0 ? durationSec * 1000 : 210000,
      coverUrl: coverUrl.isNotEmpty ? coverUrl : null,
      score: 0,
    );
  }

  _MiguCandidate copyWith({int? score}) {
    return _MiguCandidate(
      copyrightId: copyrightId,
      title: title,
      artist: artist,
      durationMs: durationMs,
      coverUrl: coverUrl,
      score: score ?? this.score,
    );
  }

  static int _miguParseDuration(String value) {
    final parts = value
        .split(':')
        .map((item) => int.tryParse(item.trim()) ?? 0)
        .toList(growable: false);
    if (parts.length == 2) {
      return (parts[0] * 60) + parts[1];
    }
    return int.tryParse(value) ?? 0;
  }
}

class _QQCandidate {
  const _QQCandidate({
    required this.songMid,
    required this.title,
    required this.artist,
    required this.durationMs,
    this.coverUrl,
    required this.score,
  });

  final String songMid;
  final String title;
  final String artist;
  final int durationMs;
  final String? coverUrl;
  final int score;

  static _QQCandidate? fromJson(Map<String, dynamic> json) {
    final songMid = (json['mid'] as String? ?? '').trim();
    final title = (json['title'] as String? ?? json['name'] as String? ?? '').trim();
    if (songMid.isEmpty || title.isEmpty) {
      return null;
    }

    final singers = json['singer'] as List?;
    final artist = singers is List && singers.isNotEmpty
        ? ((singers.first as Map?)?.cast<String, dynamic>()['name'] as String? ?? '').trim()
        : '';

    final intervalSec = (json['interval'] as num?)?.toInt() ?? 0;

    final album = json['album'] as Map?;
    final coverUrl = (album?.cast<String, dynamic>()['picUrl'] as String?)?.trim() ?? '';

    return _QQCandidate(
      songMid: songMid,
      title: title,
      artist: artist,
      durationMs: intervalSec > 0 ? intervalSec * 1000 : 210000,
      coverUrl: coverUrl.isNotEmpty ? coverUrl : null,
      score: 0,
    );
  }

  _QQCandidate copyWith({int? score}) {
    return _QQCandidate(
      songMid: songMid,
      title: title,
      artist: artist,
      durationMs: durationMs,
      coverUrl: coverUrl,
      score: score ?? this.score,
    );
  }
}

class _KugouCandidate {
  const _KugouCandidate({
    required this.fileHash,
    required this.title,
    required this.artist,
    required this.durationMs,
    this.coverUrl,
    required this.score,
  });

  final String fileHash;
  final String title;
  final String artist;
  final int durationMs;
  final String? coverUrl;
  final int score;

  static _KugouCandidate? fromJson(Map<String, dynamic> json) {
    final fileHash = (json['hash'] as String? ?? '').trim();
    final title = (json['filename'] as String? ?? json['songname'] as String? ?? '').trim();
    if (fileHash.isEmpty || title.isEmpty) {
      return null;
    }

    final durationSec = (json['duration'] as num?)?.toInt() ?? 0;
    final coverUrl = (json['imgUrl'] as String?)?.trim() ?? '';

    return _KugouCandidate(
      fileHash: fileHash,
      title: title,
      artist: (json['singername'] as String? ?? '').trim(),
      durationMs: durationSec > 0 ? durationSec * 1000 : 210000,
      coverUrl: coverUrl.isNotEmpty ? coverUrl : null,
      score: 0,
    );
  }

  _KugouCandidate copyWith({int? score}) {
    return _KugouCandidate(
      fileHash: fileHash,
      title: title,
      artist: artist,
      durationMs: durationMs,
      coverUrl: coverUrl,
      score: score ?? this.score,
    );
  }
}

class _NeteaseCandidate {
  const _NeteaseCandidate({
    required this.songId,
    required this.title,
    required this.artist,
    required this.durationMs,
    this.coverUrl,
    required this.score,
  });

  final int songId;
  final String title;
  final String artist;
  final int durationMs;
  final String? coverUrl;
  final int score;

  static _NeteaseCandidate? fromJson(Map<String, dynamic> json) {
    final songId = (json['id'] as num?)?.toInt() ?? 0;
    final title = (json['name'] as String? ?? '').trim();
    if (songId <= 0 || title.isEmpty) {
      return null;
    }

    final artists = json['ar'] as List?;
    final artist = artists is List && artists.isNotEmpty
        ? ((artists.first as Map?)?.cast<String, dynamic>()['name'] as String? ?? '').trim()
        : '';

    final album = json['al'] as Map?;
    final coverUrl = (album?.cast<String, dynamic>()['picUrl'] as String?)?.trim() ?? '';

    final durationMs = (json['dt'] as num?)?.toInt() ?? 0;

    return _NeteaseCandidate(
      songId: songId,
      title: title,
      artist: artist,
      durationMs: durationMs,
      coverUrl: coverUrl.isNotEmpty ? coverUrl : null,
      score: 0,
    );
  }

  _NeteaseCandidate copyWith({int? score}) {
    return _NeteaseCandidate(
      songId: songId,
      title: title,
      artist: artist,
      durationMs: durationMs,
      coverUrl: coverUrl,
      score: score ?? this.score,
    );
  }
}
