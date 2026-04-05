import 'dart:typed_data';

import '../models/lyric_line.dart';
import '../models/lyrics_document.dart';
import '../models/lyrics_source_type.dart';
import '../models/track.dart';

class LibraryState {
  const LibraryState({
    this.libraryFolders = const [],
    this.tracks = const [],
    this.durationByPath = const {},
    this.coverBytesByPath = const {},
    this.customCoverPathByTrackPath = const {},
    this.lyricsOffsetSecondsByPath = const {},
    this.localLyricsByPath = const {},
    this.onlineLyricsByPath = const {},
    this.preferredLyricsSourceByPath = const {},
    this.localLyricsResolvedPaths = const {},
    this.onlineLyricsResolvedPaths = const {},
    this.lyricsLoadingPaths = const {},
    this.favoritePaths = const {},
    this.favoriteOrderPaths = const [],
    this.searchQuery = '',
    this.isScanning = false,
    this.error,
    this.lowEffects = false,
  });

  final List<String> libraryFolders;
  final List<Track> tracks;
  final Map<String, Duration> durationByPath;
  final Map<String, Uint8List> coverBytesByPath;
  final Map<String, String> customCoverPathByTrackPath;
  final Map<String, double> lyricsOffsetSecondsByPath;
  final Map<String, LyricsDocument> localLyricsByPath;
  final Map<String, LyricsDocument> onlineLyricsByPath;
  final Map<String, LyricsSourceType> preferredLyricsSourceByPath;
  final Set<String> localLyricsResolvedPaths;
  final Set<String> onlineLyricsResolvedPaths;
  final Set<String> lyricsLoadingPaths;
  final Set<String> favoritePaths;
  final List<String> favoriteOrderPaths;
  final String searchQuery;
  final bool isScanning;
  final String? error;
  final bool lowEffects;

  List<Track> get filteredTracks {
    if (searchQuery.trim().isEmpty) return tracks;
    final query = searchQuery.toLowerCase();
    return tracks
        .where(
          (track) =>
              track.title.toLowerCase().contains(query) ||
              track.artist.toLowerCase().contains(query) ||
              track.album.toLowerCase().contains(query) ||
              track.path.toLowerCase().contains(query),
        )
        .toList(growable: false);
  }

  List<Track> get favoriteTracks {
    final favoriteTrackByPath = <String, Track>{
      for (final track in filteredTracks)
        if (favoritePaths.contains(track.path)) track.path: track,
    };

    final ordered = <Track>[];
    final seen = <String>{};

    for (final path in favoriteOrderPaths) {
      final track = favoriteTrackByPath[path];
      if (track == null || !seen.add(path)) continue;
      ordered.add(track);
    }

    for (final track in filteredTracks) {
      if (!favoritePaths.contains(track.path) || !seen.add(track.path)) {
        continue;
      }
      ordered.add(track);
    }

    return ordered;
  }

  Duration? durationOf(Track track) => durationByPath[track.path];

  Uint8List? coverBytesOf(Track track) => coverBytesByPath[track.path];

  double lyricsOffsetOf(Track track) =>
      lyricsOffsetSecondsByPath[track.path] ?? 0;

  LyricsSourceType preferredLyricsSourceOf(Track track) =>
      preferredLyricsSourceByPath[track.path] ?? LyricsSourceType.local;

  LyricsSourceType effectiveLyricsSourceOf(Track track) {
    final preferred = preferredLyricsSourceOf(track);
    final local = localLyricsByPath[track.path];
    final online = onlineLyricsByPath[track.path];

    if (preferred == LyricsSourceType.local) {
      if (local != null && !local.isEmpty) return LyricsSourceType.local;
      if (online != null && !online.isEmpty) return LyricsSourceType.online;
      return LyricsSourceType.local;
    }

    if (online != null && !online.isEmpty) return LyricsSourceType.online;
    if (local != null && !local.isEmpty) return LyricsSourceType.local;
    return LyricsSourceType.online;
  }

  LyricsDocument? lyricsDocumentOf(Track track) {
    final effective = effectiveLyricsSourceOf(track);
    return switch (effective) {
      LyricsSourceType.local => localLyricsByPath[track.path],
      LyricsSourceType.online => onlineLyricsByPath[track.path],
    };
  }

  List<LyricLine> lyricsOf(Track track) {
    final lines = lyricsDocumentOf(track)?.lines ?? const <LyricLine>[];
    final offsetSeconds = lyricsOffsetOf(track);
    if (lines.isEmpty || offsetSeconds == 0) return lines;
    final offsetMs = (offsetSeconds * 1000).round();
    return lines
        .map(
          (line) => LyricLine(
            time: Duration(
              milliseconds: (line.time.inMilliseconds + offsetMs).clamp(
                0,
                1 << 31,
              ),
            ),
            text: line.text,
            segments: line.segments
                .map(
                  (segment) => LyricSegment(
                    start: Duration(
                      milliseconds: (segment.start.inMilliseconds + offsetMs)
                          .clamp(0, 1 << 31),
                    ),
                    end: Duration(
                      milliseconds: (segment.end.inMilliseconds + offsetMs)
                          .clamp(0, 1 << 31),
                    ),
                    text: segment.text,
                  ),
                )
                .toList(growable: false),
          ),
        )
        .toList(growable: false);
  }

  bool isLyricsLoading(Track track) => lyricsLoadingPaths.contains(track.path);

  bool get hasAnyLyricsData =>
      localLyricsByPath.isNotEmpty || onlineLyricsByPath.isNotEmpty;

  LibraryState copyWith({
    List<String>? libraryFolders,
    List<Track>? tracks,
    Map<String, Duration>? durationByPath,
    Map<String, Uint8List>? coverBytesByPath,
    Map<String, String>? customCoverPathByTrackPath,
    Map<String, double>? lyricsOffsetSecondsByPath,
    Map<String, LyricsDocument>? localLyricsByPath,
    Map<String, LyricsDocument>? onlineLyricsByPath,
    Map<String, LyricsSourceType>? preferredLyricsSourceByPath,
    Set<String>? localLyricsResolvedPaths,
    Set<String>? onlineLyricsResolvedPaths,
    Set<String>? lyricsLoadingPaths,
    Set<String>? favoritePaths,
    List<String>? favoriteOrderPaths,
    String? searchQuery,
    bool? isScanning,
    String? error,
    bool clearError = false,
    bool? lowEffects,
  }) {
    return LibraryState(
      libraryFolders: libraryFolders ?? this.libraryFolders,
      tracks: tracks ?? this.tracks,
      durationByPath: durationByPath ?? this.durationByPath,
      coverBytesByPath: coverBytesByPath ?? this.coverBytesByPath,
      customCoverPathByTrackPath:
          customCoverPathByTrackPath ?? this.customCoverPathByTrackPath,
      lyricsOffsetSecondsByPath:
          lyricsOffsetSecondsByPath ?? this.lyricsOffsetSecondsByPath,
      localLyricsByPath: localLyricsByPath ?? this.localLyricsByPath,
      onlineLyricsByPath: onlineLyricsByPath ?? this.onlineLyricsByPath,
      preferredLyricsSourceByPath:
          preferredLyricsSourceByPath ?? this.preferredLyricsSourceByPath,
      localLyricsResolvedPaths:
          localLyricsResolvedPaths ?? this.localLyricsResolvedPaths,
      onlineLyricsResolvedPaths:
          onlineLyricsResolvedPaths ?? this.onlineLyricsResolvedPaths,
      lyricsLoadingPaths: lyricsLoadingPaths ?? this.lyricsLoadingPaths,
      favoritePaths: favoritePaths ?? this.favoritePaths,
      favoriteOrderPaths: favoriteOrderPaths ?? this.favoriteOrderPaths,
      searchQuery: searchQuery ?? this.searchQuery,
      isScanning: isScanning ?? this.isScanning,
      error: clearError ? null : (error ?? this.error),
      lowEffects: lowEffects ?? this.lowEffects,
    );
  }
}
