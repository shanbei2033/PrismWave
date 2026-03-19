import 'dart:typed_data';

import '../models/lyric_line.dart';
import '../models/track.dart';

class LibraryState {
  const LibraryState({
    this.libraryFolders = const [],
    this.tracks = const [],
    this.durationByPath = const {},
    this.coverBytesByPath = const {},
    this.lyricsByPath = const {},
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
  final Map<String, List<LyricLine>> lyricsByPath;
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

  List<LyricLine> lyricsOf(Track track) =>
      lyricsByPath[track.path] ?? const <LyricLine>[];

  LibraryState copyWith({
    List<String>? libraryFolders,
    List<Track>? tracks,
    Map<String, Duration>? durationByPath,
    Map<String, Uint8List>? coverBytesByPath,
    Map<String, List<LyricLine>>? lyricsByPath,
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
      lyricsByPath: lyricsByPath ?? this.lyricsByPath,
      favoritePaths: favoritePaths ?? this.favoritePaths,
      searchQuery: searchQuery ?? this.searchQuery,
      isScanning: isScanning ?? this.isScanning,
      error: clearError ? null : (error ?? this.error),
      lowEffects: lowEffects ?? this.lowEffects,
    );
  }
}
