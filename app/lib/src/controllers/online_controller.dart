import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/online_recommendation.dart';
import '../models/track.dart';
import '../services/hits_audio_resolver_service.dart';
import '../services/netease_home_service.dart';
import '../services/online_search_service.dart';
import '../services/online_url_utils.dart';
import '../state/library_state.dart';
import '../state/online_state.dart';
import 'playback_controller.dart';

typedef OnlineLibraryReader = LibraryState Function();
typedef OnlineDebugLogger = void Function(String message, {bool force});

class OnlineController extends StateNotifier<OnlineState> {
  OnlineController({
    required PlaybackController playbackController,
    required OnlineLibraryReader readLibraryState,
    OnlineDebugLogger? debugLog,
    HitsAudioResolverService? resolver,
    NeteaseHomeService? homeService,
    OnlineSearchService? searchService,
  }) : _playbackController = playbackController,
       _readLibraryState = readLibraryState,
       _debugLog = debugLog,
       _resolver = resolver ?? HitsAudioResolverService(debugLog: debugLog),
       _homeService = homeService ?? NeteaseHomeService(),
       super(const OnlineState()) {
    _searchService = searchService ?? OnlineSearchService(_resolver);
    _playbackController.setQueueTrackResolver(_resolveQueuedPlaybackTrack);
    _playbackController.setQueueTrackFailureHandler(
      _handleQueuedPlaybackFailure,
    );
    // Keep startup responsive: the app opens online Home by default, so
    // resolver warm-up waits until the first frame and home cache load have a
    // chance to settle.
    _resolverWarmUpTimer = Timer(const Duration(seconds: 3), () {
      if (_disposed) return;
      unawaited(_resolver.warmUp());
    });
    _homeAutoRefreshTimer = Timer.periodic(_homeAutoRefreshInterval, (_) {
      _maybeAutoRefreshHome(reason: 'periodic');
    });
  }

  final PlaybackController _playbackController;
  final OnlineLibraryReader _readLibraryState;
  final OnlineDebugLogger? _debugLog;
  final HitsAudioResolverService _resolver;
  final NeteaseHomeService _homeService;
  late final OnlineSearchService _searchService;

  Timer? _searchDebounce;
  Timer? _resolverWarmUpTimer;
  Timer? _homeAutoRefreshTimer;
  DateTime? _lastHomeAutoRefreshAttemptAt;
  int _homeSeq = 0;
  int _searchSeq = 0;
  int _playbackSeq = 0;
  bool _disposed = false;
  bool _homeBackgroundRefreshRunning = false;
  final Map<String, OnlineTrackCandidate> _queueCandidatesByTrackId = {};
  final Map<String, Future<Track?>> _queueResolvePending = {};
  final Set<String> _forceRefreshCandidateKeys = <String>{};
  final Set<String> _homeCoverEnrichmentKeysInFlight = <String>{};
  static const Duration _searchDebounceWindow = Duration(milliseconds: 320);
  static const Duration _homeAutoRefreshInterval = Duration(minutes: 30);
  static const int _queueResolveConcurrency = 3;

  Future<void> ensureHomeLoaded({bool forceRefresh = false}) async {
    if (state.home.status == OnlineHomeStatus.loading) return;
    if (!forceRefresh &&
        state.home.status == OnlineHomeStatus.ready &&
        state.home.data != null) {
      if (!_homeService.isFreshData(state.home.data!)) {
        _refreshHomeInBackground(reason: 'ready-state');
        return;
      }
      if (_shouldAutoRefreshHomeData(state.home.data!)) {
        _refreshHomeInBackground(reason: 'ready-auto-age');
        return;
      }
      _enrichHomeCoversIfNeeded(
        state.home.data!,
        seq: _homeSeq,
        reason: 'ready-state',
      );
      return;
    }

    final seq = ++_homeSeq;
    final stopwatch = Stopwatch()..start();
    final hadData = state.home.data != null;
    _debug(
      'home.load.start -> forceRefresh=$forceRefresh hadData=$hadData',
      force: true,
    );
    state = state.copyWith(
      home: state.home.copyWith(
        status: OnlineHomeStatus.loading,
        clearError: true,
      ),
    );

    try {
      final bundle = await _loadHomeBundleForInitialDisplay(
        forceRefresh: forceRefresh,
      );
      if (_disposed || seq != _homeSeq) return;
      final isFresh = _homeService.isFreshBundle(bundle);
      state = state.copyWith(
        home: OnlineHomeView(
          status: OnlineHomeStatus.ready,
          data: bundle.data,
          usedCache: bundle.usedCache,
          errorMessage: '',
          recommendationsUnavailable: bundle.recommendationsUnavailable,
        ),
      );
      _debug(
        'home.load.ready -> forceRefresh=$forceRefresh '
        'usedCache=${bundle.usedCache} fresh=$isFresh '
        'edition=${bundle.data.editionDate} '
        'sections=${bundle.data.sections.length} '
        'albums=${bundle.data.albumRecommendations.length} '
        'elapsedMs=${stopwatch.elapsedMilliseconds}',
        force: true,
      );
      final autoRefreshDue = !forceRefresh && _shouldAutoRefreshHomeData(
        bundle.data,
      );
      if (!forceRefresh &&
          (bundle.needsBackgroundRefresh || autoRefreshDue) &&
          !bundle.recommendationsUnavailable) {
        _refreshHomeInBackground(
          reason: bundle.needsBackgroundRefresh
              ? 'partial-fast-load'
              : 'auto-refresh-due',
        );
      } else if (isFresh) {
        _enrichHomeCoversIfNeeded(
          bundle.data,
          seq: seq,
          reason: forceRefresh ? 'manual-refresh' : 'fresh-load',
        );
      } else if (!forceRefresh) {
        _refreshHomeInBackground(reason: 'stale-cache');
      } else {
        _debug(
          'home.load.stale-after-manual-refresh -> usedCache=${bundle.usedCache} '
          'edition=${bundle.data.editionDate}',
          force: true,
        );
      }
    } catch (error) {
      if (_disposed || seq != _homeSeq) return;
      _debug(
        'home.load.failed -> forceRefresh=$forceRefresh '
        'elapsedMs=${stopwatch.elapsedMilliseconds} error=$error',
        force: true,
      );
      state = state.copyWith(
        home: state.home.copyWith(
          status: OnlineHomeStatus.failed,
          errorMessage: error.toString(),
        ),
      );
    }
  }

  Future<bool> refreshHomeRecommendations() async {
    if (state.home.status == OnlineHomeStatus.loading) return false;

    final seq = ++_homeSeq;
    final stopwatch = Stopwatch()..start();
    final previousData = state.home.data;
    _debug(
      'home.manual-refresh.start -> hadData=${previousData != null}',
      force: true,
    );
    state = state.copyWith(
      home: state.home.copyWith(
        status: OnlineHomeStatus.loading,
        clearError: true,
      ),
    );

    try {
      final bundle = await _homeService.refreshLiveHome();
      if (_disposed || seq != _homeSeq) return false;
      state = state.copyWith(
        home: OnlineHomeView(
          status: OnlineHomeStatus.ready,
          data: bundle.data,
          usedCache: bundle.usedCache,
          errorMessage: '',
          recommendationsUnavailable: bundle.recommendationsUnavailable,
        ),
      );
      _debug(
        'home.manual-refresh.ready -> '
        'topPlaylistUpdated=${bundle.data.topPlaylist != null} '
        'edition=${bundle.data.editionDate} '
        'sections=${bundle.data.sections.length} '
        'albums=${bundle.data.albumRecommendations.length} '
        'elapsedMs=${stopwatch.elapsedMilliseconds}',
        force: true,
      );
      _enrichHomeCoversIfNeeded(
        bundle.data,
        seq: seq,
        reason: 'manual-refresh',
      );
      return true;
    } catch (error) {
      if (_disposed || seq != _homeSeq) return false;
      _debug(
        'home.manual-refresh.failed -> '
        'elapsedMs=${stopwatch.elapsedMilliseconds} error=$error',
        force: true,
      );
      final fallback = previousData != null &&
              _homeService.isFreshData(previousData)
          ? null
          : await _homeService.loadYesterdayCachedBundle();
      if (_disposed || seq != _homeSeq) return false;
      if (fallback != null) {
        state = state.copyWith(
          home: OnlineHomeView(
            status: OnlineHomeStatus.ready,
            data: fallback.data,
            usedCache: fallback.usedCache,
            errorMessage: error.toString(),
            recommendationsUnavailable: true,
          ),
        );
        return false;
      }
      final bundled = await _homeService.loadBundledFallbackBundle();
      if (_disposed || seq != _homeSeq) return false;
      if (bundled != null) {
        state = state.copyWith(
          home: OnlineHomeView(
            status: OnlineHomeStatus.ready,
            data: bundled.data,
            usedCache: bundled.usedCache,
            errorMessage: error.toString(),
            recommendationsUnavailable: true,
          ),
        );
        return false;
      }
      state = state.copyWith(
        home: state.home.copyWith(
          status: previousData == null
              ? OnlineHomeStatus.failed
              : OnlineHomeStatus.ready,
          errorMessage: error.toString(),
          recommendationsUnavailable: previousData != null &&
              !_homeService.isFreshData(previousData),
        ),
      );
      return false;
    }
  }

  void _debug(String message, {bool force = false}) {
    _debugLog?.call('online.$message', force: force);
  }

  Future<OnlineHomeBundle> _loadHomeBundleForInitialDisplay({
    required bool forceRefresh,
  }) async {
    if (forceRefresh) {
      return _homeService.loadBundle(forceRefresh: true);
    }

    final cached = await _homeService.loadCachedBundle(allowStale: true);
    if (cached != null) return cached;

    _debug('home.load.no-cache -> trying remote-daily-fast', force: true);
    try {
      return await _homeService.loadRemoteDailyBundle();
    } catch (error) {
      _debug(
        'home.load.remote-daily-fast.failed -> fallback=yesterday-cache error=$error',
        force: true,
      );
      final yesterday = await _homeService.loadYesterdayCachedBundle();
      if (yesterday != null) return yesterday;
      final bundled = await _homeService.loadBundledFallbackBundle();
      if (bundled != null) return bundled;
      return _homeService.loadBundle(forceRefresh: false);
    }
  }

  void _refreshHomeInBackground({required String reason}) {
    if (_disposed || _homeBackgroundRefreshRunning) {
      _debug(
        'home.refresh-background.skip -> reason=$reason '
        'running=$_homeBackgroundRefreshRunning disposed=$_disposed',
      );
      return;
    }

    _homeBackgroundRefreshRunning = true;
    _lastHomeAutoRefreshAttemptAt = DateTime.now().toUtc();
    final seq = ++_homeSeq;
    unawaited(() async {
      final stopwatch = Stopwatch()..start();
      _debug('home.refresh-background.start -> reason=$reason', force: true);
      try {
        final bundle = await _homeService.loadBundle(forceRefresh: true);
        if (_disposed || seq != _homeSeq) return;
        final isFresh = _homeService.isFreshBundle(bundle);
        state = state.copyWith(
          home: OnlineHomeView(
            status: OnlineHomeStatus.ready,
            data: bundle.data,
            usedCache: bundle.usedCache,
            errorMessage: '',
            recommendationsUnavailable: bundle.recommendationsUnavailable,
          ),
        );
        _debug(
          'home.refresh-background.ready -> reason=$reason '
          'fresh=$isFresh edition=${bundle.data.editionDate} '
          'sections=${bundle.data.sections.length} '
          'albums=${bundle.data.albumRecommendations.length} '
          'elapsedMs=${stopwatch.elapsedMilliseconds}',
          force: true,
        );
        _enrichHomeCoversIfNeeded(
          bundle.data,
          seq: seq,
          reason: 'background-refresh',
        );
      } catch (error) {
        _debug(
          'home.refresh-background.failed -> reason=$reason '
          'elapsedMs=${stopwatch.elapsedMilliseconds} error=$error',
          force: true,
        );
      } finally {
        _homeBackgroundRefreshRunning = false;
      }
    }());
  }

  void _maybeAutoRefreshHome({required String reason}) {
    if (_disposed ||
        _homeBackgroundRefreshRunning ||
        state.home.status == OnlineHomeStatus.loading) {
      return;
    }
    final data = state.home.data;
    if (data == null || !_shouldAutoRefreshHomeData(data)) return;
    _refreshHomeInBackground(reason: reason);
  }

  bool _shouldAutoRefreshHomeData(OnlineHomeData data) {
    final now = DateTime.now().toUtc();
    final lastAttempt = _lastHomeAutoRefreshAttemptAt;
    if (lastAttempt != null &&
        now.difference(lastAttempt) < _homeAutoRefreshInterval) {
      return false;
    }
    return !_homeService.isFreshData(data);
  }

  void _enrichHomeCoversIfNeeded(
    OnlineHomeData data, {
    required int seq,
    required String reason,
  }) {
    if (!_homeService.needsMainlandCoverFallbacks(data)) return;
    final key = _homeDataRefreshKey(data);
    if (!_homeCoverEnrichmentKeysInFlight.add(key)) {
      _debug('home.cover-enrich.skip -> reason=$reason key=$key');
      return;
    }

    unawaited(() async {
      final stopwatch = Stopwatch()..start();
      _debug('home.cover-enrich.start -> reason=$reason key=$key', force: true);
      try {
        final bundle = await _homeService.enrichMainlandCoverFallbacks(data);
        if (_disposed || seq != _homeSeq) return;
        state = state.copyWith(
          home: OnlineHomeView(
            status: OnlineHomeStatus.ready,
            data: bundle.data,
            usedCache: bundle.usedCache,
            errorMessage: '',
            recommendationsUnavailable:
                state.home.recommendationsUnavailable,
          ),
        );
        _debug(
          'home.cover-enrich.ready -> reason=$reason key=$key '
          'elapsedMs=${stopwatch.elapsedMilliseconds}',
          force: true,
        );
      } catch (error) {
        _debug(
          'home.cover-enrich.failed -> reason=$reason key=$key '
          'elapsedMs=${stopwatch.elapsedMilliseconds} error=$error',
          force: true,
        );
      } finally {
        _homeCoverEnrichmentKeysInFlight.remove(key);
      }
    }());
  }

  String _homeDataRefreshKey(OnlineHomeData data) {
    final topCount = data.topPlaylist?.tracks.length ?? 0;
    final sectionCount = data.sections.fold<int>(
      0,
      (count, section) => count + section.tracks.length,
    );
    return '${data.editionDate}|${data.generatedAt.toUtc().toIso8601String()}|'
        '$topCount|$sectionCount';
  }

  void setSearchQuery(String value) {
    final trimmed = value;
    state = state.copyWith(search: state.search.copyWith(query: trimmed));

    _searchDebounce?.cancel();
    final cleaned = trimmed.trim();
    if (cleaned.isEmpty) {
      _searchSeq++;
      state = state.copyWith(
        search: const OnlineSearchView(
          query: '',
          status: OnlineSearchStatus.idle,
          results: <OnlineSearchResult>[],
          errorMessage: '',
        ),
      );
      return;
    }

    final seq = ++_searchSeq;
    state = state.copyWith(
      search: state.search.copyWith(
        status: OnlineSearchStatus.searching,
        clearError: true,
      ),
    );

    _searchDebounce = Timer(
      _searchDebounceWindow,
      () => _runSearch(cleaned, seq),
    );
  }

  Future<void> _runSearch(String query, int seq) async {
    try {
      final library = _readLibraryState();
      final results = await _searchService.search(
        query: query,
        libraryTracks: library.tracks,
      );
      if (_disposed || seq != _searchSeq) return;
      state = state.copyWith(
        search: state.search.copyWith(
          status: OnlineSearchStatus.ready,
          results: results,
          clearError: true,
        ),
      );
    } catch (error) {
      if (_disposed || seq != _searchSeq) return;
      state = state.copyWith(
        search: state.search.copyWith(
          status: OnlineSearchStatus.failed,
          errorMessage: error.toString(),
        ),
      );
    }
  }

  /// Play [picked] from a section, establishing the entire [contextTracks]
  /// list as the active playback queue.
  ///
  /// **Resolution strategy**: resolve and play [picked] first, then resolve
  /// the rest of [contextTracks] in the background. The full metadata queue is
  /// published immediately so the playback list appears at once; resolved
  /// playable URLs are patched into that queue as they arrive. Resolving the
  /// whole list before playback made large online sections feel frozen for
  /// 10+ seconds.
  ///
  /// Tracks that fail to resolve are dropped from the queue; if [picked]
  /// itself fails, the call surfaces an error and does not change playback.
  Future<void> playOnlineTrack({
    required OnlineTrackCandidate picked,
    required List<OnlineTrackCandidate> contextTracks,
  }) async {
    final seq = ++_playbackSeq;
    final stopwatch = Stopwatch()..start();
    final resolveKey = picked.canonicalKey;
    state = state.copyWith(
      resolve: state.resolve.copyWith(
        resolvingTrackKey: resolveKey,
        clearError: true,
      ),
    );
    _debug(
      'play.start -> key=$resolveKey title="${picked.title}" '
      'provider=${picked.audioProvider ?? 'none'} '
      'context=${contextTracks.length}',
      force: true,
    );

    try {
      final resolveOrder = contextTracks.isEmpty
          ? <OnlineTrackCandidate>[picked]
          : contextTracks;

      final pickedTrack = await _resolveCandidateToTrack(picked);
      if (_disposed || seq != _playbackSeq) return;

      if (pickedTrack == null) {
        _debug(
          'play.picked-failed -> key=$resolveKey elapsedMs=${stopwatch.elapsedMilliseconds}',
          force: true,
        );
        state = state.copyWith(
          resolve: state.resolve.copyWith(
            clearResolvingTrack: true,
            errorMessage: 'No playable source found.',
          ),
        );
        return;
      }

      _debug(
        'play.picked-resolved -> key=$resolveKey elapsedMs=${stopwatch.elapsedMilliseconds} '
        'remote=${pickedTrack.isRemote}',
        force: true,
      );

      final ordered = _rotateCandidatesToPickedFirst(resolveOrder, picked);
      final placeholderQueue = _metadataQueueForCandidates(
        candidates: ordered,
        picked: picked,
        pickedTrack: pickedTrack,
      );
      _rememberQueueCandidates(ordered, placeholderQueue);

      await _playbackController.playFromPlaylist(
        pickedTrack,
        placeholderQueue,
        includeUnplayableInQueue: true,
      );
      _debug(
        'queue.placeholder-published -> key=$resolveKey '
        'length=${placeholderQueue.length} elapsedMs=${stopwatch.elapsedMilliseconds}',
        force: true,
      );

      if (_disposed) return;
      state = state.copyWith(
        resolve: state.resolve.copyWith(
          clearResolvingTrack: true,
          clearError: true,
        ),
      );
      _debug(
        'play.started -> key=$resolveKey elapsedMs=${stopwatch.elapsedMilliseconds}',
        force: true,
      );

      _resolveRemainingQueueInBackground(
        seq: seq,
        picked: picked,
        pickedTrack: pickedTrack,
        ordered: ordered,
        initialQueue: placeholderQueue,
      );
    } catch (error) {
      if (_disposed) return;
      _debug(
        'play.error -> key=$resolveKey elapsedMs=${stopwatch.elapsedMilliseconds} error=$error',
        force: true,
      );
      state = state.copyWith(
        resolve: state.resolve.copyWith(
          clearResolvingTrack: true,
          errorMessage: error.toString(),
        ),
      );
    }
  }

  void _resolveRemainingQueueInBackground({
    required int seq,
    required OnlineTrackCandidate picked,
    required Track pickedTrack,
    required List<OnlineTrackCandidate> ordered,
    required List<Track> initialQueue,
  }) {
    if (ordered.length <= 1) return;

    unawaited(() async {
      final stopwatch = Stopwatch()..start();
      _debug(
        'queue.resolve-start -> key=${picked.canonicalKey} candidates=${ordered.length}',
        force: true,
      );

      final queue = List<Track>.from(initialQueue, growable: false);
      var failures = 0;
      _rememberQueueCandidates(ordered, queue);

      var cursor = 1;
      final workerCount = ordered.length <= 1
          ? 0
          : ordered.length - 1 < _queueResolveConcurrency
          ? ordered.length - 1
          : _queueResolveConcurrency;
      final workers = List.generate(workerCount, (_) async {
        while (true) {
          if (_disposed || seq != _playbackSeq) return;
          final index = cursor;
          cursor += 1;
          if (index >= ordered.length) return;

          final candidate = ordered[index];
          final resolved = await _resolveCandidateToTrack(candidate);
          if (_disposed || seq != _playbackSeq) return;
          if (resolved == null) {
            failures += 1;
            _debug(
              'queue.resolve-skip -> key=${candidate.canonicalKey} '
              'title="${candidate.title}"',
            );
            continue;
          }

          queue[index] = resolved;
          _rememberQueueCandidate(candidate, resolved);
          _mergeResolvedQueueEntries(queue);
          await _playbackController.replaceQueuePreservingCurrent(
            queue,
            includeUnplayable: true,
          );
        }
      });
      await Future.wait(workers);

      if (_disposed || seq != _playbackSeq) return;
      _mergeResolvedQueueEntries(queue);
      _rememberQueueCandidates(ordered, queue);
      await _playbackController.replaceQueuePreservingCurrent(
        queue,
        includeUnplayable: true,
      );
      _debug(
        'queue.resolve-complete -> key=${picked.canonicalKey} '
        'playable=${queue.where((track) => track.isRemote).length}/${ordered.length} failed=$failures '
        'elapsedMs=${stopwatch.elapsedMilliseconds}',
        force: true,
      );
    }());
  }

  List<Track> _metadataQueueForCandidates({
    required List<OnlineTrackCandidate> candidates,
    required OnlineTrackCandidate picked,
    required Track pickedTrack,
  }) {
    return candidates
        .map((candidate) {
          if (candidate.canonicalKey == picked.canonicalKey) return pickedTrack;
          return candidate.toTrack();
        })
        .toList(growable: false);
  }

  void _rememberQueueCandidates(
    List<OnlineTrackCandidate> candidates,
    List<Track> tracks,
  ) {
    _queueCandidatesByTrackId.clear();
    final count = candidates.length < tracks.length
        ? candidates.length
        : tracks.length;
    for (var i = 0; i < count; i += 1) {
      _rememberQueueCandidate(candidates[i], tracks[i]);
    }
  }

  void _rememberQueueCandidate(OnlineTrackCandidate candidate, Track track) {
    _queueCandidatesByTrackId[track.id] = candidate;
    _queueCandidatesByTrackId[track.path] = candidate;
  }

  void _mergeResolvedQueueEntries(List<Track> queue) {
    final currentQueue = _playbackController.state.currentPlaylist;
    if (currentQueue.isEmpty) return;

    final resolvedByCandidateKey = <String, Track>{};
    for (final track in currentQueue) {
      if (!track.isRemote) continue;
      final candidate =
          _queueCandidatesByTrackId[track.id] ??
          _queueCandidatesByTrackId[track.path];
      if (candidate != null) {
        resolvedByCandidateKey[candidate.canonicalKey] = track;
      }
    }

    for (var i = 0; i < queue.length; i += 1) {
      if (queue[i].isRemote) continue;
      final candidate =
          _queueCandidatesByTrackId[queue[i].id] ??
          _queueCandidatesByTrackId[queue[i].path];
      final resolved = candidate == null
          ? null
          : resolvedByCandidateKey[candidate.canonicalKey];
      if (resolved != null) {
        queue[i] = resolved;
        _rememberQueueCandidate(candidate!, resolved);
      }
    }
  }

  Future<Track?> _resolveQueuedPlaybackTrack(
    Track track,
    int index, {
    bool forceRefresh = false,
  }) async {
    final candidate =
        _queueCandidatesByTrackId[track.id] ??
        _queueCandidatesByTrackId[track.path];
    if (candidate == null) {
      _debug(
        'queue.resolve-on-demand.miss -> index=$index '
        'title="${track.title}" path=${track.path}',
        force: true,
      );
      return null;
    }

    final shouldForceRefresh =
        forceRefresh ||
        _forceRefreshCandidateKeys.remove(candidate.canonicalKey);

    final pending = _queueResolvePending[candidate.canonicalKey];
    if (pending != null && !shouldForceRefresh) {
      _debug(
        'queue.resolve-on-demand.join -> key=${candidate.canonicalKey} '
        'index=$index title="${track.title}"',
      );
      return pending;
    }

    final future = () async {
      final stopwatch = Stopwatch()..start();
      _debug(
        'queue.resolve-on-demand.start -> key=${candidate.canonicalKey} '
        'index=$index title="${candidate.title}" '
        'forceRefresh=$shouldForceRefresh',
        force: true,
      );
      try {
        if (shouldForceRefresh) {
          _resolver.invalidateSearchHit(_hitFromCandidate(candidate));
        }
        final resolved = await _resolveCandidateToTrack(candidate);
        if (resolved == null) {
          _debug(
            'queue.resolve-on-demand.failed -> key=${candidate.canonicalKey} '
            'elapsedMs=${stopwatch.elapsedMilliseconds}',
            force: true,
          );
          return null;
        }
        _rememberQueueCandidate(candidate, resolved);
        _debug(
          'queue.resolve-on-demand.ok -> key=${candidate.canonicalKey} '
          'provider=${candidate.audioProvider ?? 'auto'} '
          'forceRefresh=$shouldForceRefresh '
          'elapsedMs=${stopwatch.elapsedMilliseconds}',
          force: true,
        );
        return resolved;
      } finally {
        _queueResolvePending.remove(candidate.canonicalKey);
      }
    }();

    _queueResolvePending[candidate.canonicalKey] = future;
    return future;
  }

  void _handleQueuedPlaybackFailure(Track track, String reason) {
    final candidate =
        _queueCandidatesByTrackId[track.id] ??
        _queueCandidatesByTrackId[track.path];
    if (candidate == null) {
      _debug(
        'queue.playback-failure.miss -> title="${track.title}" '
        'path=${track.path} reason=$reason',
        force: true,
      );
      return;
    }

    _forceRefreshCandidateKeys.add(candidate.canonicalKey);
    _rememberQueueCandidate(candidate, track);
    _rememberQueueCandidate(candidate, candidate.toTrack());
    _resolver.invalidateSearchHit(_hitFromCandidate(candidate));
    _debug(
      'queue.playback-failure.invalidate -> key=${candidate.canonicalKey} '
      'title="${candidate.title}" reason=$reason',
      force: true,
    );
  }

  List<OnlineTrackCandidate> _rotateCandidatesToPickedFirst(
    List<OnlineTrackCandidate> candidates,
    OnlineTrackCandidate picked,
  ) {
    if (candidates.isEmpty) return <OnlineTrackCandidate>[picked];
    final index = candidates.indexWhere(
      (item) => item.canonicalKey == picked.canonicalKey,
    );
    if (index < 0) return <OnlineTrackCandidate>[picked, ...candidates];
    if (index == 0) return List<OnlineTrackCandidate>.from(candidates);
    return <OnlineTrackCandidate>[
      ...candidates.skip(index),
      ...candidates.take(index),
    ];
  }

  /// Play a [hit] returned from search. Builds an online queue from sibling
  /// hits (same search result list) so next/prev still works.
  Future<void> playSearchHit({
    required OnlineSearchHit hit,
    required List<OnlineSearchHit> contextHits,
  }) async {
    final asCandidate = _candidateFromHit(hit);
    final contextCandidates = contextHits
        .map(_candidateFromHit)
        .toList(growable: false);
    await playOnlineTrack(
      picked: asCandidate,
      contextTracks: contextCandidates,
    );
  }

  /// Play a local library track using the same playback infrastructure.
  /// Convenience method so the search UI has a single play sink for both
  /// kinds of results.
  Future<void> playLocalTrack({
    required Track track,
    required List<Track> queue,
  }) async {
    await _playbackController.playFromPlaylist(track, queue);
  }

  /// Loads an album's full track list and stores it in `state.albumDetail`.
  /// The detail panel observes this state. If the same album is already
  /// loaded, this is a no-op so reopening the panel is instant.
  Future<void> loadAlbumDetail(OnlineAlbumCard album) async {
    final current = state.albumDetail;
    if (current.status == OnlineAlbumDetailStatus.ready &&
        current.album?.canonicalKey == album.canonicalKey) {
      return;
    }

    state = state.copyWith(
      albumDetail: OnlineAlbumDetailView(
        status: OnlineAlbumDetailStatus.loading,
        album: album,
        tracks: const <OnlineTrackCandidate>[],
        errorMessage: '',
      ),
    );

    try {
      final rawTracks = await _homeService.loadAlbumTracks(album.albumId);
      if (_disposed) return;
      final candidates = <OnlineTrackCandidate>[];
      for (var i = 0; i < rawTracks.length; i++) {
        final c = OnlineTrackCandidate.fromJson(
          rawTracks[i],
          sectionId: album.canonicalKey,
          index: i,
        );
        if (c != null) candidates.add(c);
      }

      state = state.copyWith(
        albumDetail: OnlineAlbumDetailView(
          status: candidates.isEmpty
              ? OnlineAlbumDetailStatus.failed
              : OnlineAlbumDetailStatus.ready,
          album: album,
          tracks: List.unmodifiable(candidates),
          errorMessage: candidates.isEmpty ? 'No tracks found in album.' : '',
        ),
      );
    } catch (error) {
      if (_disposed) return;
      state = state.copyWith(
        albumDetail: OnlineAlbumDetailView(
          status: OnlineAlbumDetailStatus.failed,
          album: album,
          tracks: const <OnlineTrackCandidate>[],
          errorMessage: error.toString(),
        ),
      );
    }
  }

  /// Fetch an album's full track list from NetEase and play it as a queue.
  /// Resolves all tracks in parallel before starting playback so the queue
  /// stays intact (PlaybackController drops tracks without a playbackUrl).
  Future<void> playOnlineAlbum(OnlineAlbumCard album) async {
    final resolveKey = album.canonicalKey;
    state = state.copyWith(
      resolve: state.resolve.copyWith(
        resolvingTrackKey: resolveKey,
        clearError: true,
      ),
    );

    try {
      final rawTracks = await _homeService.loadAlbumTracks(album.albumId);
      if (_disposed) return;
      if (rawTracks.isEmpty) {
        state = state.copyWith(
          resolve: state.resolve.copyWith(
            clearResolvingTrack: true,
            errorMessage: 'No tracks found in album.',
          ),
        );
        return;
      }

      final candidates = <OnlineTrackCandidate>[];
      for (var i = 0; i < rawTracks.length; i++) {
        final c = OnlineTrackCandidate.fromJson(
          rawTracks[i],
          sectionId: album.canonicalKey,
          index: i,
        );
        if (c != null) candidates.add(c);
      }
      if (candidates.isEmpty) {
        state = state.copyWith(
          resolve: state.resolve.copyWith(
            clearResolvingTrack: true,
            errorMessage: 'No playable tracks in album.',
          ),
        );
        return;
      }

      await playOnlineTrack(
        picked: candidates.first,
        contextTracks: candidates,
      );
    } catch (error) {
      if (_disposed) return;
      state = state.copyWith(
        resolve: state.resolve.copyWith(
          clearResolvingTrack: true,
          errorMessage: error.toString(),
        ),
      );
    }
  }

  /// Resolves [candidate] to a [Track] with a real `playbackUrl`. Returns null
  /// if no playable source can be found (in which case the caller should not
  /// attempt to play the track).
  Future<Track?> _resolveCandidateToTrack(
    OnlineTrackCandidate candidate,
  ) async {
    final stopwatch = Stopwatch()..start();
    // Fast path: candidate already carries a direct, playable URL (Audius).
    final directUrl = candidate.audioUrl;
    if (directUrl != null && !isNonPlayableAudioUrl(directUrl)) {
      _debug(
        'resolve.direct -> key=${candidate.canonicalKey} '
        'provider=${candidate.audioProvider ?? 'direct'} '
        'elapsedMs=${stopwatch.elapsedMilliseconds}',
      );
      return candidate.toTrack();
    }

    // Slow path: we need the resolver. Build a synthetic search hit and let
    // the resolver use its pinned-provider path (when provider+id known) or
    // fall through to the multi-provider search chain.
    final hit = OnlineSearchHit(
      provider: candidate.audioProvider ?? '',
      providerTrackId: candidate.providerTrackId ?? '',
      title: candidate.title,
      artist: candidate.artist,
      album: candidate.album,
      durationMs: candidate.durationMs,
      coverUrl: candidate.coverUrl,
      directAudioUrl: (directUrl != null && !isNonPlayableAudioUrl(directUrl))
          ? directUrl
          : null,
    );

    final resolved = await _resolver.resolveSearchHit(hit);
    if (resolved == null) {
      _debug(
        'resolve.failed -> key=${candidate.canonicalKey} '
        'provider=${candidate.audioProvider ?? 'auto'} '
        'elapsedMs=${stopwatch.elapsedMilliseconds}',
      );
      return null;
    }
    if (isNonPlayableAudioUrl(resolved.playbackUrl)) {
      _debug(
        'resolve.nonplayable -> key=${candidate.canonicalKey} '
        'provider=${resolved.provider ?? candidate.audioProvider ?? 'auto'} '
        'elapsedMs=${stopwatch.elapsedMilliseconds}',
      );
      return null;
    }

    _debug(
      'resolve.ok -> key=${candidate.canonicalKey} '
      'provider=${resolved.provider ?? candidate.audioProvider ?? 'auto'} '
      'elapsedMs=${stopwatch.elapsedMilliseconds}',
    );
    return Track(
      path:
          'online://${candidate.audioProvider ?? resolved.provider ?? 'unknown'}/'
          '${candidate.providerTrackId ?? resolved.providerTrackId ?? candidate.canonicalKey}',
      title: candidate.title,
      artist: candidate.artist,
      album: candidate.album,
      coverPath: candidate.coverUrl ?? resolved.coverUrl,
      playbackUrl: resolved.playbackUrl,
      playbackHeaders: resolved.playbackHeaders,
    );
  }

  OnlineTrackCandidate _candidateFromHit(OnlineSearchHit hit) {
    final fakeJson = <String, dynamic>{
      'title': hit.title,
      'artist': hit.artist.trim().isEmpty ? 'Unknown Artist' : hit.artist,
      'album': hit.album,
      'durationMs': hit.durationMs,
      'coverUrl': hit.coverUrl,
      'audioUrl': hit.directAudioUrl,
      'audioProvider': hit.provider,
      'providerTrackId': hit.providerTrackId,
      'sourceTags': <String>[hit.provider],
    };
    final result = OnlineTrackCandidate.fromJson(
      fakeJson,
      sectionId: 'search',
      index: 0,
    );
    return result!;
  }

  OnlineSearchHit _hitFromCandidate(OnlineTrackCandidate candidate) {
    return OnlineSearchHit(
      provider: candidate.audioProvider ?? '',
      providerTrackId: candidate.providerTrackId ?? '',
      title: candidate.title,
      artist: candidate.artist,
      album: candidate.album,
      durationMs: candidate.durationMs,
      coverUrl: candidate.coverUrl,
      directAudioUrl:
          (candidate.audioUrl != null &&
              !isNonPlayableAudioUrl(candidate.audioUrl))
          ? candidate.audioUrl
          : null,
    );
  }

  @override
  void dispose() {
    _disposed = true;
    _searchDebounce?.cancel();
    _resolverWarmUpTimer?.cancel();
    _homeAutoRefreshTimer?.cancel();
    _playbackController.setQueueTrackResolver(null);
    _playbackController.setQueueTrackFailureHandler(null);
    _homeService.dispose();
    _resolver.dispose();
    super.dispose();
  }
}
