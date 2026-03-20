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
    this.localLyricsByPath = const {},
    this.onlineLyricsByPath = const {},
    this.preferredLyricsSourceByPath = const {},
    this.localLyricsResolvedPaths = const {},
    this.onlineLyricsResolvedPaths = const {},
    this.lyricsLoadingPaths = const {},
    this.favoritePaths = const {},
    this.searchQuery = '',
    this.isScanning = false,
    this.error,
    this.lowEffects = false,
  });

  final List<String> libraryFolders;
  final List<Track> tracks;
  final Map<String, Duration> durationByPath;
  final Map<String, Uint8List> coverBytesByPath;
  final Map<String, LyricsDocument> localLyricsByPath;
  final Map<String, LyricsDocument> onlineLyricsByPath;
  final Map<String, LyricsSourceType> preferredLyricsSourceByPath;
  final Set<String> localLyricsResolvedPaths;
  final Set<String> onlineLyricsResolvedPaths;
  final Set<String> lyricsLoadingPaths;
  final Set<String> favoritePaths;
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
    return filteredTracks
        .where((track) => favoritePaths.contains(track.path))
        .toList(growable: false);
  }

  Duration? durationOf(Track track) => durationByPath[track.path];

  Uint8List? coverBytesOf(Track track) => coverBytesByPath[track.path];

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

  List<LyricLine> lyricsOf(Track track) =>
      lyricsDocumentOf(track)?.lines ?? const <LyricLine>[];

  bool isLyricsLoading(Track track) => lyricsLoadingPaths.contains(track.path);

  bool get hasAnyLyricsData =>
      localLyricsByPath.isNotEmpty || onlineLyricsByPath.isNotEmpty;

  LibraryState copyWith({
    List<String>? libraryFolders,
    List<Track>? tracks,
    Map<String, Duration>? durationByPath,
    Map<String, Uint8List>? coverBytesByPath,
    Map<String, LyricsDocument>? localLyricsByPath,
    Map<String, LyricsDocument>? onlineLyricsByPath,
    Map<String, LyricsSourceType>? preferredLyricsSourceByPath,
    Set<String>? localLyricsResolvedPaths,
    Set<String>? onlineLyricsResolvedPaths,
    Set<String>? lyricsLoadingPaths,
    Set<String>? favoritePaths,
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
      searchQuery: searchQuery ?? this.searchQuery,
      isScanning: isScanning ?? this.isScanning,
      error: clearError ? null : (error ?? this.error),
      lowEffects: lowEffects ?? this.lowEffects,
    );
  }
}
