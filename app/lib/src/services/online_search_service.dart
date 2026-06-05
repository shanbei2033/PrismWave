import '../models/track.dart';
import 'hits_audio_resolver_service.dart';

/// Source classification for a single search result row.
enum OnlineSearchResultSource { local, online }

/// One row in the merged search list. The UI renders a "💾 / ☁️ provider"
/// badge based on [source]; selecting an [online] row routes through the
/// online controller's resolve+play, and [local] rows go through library play.
class OnlineSearchResult {
  const OnlineSearchResult({
    required this.source,
    required this.relevance,
    required this.localTrack,
    required this.onlineHit,
  });

  factory OnlineSearchResult.local({required Track track, required double relevance}) {
    return OnlineSearchResult(
      source: OnlineSearchResultSource.local,
      relevance: relevance,
      localTrack: track,
      onlineHit: null,
    );
  }

  factory OnlineSearchResult.online({
    required OnlineSearchHit hit,
    required double relevance,
  }) {
    return OnlineSearchResult(
      source: OnlineSearchResultSource.online,
      relevance: relevance,
      localTrack: null,
      onlineHit: hit,
    );
  }

  final OnlineSearchResultSource source;
  final double relevance;
  final Track? localTrack;
  final OnlineSearchHit? onlineHit;

  String get displayTitle =>
      source == OnlineSearchResultSource.local ? localTrack!.title : onlineHit!.title;

  String get displayArtist =>
      source == OnlineSearchResultSource.local ? localTrack!.artist : onlineHit!.artist;
}

/// Runs a single user query against:
///   - the local library (substring match on title/artist/album)
///   - the 9-provider online resolver (parallel)
///
/// Merges and ranks both into a single list. Local hits get a +0.3 relevance
/// boost so a song the user already owns floats above an online match for the
/// same query, but the user can still see online matches below.
class OnlineSearchService {
  OnlineSearchService(this._resolver);

  final HitsAudioResolverService _resolver;

  static const double _localBoost = 0.3;

  Future<List<OnlineSearchResult>> search({
    required String query,
    required List<Track> libraryTracks,
  }) async {
    final trimmed = query.trim();
    if (trimmed.isEmpty) return const <OnlineSearchResult>[];

    final localFuture = Future<List<OnlineSearchResult>>(
      () => _searchLocal(query: trimmed, library: libraryTracks),
    );
    final onlineFuture = _resolver.searchByQuery(trimmed).then(
          (hits) => hits
              .map((hit) => OnlineSearchResult.online(
                    hit: hit,
                    relevance: _scoreHit(hit, trimmed),
                  ))
              .toList(growable: false),
        );

    final results = await Future.wait([localFuture, onlineFuture]);
    final merged = <OnlineSearchResult>[...results[0], ...results[1]];

    // Suppress redundant online rows when the local library already has the
    // same (title|artist) — they'd be confusing duplicates.
    final localKeys = results[0]
        .map((r) =>
            '${r.displayTitle.toLowerCase()}|${r.displayArtist.toLowerCase()}')
        .toSet();
    final deduped = merged
        .where((row) =>
            row.source == OnlineSearchResultSource.local ||
            !localKeys.contains(
              '${row.displayTitle.toLowerCase()}|${row.displayArtist.toLowerCase()}',
            ))
        .toList()
      ..sort((a, b) => b.relevance.compareTo(a.relevance));
    return deduped;
  }

  List<OnlineSearchResult> _searchLocal({
    required String query,
    required List<Track> library,
  }) {
    final lower = query.toLowerCase();
    final hits = <OnlineSearchResult>[];
    for (final track in library) {
      final score = _scoreLocalTrack(track, lower);
      if (score <= 0) continue;
      hits.add(OnlineSearchResult.local(track: track, relevance: score + _localBoost));
    }
    hits.sort((a, b) => b.relevance.compareTo(a.relevance));
    return hits;
  }

  double _scoreLocalTrack(Track track, String lowerQuery) {
    final title = track.title.toLowerCase();
    final artist = track.artist.toLowerCase();
    final album = track.album.toLowerCase();

    if (title == lowerQuery) return 1.0;
    if (artist == lowerQuery) return 0.95;
    if (title.startsWith(lowerQuery)) return 0.85;
    if (artist.startsWith(lowerQuery)) return 0.8;
    if (title.contains(lowerQuery)) return 0.7;
    if (artist.contains(lowerQuery)) return 0.6;
    if (album.contains(lowerQuery)) return 0.45;
    return 0.0;
  }

  double _scoreHit(OnlineSearchHit hit, String query) {
    final lower = query.toLowerCase();
    final title = hit.title.toLowerCase();
    final artist = hit.artist.toLowerCase();

    var score = 0.0;
    if (title == lower) {
      score = 0.9;
    } else if (title.startsWith(lower)) {
      score = 0.7;
    } else if (title.contains(lower)) {
      score = 0.55;
    } else if (artist.contains(lower)) {
      score = 0.4;
    } else {
      score = 0.25;
    }

    // Audius hits with direct stream URLs play more reliably than resolver-
    // dependent providers, so nudge them slightly higher.
    if (hit.directAudioUrl != null && hit.directAudioUrl!.isNotEmpty) {
      score += 0.05;
    }
    return score;
  }
}
