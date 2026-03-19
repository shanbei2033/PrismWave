import 'dart:async';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:metadata_god/metadata_god.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/lyric_line.dart';
import '../models/track.dart';
import '../services/library_scanner.dart';
import '../services/lyrics_reader.dart';
import '../services/track_duration_resolver.dart';
import '../state/library_state.dart';

class LibraryController extends StateNotifier<LibraryState> {
  LibraryController() : super(const LibraryState()) {
    Future<void>.microtask(_loadInitialState);
  }

  int _metadataJobSeed = 0;

  static const _prefRootPath = 'library.rootPath';
  static const _prefLibraryFolders = 'library.folders';
  static const _prefFavorites = 'library.favorites';
  static const _prefLowEffects = 'ui.lowEffects';

  Future<void> _loadInitialState() async {
    final prefs = await SharedPreferences.getInstance();
    final legacyRoot = prefs.getString(_prefRootPath);
    final folders = prefs.getStringList(_prefLibraryFolders) ?? const [];
    final favorites = prefs.getStringList(_prefFavorites) ?? const [];
    final lowEffects = prefs.getBool(_prefLowEffects) ?? false;

    final resolvedFolders = folders.isNotEmpty
        ? folders
        : (legacyRoot == null || legacyRoot.isEmpty
              ? const <String>[]
              : [legacyRoot]);

    state = state.copyWith(
      libraryFolders: resolvedFolders,
      favoritePaths: favorites.toSet(),
      lowEffects: lowEffects,
      clearError: true,
    );

    if (resolvedFolders.isNotEmpty) {
      await _scanFolders(resolvedFolders, persistFolders: false);
    }
  }

  void setSearchQuery(String query) {
    state = state.copyWith(searchQuery: query);
  }

  Future<void> addMusicFolder() async {
    final selected = await FilePicker.platform.getDirectoryPath(
      dialogTitle: 'Select Music Folder',
    );
    if (selected == null) return;
    final nextFolders = <String>{
      ...state.libraryFolders,
      selected,
    }.toList(growable: false);
    await _scanFolders(nextFolders, persistFolders: true);
  }

  Future<void> removeMusicFolder(String path) async {
    final nextFolders = state.libraryFolders
        .where((folder) => folder != path)
        .toList(growable: false);
    await _scanFolders(nextFolders, persistFolders: true);
  }

  Future<void> rescanAllFolders() async {
    await _scanFolders(state.libraryFolders, persistFolders: false);
  }

  // Backward-compatible wrappers for earlier UI calls.
  Future<void> pickAndScanDirectory() => addMusicFolder();

  Future<void> scanDirectory(String path, {required bool persistRoot}) async {
    final nextFolders = <String>{
      ...state.libraryFolders,
      path,
    }.toList(growable: false);
    await _scanFolders(nextFolders, persistFolders: persistRoot);
  }

  Future<void> _scanFolders(
    List<String> folders, {
    required bool persistFolders,
  }) async {
    final job = ++_metadataJobSeed;
    state = state.copyWith(
      isScanning: true,
      libraryFolders: folders,
      clearError: true,
    );

    try {
      final scanned = await scanTracksFromRoots(folders);
      final nextDurations = <String, Duration>{};
      final nextCoverBytes = <String, Uint8List>{};
      final nextLyrics = <String, List<LyricLine>>{};
      final previousDurations = state.durationByPath;
      final previousCoverBytes = state.coverBytesByPath;
      final previousLyrics = state.lyricsByPath;
      for (final track in scanned) {
        final duration = previousDurations[track.path];
        final coverBytes = previousCoverBytes[track.path];
        final lyrics = previousLyrics[track.path];
        if (duration != null) {
          nextDurations[track.path] = duration;
        }
        if (coverBytes != null && coverBytes.isNotEmpty) {
          nextCoverBytes[track.path] = coverBytes;
        }
        if (lyrics != null && lyrics.isNotEmpty) {
          nextLyrics[track.path] = lyrics;
        }
      }

      state = state.copyWith(
        tracks: scanned,
        durationByPath: nextDurations,
        coverBytesByPath: nextCoverBytes,
        lyricsByPath: nextLyrics,
        isScanning: false,
      );

      if (persistFolders) {
        final prefs = await SharedPreferences.getInstance();
        await prefs.setStringList(_prefLibraryFolders, folders);
        if (folders.isNotEmpty) {
          await prefs.setString(_prefRootPath, folders.first);
        } else {
          await prefs.remove(_prefRootPath);
        }
      }

      unawaited(_enrichMetadata(scanned, job: job));
    } catch (error) {
      state = state.copyWith(isScanning: false, error: 'Scan failed: $error');
    }
  }

  Future<void> _enrichMetadata(List<Track> tracks, {required int job}) async {
    final trackPatch = <String, Track>{};
    final durationPatch = <String, Duration>{};
    final coverPatch = <String, Uint8List>{};

    for (final track in tracks) {
      if (job != _metadataJobSeed) return;

      try {
        final metadata = await MetadataGod.readMetadata(file: track.path);

        final mergedTrack = track.copyWith(
          title: _pickField(metadata.title, fallback: track.title),
          artist: _pickField(metadata.artist, fallback: track.artist),
          album: _pickField(metadata.album, fallback: track.album),
        );
        trackPatch[track.path] = mergedTrack;

        final duration = metadata.duration;
        if (duration != null && duration > Duration.zero) {
          durationPatch[track.path] = duration;
        }

        final pictureData = metadata.picture?.data;
        if (pictureData != null && pictureData.isNotEmpty) {
          coverPatch[track.path] = pictureData;
        }
      } catch (_) {
        // Metadata plugin may fail for unsupported/corrupted files.
      }

      if (trackPatch.length >= 10 ||
          durationPatch.length >= 10 ||
          coverPatch.length >= 10) {
        _applyMetadataPatch(
          job: job,
          trackPatch: trackPatch,
          durationPatch: durationPatch,
          coverPatch: coverPatch,
        );
        trackPatch.clear();
        durationPatch.clear();
        coverPatch.clear();
      }
    }

    _applyMetadataPatch(
      job: job,
      trackPatch: trackPatch,
      durationPatch: durationPatch,
      coverPatch: coverPatch,
    );

    await _resolveDurationsFor(tracks, job: job);
  }

  void _applyMetadataPatch({
    required int job,
    required Map<String, Track> trackPatch,
    required Map<String, Duration> durationPatch,
    required Map<String, Uint8List> coverPatch,
  }) {
    if (job != _metadataJobSeed) return;
    if (trackPatch.isEmpty && durationPatch.isEmpty && coverPatch.isEmpty) {
      return;
    }

    final mergedTracks = trackPatch.isEmpty
        ? state.tracks
        : state.tracks
              .map((track) => trackPatch[track.path] ?? track)
              .toList(growable: false);

    state = state.copyWith(
      tracks: mergedTracks,
      durationByPath: <String, Duration>{
        ...state.durationByPath,
        ...durationPatch,
      },
      coverBytesByPath: <String, Uint8List>{
        ...state.coverBytesByPath,
        ...coverPatch,
      },
    );
  }

  Future<void> _resolveDurationsFor(
    List<Track> tracks, {
    required int job,
  }) async {
    final unresolved = tracks
        .where((track) => !state.durationByPath.containsKey(track.path))
        .toList(growable: false);

    if (unresolved.isEmpty) return;

    await resolveTrackDurations(
      unresolved,
      onBatch: (batch) {
        if (job != _metadataJobSeed || batch.isEmpty) return false;
        state = state.copyWith(
          durationByPath: <String, Duration>{...state.durationByPath, ...batch},
        );
        return true;
      },
    );
  }

  String _pickField(String? primary, {required String fallback}) {
    final value = primary?.trim() ?? '';
    if (value.isEmpty) return fallback;
    return value;
  }

  bool isFavorite(Track track) => state.favoritePaths.contains(track.path);

  Future<void> ensureLyricsLoaded(Track track) async {
    if (state.lyricsByPath.containsKey(track.path)) return;

    final lyrics = await readLyricsForTrack(
      track.path,
      durationHint: state.durationByPath[track.path],
      title: track.title,
      artist: track.artist,
    );
    if (lyrics.isEmpty) return;

    state = state.copyWith(
      lyricsByPath: <String, List<LyricLine>>{
        ...state.lyricsByPath,
        track.path: lyrics,
      },
    );
  }

  Future<void> toggleFavorite(Track track) async {
    final next = state.favoritePaths.toSet();
    if (next.contains(track.path)) {
      next.remove(track.path);
    } else {
      next.add(track.path);
    }

    state = state.copyWith(favoritePaths: next);

    final prefs = await SharedPreferences.getInstance();
    await prefs.setStringList(_prefFavorites, next.toList(growable: false));
  }

  Future<void> setLowEffects(bool value) async {
    state = state.copyWith(lowEffects: value);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_prefLowEffects, value);
  }
}
