import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:metadata_god/metadata_god.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/lyrics_document.dart';
import '../models/lyrics_source_type.dart';
import '../models/online_lyrics_search_result.dart';
import '../models/track.dart';
import '../services/library_scanner.dart';
import '../services/lyrics_reader.dart';
import '../services/online_lyrics_service.dart';
import '../services/track_duration_resolver.dart';
import '../state/library_state.dart';

class LibraryController extends StateNotifier<LibraryState> {
  LibraryController() : super(const LibraryState()) {
    Future<void>.microtask(_loadInitialState);
  }

  int _metadataJobSeed = 0;
  final OnlineLyricsService _onlineLyricsService = OnlineLyricsService();

  static const _prefRootPath = 'library.rootPath';
  static const _prefLibraryFolders = 'library.folders';
  static const _prefFavorites = 'library.favorites';
  static const _prefLowEffects = 'ui.lowEffects';
  static const _prefPreferredLyricsSources = 'lyrics.preferredSources';

  Future<void> _loadInitialState() async {
    final prefs = await SharedPreferences.getInstance();
    final legacyRoot = prefs.getString(_prefRootPath);
    final folders = prefs.getStringList(_prefLibraryFolders) ?? const [];
    final favorites = prefs.getStringList(_prefFavorites) ?? const [];
    final lowEffects = prefs.getBool(_prefLowEffects) ?? false;
    final preferredLyricsSourceByPath = _decodePreferredLyricsSources(
      prefs.getString(_prefPreferredLyricsSources),
    );

    final resolvedFolders = folders.isNotEmpty
        ? folders
        : (legacyRoot == null || legacyRoot.isEmpty
              ? const <String>[]
              : [legacyRoot]);

    state = state.copyWith(
      libraryFolders: resolvedFolders,
      favoritePaths: favorites.toSet(),
      lowEffects: lowEffects,
      preferredLyricsSourceByPath: preferredLyricsSourceByPath,
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
    final nextFolders = <String>{...state.libraryFolders, selected}.toList(
      growable: false,
    );
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

  Future<void> pickAndScanDirectory() => addMusicFolder();

  Future<void> scanDirectory(String path, {required bool persistRoot}) async {
    final nextFolders = <String>{...state.libraryFolders, path}.toList(
      growable: false,
    );
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
      final activePaths = scanned.map((track) => track.path).toSet();

      final nextDurations = <String, Duration>{};
      final nextCoverBytes = <String, Uint8List>{};
      final nextLocalLyrics = <String, LyricsDocument>{};
      final nextOnlineLyrics = <String, LyricsDocument>{};
      final nextPreferredSources = <String, LyricsSourceType>{
        ...state.preferredLyricsSourceByPath,
      };
      final nextLocalResolved = <String>{};
      final nextOnlineResolved = <String>{};

      for (final track in scanned) {
        final path = track.path;
        final duration = state.durationByPath[path];
        final coverBytes = state.coverBytesByPath[path];
        final localLyrics = state.localLyricsByPath[path];
        final onlineLyrics = state.onlineLyricsByPath[path];
        final preferred = state.preferredLyricsSourceByPath[path];

        if (duration != null) {
          nextDurations[path] = duration;
        }
        if (coverBytes != null && coverBytes.isNotEmpty) {
          nextCoverBytes[path] = coverBytes;
        }
        if (localLyrics != null && !localLyrics.isEmpty) {
          nextLocalLyrics[path] = localLyrics;
        }
        if (onlineLyrics != null && !onlineLyrics.isEmpty) {
          nextOnlineLyrics[path] = onlineLyrics;
        }
        if (preferred != null) {
          nextPreferredSources[path] = preferred;
        }
        if (state.localLyricsResolvedPaths.contains(path)) {
          nextLocalResolved.add(path);
        }
        if (state.onlineLyricsResolvedPaths.contains(path)) {
          nextOnlineResolved.add(path);
        }
      }

      state = state.copyWith(
        tracks: scanned,
        durationByPath: nextDurations,
        coverBytesByPath: nextCoverBytes,
        localLyricsByPath: nextLocalLyrics,
        onlineLyricsByPath: nextOnlineLyrics,
        preferredLyricsSourceByPath: nextPreferredSources,
        localLyricsResolvedPaths: nextLocalResolved,
        onlineLyricsResolvedPaths: nextOnlineResolved,
        lyricsLoadingPaths: state.lyricsLoadingPaths
            .where(activePaths.contains)
            .toSet(),
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
    await _ensureLocalLyricsLoaded(track);

    final preferred = state.preferredLyricsSourceOf(track);
    final local = state.localLyricsByPath[track.path];
    if (preferred == LyricsSourceType.online) {
      await _ensureOnlineLyricsLoaded(
        track,
        autoSelectOnline: true,
        forceReload: false,
      );
      return;
    }

    if (local != null && !local.isEmpty) return;

    await _setPreferredLyricsSource(track.path, LyricsSourceType.online);

    await _ensureOnlineLyricsLoaded(
      track,
      autoSelectOnline: true,
      forceReload: false,
    );
  }

  Future<void> selectLyricsSource(Track track, LyricsSourceType source) async {
    await _setPreferredLyricsSource(track.path, source);

    if (source == LyricsSourceType.local) {
      await _ensureLocalLyricsLoaded(track);
      final local = state.localLyricsByPath[track.path];
      if (local == null || local.isEmpty) {
        await _setPreferredLyricsSource(track.path, LyricsSourceType.online);
        await _ensureOnlineLyricsLoaded(
          track,
          autoSelectOnline: true,
          forceReload: false,
        );
      }
      return;
    }

    await _ensureOnlineLyricsLoaded(
      track,
      autoSelectOnline: true,
      forceReload: true,
    );
  }

  Future<List<OnlineLyricsSearchResult>> searchOnlineLyrics(
    Track track,
    String query,
  ) async {
    final normalized = query.trim().isEmpty ? track.title : query.trim();
    return _onlineLyricsService.searchLyricsForTrack(
      track,
      query: normalized,
      durationHint: state.durationByPath[track.path],
    );
  }

  Future<void> applyManualOnlineLyricsSelection(
    Track track,
    OnlineLyricsSearchResult result,
  ) async {
    final raw = result.preferredRawLyrics;
    if (raw == null || raw.trim().isEmpty) {
      state = state.copyWith(error: 'Selected lyric item is empty.');
      return;
    }

    final parsed = parseLyricsDocument(
      raw,
      durationHint: state.durationByPath[track.path],
    );
    if (parsed == null || parsed.isEmpty) {
      state = state.copyWith(error: 'Selected lyric item cannot be parsed.');
      return;
    }

    final document = LyricsDocument(
      lines: parsed.lines,
      isSynced: parsed.isSynced,
      rawText: raw,
      provider: result.provider,
      remoteId: result.id,
      title: result.title,
      artist: result.artist,
      album: result.album,
      byteSize: result.byteSize,
    );

    await _onlineLyricsService.saveCachedLyricsForTrack(track, document);
    _storeOnlineLyricsDocument(track, document, selectOnline: true);
  }

  Future<void> _ensureLocalLyricsLoaded(Track track) async {
    if (state.localLyricsResolvedPaths.contains(track.path)) return;

    final document = await readLocalLyricsDocumentForTrack(
      track.path,
      durationHint: state.durationByPath[track.path],
      title: track.title,
      artist: track.artist,
    );

    final nextResolved = <String>{...state.localLyricsResolvedPaths, track.path};
    final nextLocal = <String, LyricsDocument>{...state.localLyricsByPath};
    if (document != null && !document.isEmpty) {
      nextLocal[track.path] = document;
    } else {
      nextLocal.remove(track.path);
    }

    state = state.copyWith(
      localLyricsByPath: nextLocal,
      localLyricsResolvedPaths: nextResolved,
    );
  }

  Future<void> _ensureOnlineLyricsLoaded(
    Track track, {
    required bool autoSelectOnline,
    required bool forceReload,
  }) async {
    if (state.lyricsLoadingPaths.contains(track.path)) return;

    if (!forceReload && state.onlineLyricsResolvedPaths.contains(track.path)) {
      if (autoSelectOnline &&
          state.onlineLyricsByPath[track.path] != null &&
          !state.onlineLyricsByPath[track.path]!.isEmpty) {
        await _setPreferredLyricsSource(track.path, LyricsSourceType.online);
      }
      return;
    }

    _setLyricsLoading(track.path, true);
    try {
      final durationHint = state.durationByPath[track.path];
      final cached = await _onlineLyricsService.loadCachedLyricsForTrack(
        track,
        durationHint: durationHint,
      );
      if (cached != null && !cached.isEmpty) {
        _storeOnlineLyricsDocument(track, cached, selectOnline: autoSelectOnline);
        return;
      }

      final fetched = await _onlineLyricsService.fetchBestLyricsForTrack(
        track,
        durationHint: durationHint,
      );
      if (fetched != null && !fetched.isEmpty) {
        await _onlineLyricsService.saveCachedLyricsForTrack(track, fetched);
        _storeOnlineLyricsDocument(
          track,
          fetched,
          selectOnline: autoSelectOnline,
        );
        return;
      }

      state = state.copyWith(
        onlineLyricsResolvedPaths: <String>{
          ...state.onlineLyricsResolvedPaths,
          track.path,
        },
      );
    } finally {
      _setLyricsLoading(track.path, false);
    }
  }

  void _storeOnlineLyricsDocument(
    Track track,
    LyricsDocument document, {
    required bool selectOnline,
  }) {
    if (document.isEmpty) return;
    if (selectOnline) {
      _setPreferredLyricsSourceInState(track.path, LyricsSourceType.online);
      unawaited(_persistPreferredLyricsSources());
    }
    state = state.copyWith(
      onlineLyricsByPath: <String, LyricsDocument>{
        ...state.onlineLyricsByPath,
        track.path: document,
      },
      onlineLyricsResolvedPaths: <String>{
        ...state.onlineLyricsResolvedPaths,
        track.path,
      },
    );
  }

  void _setLyricsLoading(String path, bool loading) {
    final next = <String>{...state.lyricsLoadingPaths};
    if (loading) {
      next.add(path);
    } else {
      next.remove(path);
    }
    state = state.copyWith(lyricsLoadingPaths: next);
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

  void _setPreferredLyricsSourceInState(String path, LyricsSourceType source) {
    state = state.copyWith(
      preferredLyricsSourceByPath: <String, LyricsSourceType>{
        ...state.preferredLyricsSourceByPath,
        path: source,
      },
    );
  }

  Future<void> _setPreferredLyricsSource(
    String path,
    LyricsSourceType source,
  ) async {
    _setPreferredLyricsSourceInState(path, source);
    await _persistPreferredLyricsSources();
  }

  Future<void> _persistPreferredLyricsSources() async {
    final prefs = await SharedPreferences.getInstance();
    final encoded = <String, String>{
      for (final entry in state.preferredLyricsSourceByPath.entries)
        if (entry.key.isNotEmpty) entry.key: entry.value.id,
    };
    await prefs.setString(_prefPreferredLyricsSources, jsonEncode(encoded));
  }

  Map<String, LyricsSourceType> _decodePreferredLyricsSources(String? raw) {
    if (raw == null || raw.trim().isEmpty) return const {};

    try {
      final decoded = jsonDecode(raw);
      if (decoded is! Map) return const {};

      final result = <String, LyricsSourceType>{};
      for (final entry in decoded.entries) {
        final path = entry.key?.toString() ?? '';
        if (path.isEmpty) continue;
        result[path] = LyricsSourceType.fromId(entry.value?.toString());
      }
      return result;
    } catch (_) {
      return const {};
    }
  }
}
