import 'dart:async';
import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/hits_manifest.dart';
import '../models/lyrics_document.dart';
import '../models/track.dart';
import '../services/hits_audio_resolver_service.dart';
import '../services/hits_media_cache_service.dart';
import '../services/hits_manifest_service.dart';
import '../services/hits_scheduler.dart';
import '../services/online_cover_service.dart';
import '../services/online_lyrics_service.dart';
import '../state/hits_state.dart';
import '../state/library_state.dart';
import '../state/playback_state.dart';
import 'playback_controller.dart';

typedef HitsLibraryStateReader = LibraryState Function();
typedef HitsPlaybackStateReader = PlaybackState Function();

class HitsController extends StateNotifier<HitsState> {
  HitsController({
    required HitsLibraryStateReader readLibraryState,
    required HitsPlaybackStateReader readPlaybackState,
    required PlaybackController playbackController,
    HitsManifestService? manifestService,
    HitsScheduler? scheduler,
    HitsAudioResolverService? audioResolverService,
    HitsMediaCacheService? mediaCacheService,
  }) : _readLibraryState = readLibraryState,
       _readPlaybackState = readPlaybackState,
       _playbackController = playbackController,
       _manifestService = manifestService ?? HitsManifestService(),
       _scheduler = scheduler ?? const HitsScheduler(),
       _audioResolverService =
           audioResolverService ?? HitsAudioResolverService(),
       _mediaCacheService = mediaCacheService ?? HitsMediaCacheService(),
       _onlineCoverService = OnlineCoverService(),
       _onlineLyricsService = OnlineLyricsService(),
       super(const HitsState());

  final HitsLibraryStateReader _readLibraryState;
  final HitsPlaybackStateReader _readPlaybackState;
  final PlaybackController _playbackController;
  final HitsManifestService _manifestService;
  final HitsScheduler _scheduler;
  final HitsAudioResolverService _audioResolverService;
  final HitsMediaCacheService _mediaCacheService;
  final OnlineCoverService _onlineCoverService;
  final OnlineLyricsService _onlineLyricsService;

  Timer? _ticker;
  bool _initialized = false;
  bool _syncInFlight = false;
  bool _restoringSession = false;
  bool _disposed = false;
  DateTime? _lastRefreshAttemptAt;
  String? _lastResolvedTrackId;
  String? _lastOnlineLyricsTrackId;
  String? _lastPlaybackResolutionTrackId;
  String? _lastCoverTrackId;
  String? _lastPrefetchLeadTrackId;
  DateTime? _lastPlaybackStartedAt;
  final Map<String, DateTime> _recentlyFailedTrackIds = {};
  int _onlineLyricsRequestToken = 0;
  int _playbackResolutionRequestToken = 0;
  int _coverRequestToken = 0;
  PlaybackSessionSnapshot? _playbackSnapshot;

  Future<void> initialize() async {
    if (_initialized) {
      _applySchedulePosition(nowUtc: DateTime.now().toUtc());
      unawaited(
        _syncPlaybackToSchedule(
          nowUtc: DateTime.now().toUtc(),
          forceReload: false,
        ),
      );
      return;
    }

    _initialized = true;
    await _capturePlaybackSnapshot();
    await _enterHitsSession();
    unawaited(_audioResolverService.warmUp());
    _ticker = Timer.periodic(const Duration(seconds: 1), (_) => _handleTick());
    await refresh();
  }

  Future<void> refresh() async {
    final preserveData = state.schedule != null;
    state = state.copyWith(
      status: preserveData ? state.status : HitsStatus.loading,
      isRefreshing: true,
      currentUtcTime: DateTime.now().toUtc(),
      isSessionActive: true,
      isPlaying: _isHitsPlaybackActive(),
      clearError: true,
    );

    try {
      final bundle = await _manifestService.loadBestAvailable();
      if (_disposed) return;
      _lastRefreshAttemptAt = DateTime.now().toUtc();
      await _applyBundle(bundle);

      if (bundle.usedCache) {
        unawaited(_refreshManifestInBackground());
      }
    } on HitsManifestException catch (error) {
      if (_disposed) return;
      state = state.copyWith(
        status: _statusFromManifestError(error.kind),
        currentUtcTime: DateTime.now().toUtc(),
        isSessionActive: true,
        isPlaying: _isHitsPlaybackActive(),
        isRefreshing: false,
        error: error.message,
      );
      await _syncPlaybackToSchedule(
        nowUtc: DateTime.now().toUtc(),
        forceReload: false,
      );
    } catch (error) {
      if (_disposed) return;
      state = state.copyWith(
        status: HitsStatus.unavailable,
        currentUtcTime: DateTime.now().toUtc(),
        isSessionActive: true,
        isPlaying: _isHitsPlaybackActive(),
        isRefreshing: false,
        error: error.toString(),
      );
      await _syncPlaybackToSchedule(
        nowUtc: DateTime.now().toUtc(),
        forceReload: false,
      );
    }
  }

  Future<void> _applyBundle(HitsManifestBundle bundle) async {
    final nowUtc = DateTime.now().toUtc();
    final position = _scheduler.resolve(
      schedule: bundle.schedule,
      nowUtc: nowUtc,
    );
    final matchedTrack = _matchLibraryTrack(position.currentTrack);
    final resolvedPlaybackTrack = _resolveImmediatePlaybackTrack(
      scheduleTrack: position.currentTrack,
      matchedTrack: matchedTrack,
    );

    _lastResolvedTrackId = position.currentTrack?.stationTrackId;
    state = state.copyWith(
      latestManifest: bundle.latestManifest,
      schedule: bundle.schedule,
      position: position,
      matchedLibraryTrack: matchedTrack,
      resolvedPlaybackTrack: resolvedPlaybackTrack,
      isResolvingPlaybackSource: false,
      currentCoverBytes: null,
      usingCachedSchedule: bundle.usedCache,
      currentUtcTime: nowUtc,
      status: _statusFromPosition(position),
      isSessionActive: true,
      isPlaying: _isHitsPlaybackActive(),
      isRefreshing: false,
      clearError: true,
    );
    _lastOnlineLyricsTrackId = null;
    _lastCoverTrackId = null;
    _refreshPlaybackSource(
      scheduleTrack: position.currentTrack,
      matchedTrack: matchedTrack,
      force: true,
    );
    _refreshOnlineLyrics(
      scheduleTrack: position.currentTrack,
    );
    _refreshCover(scheduleTrack: position.currentTrack);
    _prefetchUpcomingAssets(leadTrack: position.currentTrack);
    await _syncPlaybackToSchedule(nowUtc: nowUtc, forceReload: true);
  }

  Future<void> _refreshManifestInBackground() async {
    try {
      final freshBundle = await _manifestService.loadActiveBundle();
      if (!freshBundle.usedCache) {
        await _applyBundle(freshBundle);
      }
    } catch (_) {
      // keep using cached data
    }
  }

  Future<void> togglePlayback() async {
    if (!state.canTogglePlayback) return;

    if (state.isPlaying) {
      await _playbackController.togglePlayPause();
      state = state.copyWith(
        isSessionActive: true,
        isPlaying: false,
        userPaused: true,
        clearError: true,
      );
      return;
    }

    state = state.copyWith(
      isSessionActive: true,
      userPaused: false,
      clearError: true,
    );
    await _syncPlaybackToSchedule(
      nowUtc: DateTime.now().toUtc(),
      forceReload: true,
    );
  }

  void _handleTick() {
    final nowUtc = DateTime.now().toUtc();
    if (_shouldRefreshForDateBoundary(nowUtc)) {
      unawaited(refresh());
      return;
    }
    _applySchedulePosition(nowUtc: nowUtc);
    unawaited(_syncPlaybackToSchedule(nowUtc: nowUtc, forceReload: false));
  }

  bool _shouldRefreshForDateBoundary(DateTime nowUtc) {
    final schedule = state.schedule;
    if (schedule == null || state.isRefreshing) return false;

    final currentDate = _isoDate(nowUtc);
    if (schedule.editionDate == currentDate) {
      return false;
    }

    final lastAttempt = _lastRefreshAttemptAt;
    if (lastAttempt == null) return true;
    return nowUtc.difference(lastAttempt) >= const Duration(minutes: 1);
  }

  void _applySchedulePosition({required DateTime nowUtc}) {
    final schedule = state.schedule;
    if (schedule == null) {
      state = state.copyWith(
        currentUtcTime: nowUtc,
        isSessionActive: true,
        isPlaying: _isHitsPlaybackActive(),
      );
      return;
    }

    final position = _scheduler.resolve(schedule: schedule, nowUtc: nowUtc);
    final previousTrackId = state.currentScheduleTrack?.stationTrackId;
    final currentTrackId = position.currentTrack?.stationTrackId;
    final shouldRematchTrack =
        currentTrackId != _lastResolvedTrackId ||
        (position.currentTrack != null && state.matchedLibraryTrack == null);
    final matchedTrack = shouldRematchTrack
        ? _matchLibraryTrack(position.currentTrack)
        : state.matchedLibraryTrack;
    final directResolvedPlaybackTrack = _resolveImmediatePlaybackTrack(
      scheduleTrack: position.currentTrack,
      matchedTrack: matchedTrack,
    );
    final resolvedPlaybackTrack =
        directResolvedPlaybackTrack ??
        (currentTrackId != null && currentTrackId == previousTrackId
            ? state.resolvedPlaybackTrack
            : null);
    final isResolvingPlaybackSource = directResolvedPlaybackTrack != null
        ? false
        : (currentTrackId != null && currentTrackId == previousTrackId
              ? state.isResolvingPlaybackSource
              : false);

    _lastResolvedTrackId = currentTrackId;
    state = state.copyWith(
      position: position,
      matchedLibraryTrack: matchedTrack,
      resolvedPlaybackTrack: resolvedPlaybackTrack,
      isResolvingPlaybackSource: isResolvingPlaybackSource,
      currentUtcTime: nowUtc,
      status: _statusFromPosition(position),
      isSessionActive: true,
      isPlaying: _isHitsPlaybackActive(),
      clearError: true,
    );
    _refreshPlaybackSource(
      scheduleTrack: position.currentTrack,
      matchedTrack: matchedTrack,
      force: false,
    );
    _refreshOnlineLyrics(
      scheduleTrack: position.currentTrack,
    );
    _refreshCover(scheduleTrack: position.currentTrack);
    if (currentTrackId != previousTrackId) {
      _prefetchUpcomingAssets(leadTrack: position.currentTrack);
    }
  }

  Future<void> _capturePlaybackSnapshot() async {
    _playbackSnapshot ??= _playbackController.captureSessionSnapshot();
  }

  void _refreshOnlineLyrics({
    required HitsScheduleTrack? scheduleTrack,
  }) {
    if (scheduleTrack == null) {
      _lastOnlineLyricsTrackId = null;
      _onlineLyricsRequestToken += 1;
      if (state.onlineLyricsDocument != null || state.isOnlineLyricsLoading) {
        state = state.copyWith(
          onlineLyricsDocument: null,
          isOnlineLyricsLoading: false,
        );
      }
      return;
    }

    final trackId = scheduleTrack.stationTrackId;
    if (_lastOnlineLyricsTrackId == trackId) {
      return;
    }

    _lastOnlineLyricsTrackId = trackId;
    final requestToken = ++_onlineLyricsRequestToken;
    state = state.copyWith(
      onlineLyricsDocument: null,
      isOnlineLyricsLoading: true,
    );
    unawaited(
      _loadOnlineLyricsForScheduleTrack(
        scheduleTrack: scheduleTrack,
        requestToken: requestToken,
      ),
    );
  }

  Future<void> _loadOnlineLyricsForScheduleTrack({
    required HitsScheduleTrack scheduleTrack,
    required int requestToken,
  }) async {
    final document = await _resolveOnlineLyricsDocumentForScheduleTrack(
      scheduleTrack,
    );

    if (_disposed) return;
    if (requestToken != _onlineLyricsRequestToken) return;
    if (state.currentScheduleTrack?.stationTrackId !=
        scheduleTrack.stationTrackId) {
      return;
    }

    state = state.copyWith(
      onlineLyricsDocument: document,
      isOnlineLyricsLoading: false,
    );
  }

  Future<LyricsDocument?> _resolveOnlineLyricsDocumentForScheduleTrack(
    HitsScheduleTrack scheduleTrack,
  ) async {
    final pseudoTrack = _pseudoTrackForScheduleTrack(scheduleTrack);
    final durationHint = _scheduleTrackDurationHint(scheduleTrack);
    final query = _scheduleTrackLyricsQuery(scheduleTrack);

    LyricsDocument? document = await _onlineLyricsService
        .loadCachedLyricsForTrack(pseudoTrack, durationHint: durationHint);

    document ??= await _onlineLyricsService.resolveBestLyricsDocumentForTrack(
      pseudoTrack,
      query: query,
      durationHint: durationHint,
    );

    if (document != null && !document.isEmpty) {
      await _onlineLyricsService.saveCachedLyricsForTrack(
        pseudoTrack,
        document,
      );
    }

    return document;
  }

  Track _pseudoTrackForScheduleTrack(HitsScheduleTrack scheduleTrack) {
    return Track(
      path: 'hits://${scheduleTrack.stationTrackId}',
      title: scheduleTrack.title,
      artist: scheduleTrack.artist,
      album: scheduleTrack.album,
    );
  }

  Duration? _scheduleTrackDurationHint(HitsScheduleTrack scheduleTrack) {
    return scheduleTrack.duration > Duration.zero ? scheduleTrack.duration : null;
  }

  String _scheduleTrackLyricsQuery(HitsScheduleTrack scheduleTrack) {
    return scheduleTrack.searchQuery.trim().isNotEmpty
        ? scheduleTrack.searchQuery.trim()
        : '${scheduleTrack.title} ${scheduleTrack.artist}'.trim();
  }

  String _scheduleTrackCoverQuery(HitsScheduleTrack scheduleTrack) {
    final base = scheduleTrack.searchQuery.trim().isNotEmpty
        ? scheduleTrack.searchQuery.trim()
        : '${scheduleTrack.title} ${scheduleTrack.artist}'.trim();
    if (scheduleTrack.album.trim().isEmpty) {
      return base;
    }
    return '$base ${scheduleTrack.album}'.trim();
  }

  void _refreshCover({required HitsScheduleTrack? scheduleTrack}) {
    if (scheduleTrack == null) {
      _lastCoverTrackId = null;
      _coverRequestToken += 1;
      state = state.copyWith(
        currentCoverBytes: null,
        isCoverLoading: false,
      );
      return;
    }

    if (_lastCoverTrackId == scheduleTrack.stationTrackId) {
      return;
    }

    _lastCoverTrackId = scheduleTrack.stationTrackId;
    final requestToken = ++_coverRequestToken;
    state = state.copyWith(currentCoverBytes: null, isCoverLoading: true);
    unawaited(
      _loadCoverForScheduleTrack(
        scheduleTrack: scheduleTrack,
        requestToken: requestToken,
      ),
    );
  }

  Future<void> _loadCoverForScheduleTrack({
    required HitsScheduleTrack scheduleTrack,
    required int requestToken,
  }) async {
    final bytes = await _loadBestCoverBytes(scheduleTrack: scheduleTrack);

    if (_disposed) return;
    if (requestToken != _coverRequestToken) return;
    if (state.currentScheduleTrack?.stationTrackId !=
        scheduleTrack.stationTrackId) {
      return;
    }

    state = state.copyWith(
      currentCoverBytes: bytes,
      isCoverLoading: false,
    );
  }

  void _refreshPlaybackSource({
    required HitsScheduleTrack? scheduleTrack,
    required Track? matchedTrack,
    required bool force,
    }) {
    final directTrack = _resolveImmediatePlaybackTrack(
      scheduleTrack: scheduleTrack,
      matchedTrack: matchedTrack,
    );
    final immediateSource = _resolveImmediatePlaybackSource(scheduleTrack);

    if (scheduleTrack == null) {
      _lastPlaybackResolutionTrackId = null;
      _playbackResolutionRequestToken += 1;
      if (state.resolvedPlaybackTrack != directTrack ||
          state.isResolvingPlaybackSource) {
        state = state.copyWith(
          resolvedPlaybackTrack: directTrack,
          isResolvingPlaybackSource: false,
        );
      }
      return;
    }

    if (directTrack != null) {
      _lastPlaybackResolutionTrackId = scheduleTrack.stationTrackId;
      final requestToken = ++_playbackResolutionRequestToken;
      if (state.resolvedPlaybackTrack != directTrack ||
          state.isResolvingPlaybackSource) {
        state = state.copyWith(
          resolvedPlaybackTrack: directTrack,
          isResolvingPlaybackSource: false,
        );
      }
      if (immediateSource != null) {
        unawaited(
          _hydrateDirectPlaybackSource(
            scheduleTrack: scheduleTrack,
            source: immediateSource,
            requestToken: requestToken,
          ),
        );
      }
      return;
    }

    if (!force &&
        _lastPlaybackResolutionTrackId == scheduleTrack.stationTrackId &&
        (state.resolvedPlaybackTrack != null ||
            state.isResolvingPlaybackSource)) {
      return;
    }

    _lastPlaybackResolutionTrackId = scheduleTrack.stationTrackId;
    final requestToken = ++_playbackResolutionRequestToken;
    state = state.copyWith(
      resolvedPlaybackTrack: null,
      isResolvingPlaybackSource: true,
    );
    unawaited(
      _loadOnlinePlaybackSourceForScheduleTrack(
        scheduleTrack: scheduleTrack,
        requestToken: requestToken,
      ),
    );
  }

  Future<void> _loadOnlinePlaybackSourceForScheduleTrack({
    required HitsScheduleTrack scheduleTrack,
    required int requestToken,
  }) async {
    final resolved = await _audioResolverService.resolveTrack(scheduleTrack);

    if (_disposed) return;
    if (requestToken != _playbackResolutionRequestToken) return;
    if (state.currentScheduleTrack?.stationTrackId !=
        scheduleTrack.stationTrackId) {
      return;
    }

    final cachedPlaybackUrl = resolved == null
        ? null
        : await _mediaCacheService.cachedAudioPlaybackUrl(
            track: scheduleTrack,
            source: resolved,
          );

    if (_disposed) return;
    final resolvedTrack = resolved == null
        ? null
        : _buildHitsPlaybackTrack(
            scheduleTrack: scheduleTrack,
            playbackUrl: cachedPlaybackUrl ?? resolved.playbackUrl,
            playbackHeaders: cachedPlaybackUrl == null
                ? resolved.playbackHeaders
                : null,
          );

    state = state.copyWith(
      resolvedPlaybackTrack: resolvedTrack,
      isResolvingPlaybackSource: false,
    );

    if (resolved != null) {
      unawaited(
        _startTrackAssetPrefetch(scheduleTrack: scheduleTrack, source: resolved),
      );
    }

    await _syncPlaybackToSchedule(
      nowUtc: DateTime.now().toUtc(),
      forceReload: resolved != null,
    );
  }

  Future<void> _hydrateDirectPlaybackSource({
    required HitsScheduleTrack scheduleTrack,
    required HitsResolvedAudioSource source,
    required int requestToken,
  }) async {
    final cachedPlaybackUrl = await _mediaCacheService.cachedAudioPlaybackUrl(
      track: scheduleTrack,
      source: source,
    );

    if (_disposed) return;
    if (requestToken != _playbackResolutionRequestToken) return;
    if (state.currentScheduleTrack?.stationTrackId !=
        scheduleTrack.stationTrackId) {
      return;
    }

    if (cachedPlaybackUrl != null &&
        state.resolvedPlaybackTrack?.playbackUrl != cachedPlaybackUrl) {
      state = state.copyWith(
        resolvedPlaybackTrack: _buildHitsPlaybackTrack(
          scheduleTrack: scheduleTrack,
          playbackUrl: cachedPlaybackUrl,
          playbackHeaders: null,
        ),
      );

      final playback = _readPlaybackState();
      if (!playback.isPlaying) {
        await _syncPlaybackToSchedule(
          nowUtc: DateTime.now().toUtc(),
          forceReload: true,
        );
      }
    }

    unawaited(_startTrackAssetPrefetch(scheduleTrack: scheduleTrack, source: source));
  }

  Future<void> _startTrackAssetPrefetch({
    required HitsScheduleTrack scheduleTrack,
    required HitsResolvedAudioSource source,
  }) async {
    final playbackUrl = await _mediaCacheService.prefetchAudio(
      track: scheduleTrack,
      source: source,
    );
    if (playbackUrl != null) {
      _playbackController.appendDeveloperLog(
        'hits.cache.audio -> ${scheduleTrack.stationTrackId}',
      );
    }
  }

  Future<Uint8List?> _loadBestCoverBytes({
    required HitsScheduleTrack scheduleTrack,
    HitsResolvedAudioSource? source,
  }) async {
    final coverUrl = scheduleTrack.coverUrl?.toString().trim() ?? '';
    if (coverUrl.isNotEmpty && !_looksLikePlaceholderCover(coverUrl)) {
      final directBytes = await _mediaCacheService.loadCoverBytes(scheduleTrack);
      if (directBytes != null && directBytes.isNotEmpty) {
        return directBytes;
      }
    }

    final effectiveSource =
        source ??
        _resolveImmediatePlaybackSource(scheduleTrack) ??
        await _audioResolverService.resolveTrack(scheduleTrack);
    if (effectiveSource != null) {
      final providerBytes = await _loadResolvedCoverFallbackBytes(
        scheduleTrack: scheduleTrack,
        source: effectiveSource,
      );
      if (providerBytes != null && providerBytes.isNotEmpty) {
        return providerBytes;
      }
    }

    return _loadSearchedCoverFallbackBytes(scheduleTrack);
  }

  Future<Uint8List?> _loadResolvedCoverFallbackBytes({
    required HitsScheduleTrack scheduleTrack,
    required HitsResolvedAudioSource source,
  }) async {
    final provider = source.provider?.trim().toLowerCase() ?? '';
    final providerTrackId = source.providerTrackId?.trim() ?? '';
    final resolvedCoverUrl = source.coverUrl?.trim() ?? '';

    const videoPlatformSkip = <String>{'bilibili', 'bilivideo'};
    if (resolvedCoverUrl.isNotEmpty && !videoPlatformSkip.contains(provider)) {
      final resolvedBytes = await _mediaCacheService.loadCoverBytesFromUrl(
        cacheKey: scheduleTrack.stationTrackId,
        coverUrl: resolvedCoverUrl,
      );
      if (resolvedBytes != null && resolvedBytes.isNotEmpty) {
        return resolvedBytes;
      }
    }
    if (provider != 'youtube' || providerTrackId.isEmpty) {
      return null;
    }

    final fallbackCandidates = <String>[
      'https://i.ytimg.com/vi/$providerTrackId/maxresdefault.jpg',
      'https://i.ytimg.com/vi/$providerTrackId/sddefault.jpg',
      'https://i.ytimg.com/vi/$providerTrackId/hq720.jpg',
      'https://i.ytimg.com/vi/$providerTrackId/mqdefault.jpg',
      'https://i.ytimg.com/vi/$providerTrackId/hqdefault.jpg',
      'https://i.ytimg.com/vi/$providerTrackId/default.jpg',
      'https://i.ytimg.com/vi_webp/$providerTrackId/maxresdefault.webp',
      'https://i.ytimg.com/vi_webp/$providerTrackId/sddefault.webp',
      'https://i.ytimg.com/vi_webp/$providerTrackId/hqdefault.webp',
      'https://img.youtube.com/vi/$providerTrackId/maxresdefault.jpg',
      'https://img.youtube.com/vi/$providerTrackId/hqdefault.jpg',
    ];

    for (final coverUrl in fallbackCandidates) {
      final bytes = await _mediaCacheService.loadCoverBytesFromUrl(
        cacheKey: scheduleTrack.stationTrackId,
        coverUrl: coverUrl,
      );
      if (bytes != null && bytes.isNotEmpty) {
        return bytes;
      }
    }

    return null;
  }

  Future<Uint8List?> _loadSearchedCoverFallbackBytes(
    HitsScheduleTrack scheduleTrack,
  ) async {
    final pseudoTrack = _pseudoTrackForScheduleTrack(scheduleTrack);
    final query = _scheduleTrackCoverQuery(scheduleTrack);
    final results = await _onlineCoverService.searchCoversForTrack(
      pseudoTrack,
      query: query,
    );

    for (final result in results.take(6)) {
      final coverUrls = <String>[
        result.fullImageUrl.trim(),
        result.thumbnailUrl.trim(),
      ].where((item) => item.isNotEmpty).toList(growable: false);

      for (final coverUrl in coverUrls) {
        final bytes = await _mediaCacheService.loadCoverBytesFromUrl(
          cacheKey: scheduleTrack.stationTrackId,
          coverUrl: coverUrl,
        );
        if (bytes != null && bytes.isNotEmpty) {
          _playbackController.appendDeveloperLog(
            'hits.cover.search -> ${scheduleTrack.stationTrackId} (${result.source})',
          );
          return bytes;
        }
      }
    }

    return null;
  }

  void _prefetchUpcomingAssets({required HitsScheduleTrack? leadTrack}) {
    final schedule = state.schedule;
    if (schedule == null || leadTrack == null) {
      _lastPrefetchLeadTrackId = null;
      return;
    }

    if (_lastPrefetchLeadTrackId == leadTrack.stationTrackId) {
      return;
    }
    _lastPrefetchLeadTrackId = leadTrack.stationTrackId;

    final leadIndex = schedule.tracks.indexWhere(
      (track) => track.stationTrackId == leadTrack.stationTrackId,
    );
    if (leadIndex < 0) {
      return;
    }

    final prefetchTracks = schedule.tracks
        .skip(leadIndex)
        .take(3)
        .toList(growable: false);

    for (final track in prefetchTracks) {
      unawaited(_prefetchTrackAssets(track));
    }
  }

  Future<void> _prefetchTrackAssets(HitsScheduleTrack scheduleTrack) async {
    unawaited(_prefetchTrackCover(scheduleTrack));
    unawaited(_prefetchTrackLyrics(scheduleTrack));

    final immediateSource = _resolveImmediatePlaybackSource(scheduleTrack);
    final source =
        immediateSource ?? await _audioResolverService.resolveTrack(scheduleTrack);
    if (source == null) {
      return;
    }

    final playbackUrl = await _mediaCacheService.prefetchAudio(
      track: scheduleTrack,
      source: source,
    );
    if (playbackUrl != null) {
      _playbackController.appendDeveloperLog(
        'hits.prefetch.ready -> ${scheduleTrack.stationTrackId}',
      );
    }
  }

  Future<void> _prefetchTrackCover(
    HitsScheduleTrack scheduleTrack, {
    HitsResolvedAudioSource? source,
  }) async {
    final bytes = await _loadBestCoverBytes(
      scheduleTrack: scheduleTrack,
      source: source,
    );
    if (bytes != null && bytes.isNotEmpty) {
      _playbackController.appendDeveloperLog(
        'hits.cache.cover -> ${scheduleTrack.stationTrackId}',
      );
    }
  }

  Future<void> _prefetchTrackLyrics(HitsScheduleTrack scheduleTrack) async {
    final document = await _resolveOnlineLyricsDocumentForScheduleTrack(
      scheduleTrack,
    );
    if (document != null && !document.isEmpty) {
      _playbackController.appendDeveloperLog(
        'hits.cache.lyrics -> ${scheduleTrack.stationTrackId}',
      );
    }
  }

  HitsResolvedAudioSource? _resolveImmediatePlaybackSource(
    HitsScheduleTrack? scheduleTrack,
  ) {
    if (scheduleTrack == null) return null;

    final remoteUrl = scheduleTrack.audioUrl?.toString().trim() ?? '';
    if (remoteUrl.isNotEmpty) {
      return HitsResolvedAudioSource(
        playbackUrl: remoteUrl,
        provider: scheduleTrack.audioProvider.trim().isEmpty
            ? null
            : scheduleTrack.audioProvider,
        providerTrackId: scheduleTrack.providerTrackId.trim().isEmpty
            ? null
            : scheduleTrack.providerTrackId,
      );
    }

    final provider = scheduleTrack.audioProvider.trim().toLowerCase();
    final providerTrackId = scheduleTrack.providerTrackId.trim();
    if (provider == 'audius' && providerTrackId.isNotEmpty) {
      return HitsResolvedAudioSource(
        playbackUrl:
            'https://api.audius.co/v1/tracks/${Uri.encodeComponent(providerTrackId)}/stream',
        provider: provider,
        providerTrackId: providerTrackId,
        suggestedFileExtension: '.mp3',
      );
    }

    return null;
  }

  Track _buildHitsPlaybackTrack({
    required HitsScheduleTrack scheduleTrack,
    required String? playbackUrl,
    Map<String, String>? playbackHeaders,
  }) {
    return Track(
      path: 'hits://${scheduleTrack.stationTrackId}',
      title: scheduleTrack.title,
      artist: scheduleTrack.artist,
      album: scheduleTrack.album,
      playbackUrl: playbackUrl,
      playbackHeaders: playbackHeaders,
    );
  }

  bool _looksLikePlaceholderCover(String coverUrl) {
    final normalized = coverUrl.toLowerCase();
    return normalized.contains('2a96cbd8b46e442fc41c2b86b821562f') ||
        normalized.contains('/noimage/');
  }

  Future<void> _enterHitsSession() async {
    await _playbackController.stopAndClear();
    state = state.copyWith(
      isSessionActive: true,
      isPlaying: false,
      userPaused: false,
      clearError: true,
    );
  }

  Future<void> _syncPlaybackToSchedule({
    required DateTime nowUtc,
    required bool forceReload,
  }) async {
    if (_disposed || _syncInFlight || _restoringSession || !state.isSessionActive) {
      _syncPlaybackFlagsInState();
      return;
    }

    _syncInFlight = true;
    try {
      final scheduleTrack = state.currentScheduleTrack;
      final playbackTrack = state.resolvedPlaybackTrack;

      if (state.status != HitsStatus.ready || scheduleTrack == null) {
        await _stopPlaybackIfNeeded();
        return;
      }

      if (playbackTrack == null) {
        await _stopPlaybackIfNeeded();
        return;
      }

      if (state.userPaused) {
        final playback = _readPlaybackState();
        if (playback.isPlaying) {
          await _playbackController.togglePlayPause();
        } else if (playback.currentTrack?.id != playbackTrack.id &&
            (playback.hasTrack || playback.currentPlaylist.isNotEmpty)) {
          await _playbackController.stopAndClear();
        }
        _syncPlaybackFlagsInState();
        return;
      }

      // Skip tracks whose playback source recently failed (e.g. bad URL
      // that causes the player to complete within seconds).
      if (_recentlyFailedTrackIds.containsKey(playbackTrack.id)) {
        _syncPlaybackFlagsInState();
        return;
      }

      final playback = _readPlaybackState();

      // Detect rapid completion: if a track was playing but stopped within
      // 2 seconds with almost no progress, the audio source was likely bad.
      final startedAt = _lastPlaybackStartedAt;
      if (startedAt != null &&
          !playback.isPlaying &&
          !playback.isLoading &&
          playback.currentTrack?.id == playbackTrack.id) {
        final elapsed = nowUtc.difference(startedAt);
        if (elapsed < const Duration(seconds: 2) &&
            playback.currentTime < const Duration(seconds: 2)) {
          _recentlyFailedTrackIds[playbackTrack.id] = nowUtc;
          _playbackController.appendDeveloperLog(
            'hits.rapid-completion -> ${scheduleTrack.stationTrackId} '
            'elapsed=${elapsed.inMilliseconds}ms, '
            'pos=${playback.currentTime.inMilliseconds}ms — skipping',
          );
          _lastPlaybackStartedAt = null;
          // Advance schedule so the ticker moves to the next track.
          _applySchedulePosition(
            nowUtc: nowUtc.add(const Duration(seconds: 1)),
          );
          _syncPlaybackFlagsInState();
          return;
        }
      }

      final targetOffset = _clampPlaybackOffset(
        track: playbackTrack,
        desired:
            state.position?.playbackOffset ??
            nowUtc.difference(scheduleTrack.startAt),
      );
      final isSameTrack = playback.currentTrack?.id == playbackTrack.id;
      final isStandaloneTrack = isSameTrack && playback.currentPlaylist.isEmpty;

      if (!isStandaloneTrack || forceReload || !playback.isPlaying) {
        // Only stamp when the track actually changes, not on every reload
        // of the same track. This keeps rapid-completion detection accurate.
        if (!isSameTrack) {
          _lastPlaybackStartedAt = nowUtc;
        }
        await _playbackController.playStandaloneTrack(
          playbackTrack,
          initialPosition: targetOffset,
          autoplay: true,
        );
        _syncPlaybackFlagsInState(userPaused: false);
        return;
      }

      if (_playbackDrift(playback.currentTime, targetOffset) >=
          const Duration(milliseconds: 1400)) {
        await _playbackController.seekTo(targetOffset);
      }
      _syncPlaybackFlagsInState(userPaused: false);
    } finally {
      _syncInFlight = false;
    }
  }

  Future<void> _stopPlaybackIfNeeded() async {
    final playback = _readPlaybackState();
    if (!playback.hasTrack &&
        playback.currentPlaylist.isEmpty &&
        !playback.isPlaying) {
      _syncPlaybackFlagsInState();
      return;
    }

    await _playbackController.stopAndClear(useFade: true);
    _syncPlaybackFlagsInState();
  }

  void _syncPlaybackFlagsInState({bool? userPaused}) {
    state = state.copyWith(
      isSessionActive: true,
      isPlaying: _isHitsPlaybackActive(),
      userPaused: userPaused ?? state.userPaused,
    );
  }

  bool _isHitsPlaybackActive() {
    return state.isSessionActive && _readPlaybackState().isPlaying;
  }

  Duration _playbackDrift(Duration current, Duration target) {
    return Duration(
      milliseconds: (current.inMilliseconds - target.inMilliseconds).abs(),
    );
  }

  Duration _clampPlaybackOffset({
    required Track track,
    required Duration desired,
  }) {
    final safeDesired = desired < Duration.zero ? Duration.zero : desired;
    final duration = _readLibraryState().durationByPath[track.path];
    if (duration == null || duration <= Duration.zero) {
      return safeDesired;
    }

    final safeMax = duration > const Duration(milliseconds: 320)
        ? duration - const Duration(milliseconds: 320)
        : Duration.zero;
    if (safeDesired > safeMax) {
      return safeMax;
    }
    return safeDesired;
  }

  HitsStatus _statusFromPosition(HitsSchedulePosition position) {
    switch (position.kind) {
      case HitsPositionKind.onAir:
        return HitsStatus.ready;
      case HitsPositionKind.offAir:
        return HitsStatus.offAir;
      case HitsPositionKind.standby:
        return HitsStatus.standby;
    }
  }

  HitsStatus _statusFromManifestError(HitsManifestErrorKind kind) {
    switch (kind) {
      case HitsManifestErrorKind.noNetwork:
        return HitsStatus.noNetwork;
      case HitsManifestErrorKind.cloudTimeout:
        return HitsStatus.cloudTimeout;
      case HitsManifestErrorKind.unavailable:
      case HitsManifestErrorKind.invalidPayload:
        return HitsStatus.unavailable;
    }
  }

  Track? _matchLibraryTrack(HitsScheduleTrack? scheduleTrack) {
    if (scheduleTrack == null) return null;

    final library = _readLibraryState();
    if (library.tracks.isEmpty) return null;

    final targetTitle = _normalize(scheduleTrack.title);
    final targetArtist = _normalize(scheduleTrack.artist);
    final targetAlbum = _normalize(scheduleTrack.album);
    final titleVariants = {
      targetTitle,
      ...scheduleTrack.titleVariants.map(_normalize),
    }..removeWhere((item) => item.isEmpty);
    final artistVariants = {
      targetArtist,
      ...scheduleTrack.artistVariants.map(_normalize),
    }..removeWhere((item) => item.isEmpty);

    var bestScore = 0;
    Track? bestTrack;

    for (final track in library.tracks) {
      final title = _normalize(track.title);
      final artist = _normalize(track.artist);
      final album = _normalize(track.album);

      var score = 0;
      if (titleVariants.contains(title)) {
        score += 80;
      } else if (title.isNotEmpty &&
          titleVariants.any(
            (variant) => variant.contains(title) || title.contains(variant),
          )) {
        score += 42;
      }

      if (artistVariants.contains(artist)) {
        score += 72;
      } else if (artist.isNotEmpty &&
          artistVariants.any(
            (variant) => variant.contains(artist) || artist.contains(variant),
          )) {
        score += 34;
      }

      if (targetAlbum.isNotEmpty && album == targetAlbum) {
        score += 14;
      }

      final duration = library.durationByPath[track.path];
      if (duration != null && scheduleTrack.duration > Duration.zero) {
        final delta = (duration.inSeconds - scheduleTrack.duration.inSeconds)
            .abs();
        if (delta <= 2) {
          score += 10;
        } else if (delta <= 5) {
          score += 6;
        }
      }

      if (score > bestScore) {
        bestScore = score;
        bestTrack = track;
      }
    }

    return bestScore >= 96 ? bestTrack : null;
  }

  Track? _resolveImmediatePlaybackTrack({
    required HitsScheduleTrack? scheduleTrack,
    required Track? matchedTrack,
  }) {
    if (scheduleTrack == null) return null;

    final immediateSource = _resolveImmediatePlaybackSource(scheduleTrack);
    if (immediateSource != null) {
      return _buildHitsPlaybackTrack(
        scheduleTrack: scheduleTrack,
        playbackUrl: immediateSource.playbackUrl,
        playbackHeaders: immediateSource.playbackHeaders,
      );
    }

    return matchedTrack;
  }

  String _normalize(String input) {
    return input
        .toLowerCase()
        .replaceAll(RegExp(r'\[[^\]]*\]'), '')
        .replaceAll(RegExp(r'\([^)]*\)'), '')
        .replaceAll(RegExp(r'feat\.?|ft\.?|ver\.?|version|live|remix'), '')
        .replaceAll(RegExp(r'[^a-z0-9\u4e00-\u9fff]+'), '');
  }

  String _isoDate(DateTime value) {
    final utc = value.toUtc();
    final year = utc.year.toString().padLeft(4, '0');
    final month = utc.month.toString().padLeft(2, '0');
    final day = utc.day.toString().padLeft(2, '0');
    return '$year-$month-$day';
  }

  Future<void> _restorePreviousPlayback() async {
    if (_restoringSession) return;
    _restoringSession = true;
    try {
      final snapshot = _playbackSnapshot;
      if (snapshot == null) {
        await _playbackController.stopAndClear();
        return;
      }
      await _playbackController.restoreSession(snapshot);
    } finally {
      _restoringSession = false;
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _ticker?.cancel();
    _mediaCacheService.dispose();
    _audioResolverService.dispose();
    _manifestService.dispose();
    super.dispose();
    // Restore playback after super.dispose() so the StateNotifier is marked
    // disposed before the async work runs.  The _disposed flag prevents any
    // stale async callbacks from touching this controller's state.
    unawaited(_restorePreviousPlayback());
  }
}
