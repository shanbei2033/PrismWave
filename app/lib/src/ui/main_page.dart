import 'dart:async';
import 'dart:ffi';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:ffi/ffi.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_svg/flutter_svg.dart';
import 'package:win32/win32.dart';

import '../i18n/app_strings.dart';
import '../models/audio_file_details.dart';
import '../models/app_language.dart';
import '../models/audio_output_mode.dart';
import '../models/online_recommendation.dart';
import '../models/playback_backend_kind.dart';
import '../models/playback_mode.dart';
import '../models/release_update_status.dart';
import '../models/top_bar_idle_mode.dart';
import '../models/track.dart';
import '../providers.dart';
import '../services/audio_file_details_service.dart';
import '../state/library_state.dart';
import '../state/playback_state.dart';
import 'fullplay_page.dart';
import 'glass_panel.dart';
import 'hits_availability.dart';
import 'hits_transition_page.dart';
import 'middle_click_autoscroll.dart';
import 'online_album_detail_panel.dart';
import 'online_home_panel.dart';
import 'online_search_panel.dart';
import 'online_top_playlist_panel.dart';
import 'prismwave_theme.dart';
import 'window_top_bar.dart';

enum MainSection { home, search, library, albums, artists, favorites, settings }

Future<void> _openExternalUrl(String url) async {
  final trimmed = url.trim();
  if (trimmed.isEmpty) return;
  try {
    await Process.start('cmd.exe', [
      '/c',
      'start',
      '',
      trimmed,
    ], mode: ProcessStartMode.detached);
  } catch (_) {
    await Process.start('rundll32', [
      'url.dll,FileProtocolHandler',
      trimmed,
    ], mode: ProcessStartMode.detached);
  }
}

class PrismWaveHomePage extends ConsumerStatefulWidget {
  const PrismWaveHomePage({super.key});

  @override
  ConsumerState<PrismWaveHomePage> createState() => _PrismWaveHomePageState();
}

class _PrismWaveHomePageState extends ConsumerState<PrismWaveHomePage> {
  final _searchController = TextEditingController();
  MainSection _section = MainSection.home;
  String? _selectedAlbum;
  String? _selectedArtist;
  bool _topPlaylistOpen = false;
  OnlineAlbumCard? _openAlbumCard;
  bool _showPlaybackQueue = false;

  @override
  void initState() {
    super.initState();
    _searchController.addListener(() {
      ref.read(libraryProvider.notifier).setSearchQuery(_searchController.text);
    });
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  void _hidePlaybackQueue({bool rebuild = true}) {
    if (!_showPlaybackQueue) return;
    if (rebuild && mounted) {
      setState(() {
        _showPlaybackQueue = false;
      });
      return;
    }
    _showPlaybackQueue = false;
  }

  void _togglePlaybackQueue(PlaybackState playback) {
    if (playback.currentPlaylist.isEmpty) return;
    if (!mounted) {
      _showPlaybackQueue = !_showPlaybackQueue;
      return;
    }
    setState(() {
      _showPlaybackQueue = !_showPlaybackQueue;
    });
  }

  void _syncPlaybackQueueWithPlaybackState(
    PlaybackState _,
    PlaybackState next,
  ) {
    final shouldHideQueue = _showPlaybackQueue && next.currentPlaylist.isEmpty;
    if (shouldHideQueue) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        _hidePlaybackQueue();
      });
    }
  }

  Future<void> _openTrackDetails({
    required BuildContext context,
    required Track track,
    required Duration? duration,
    required Uint8List? coverBytes,
  }) async {
    final navigator = Navigator.of(context);
    await navigator.push(
      MaterialPageRoute<void>(
        builder: (_) => _TrackDetailsPage(
          track: track,
          duration: duration,
          coverBytes: coverBytes,
          onReveal: () => _revealTrackInExplorer(track.path),
        ),
      ),
    );
  }

  Future<void> _revealTrackInExplorer(String path) async {
    final resolvedPath = File(path).absolute.path;
    final normalized = resolvedPath.replaceAll('/', '\\');
    final file = File(normalized);

    if (Platform.isWindows) {
      if (file.existsSync()) {
        final launched = _shellSelectFileInExplorer(normalized);
        if (launched) return;
      }

      final parentDirectory = file.parent;
      final fallbackPath = parentDirectory.path.replaceAll('/', '\\');
      if (parentDirectory.existsSync()) {
        final openedDirectory = _shellOpenPath(fallbackPath);
        if (openedDirectory) return;
      }
    }

    await _tryLaunchExplorer([], runInShell: true);
  }

  Future<bool> _tryLaunchExplorer(
    List<String> arguments, {
    bool runInShell = false,
  }) async {
    try {
      await Process.start('explorer.exe', arguments, runInShell: runInShell);
      return true;
    } catch (_) {
      return false;
    }
  }

  bool _shellSelectFileInExplorer(String normalizedPath) {
    return using((arena) {
      final operation = 'open'.toNativeUtf16(allocator: arena);
      final executable = 'explorer.exe'.toNativeUtf16(allocator: arena);
      final parameters = '/select,"$normalizedPath"'.toNativeUtf16(
        allocator: arena,
      );
      final result = ShellExecute(
        0,
        operation,
        executable,
        parameters,
        nullptr,
        SW_SHOWNORMAL,
      );
      return result > 32;
    });
  }

  bool _shellOpenPath(String normalizedPath) {
    return using((arena) {
      final operation = 'open'.toNativeUtf16(allocator: arena);
      final target = normalizedPath.toNativeUtf16(allocator: arena);
      final result = ShellExecute(
        0,
        operation,
        target,
        nullptr,
        nullptr,
        SW_SHOWNORMAL,
      );
      return result > 32;
    });
  }

  @override
  Widget build(BuildContext context) {
    ref.listen<LibraryState>(libraryProvider, (previous, next) {
      if (next.error != null && previous?.error != next.error && mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(next.error!)));
      }
    });

    ref.listen<bool>(appSettingsProvider.select((s) => s.onlineModeEnabled), (
      previous,
      next,
    ) {
      if (!next && mounted) {
        if (_section == MainSection.home || _section == MainSection.search) {
          setState(() {
            _section = MainSection.library;
          });
        }
      }
    });

    ref.listen<PlaybackState>(playbackProvider, (previous, next) {
      if (next.error != null && previous?.error != next.error && mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(next.error!)));
      }
      if (next.currentTrack?.id != previous?.currentTrack?.id &&
          next.currentTrack != null) {
        unawaited(
          ref
              .read(libraryProvider.notifier)
              .ensureLyricsLoaded(
                next.currentTrack!,
                durationHint: next.duration,
              ),
        );
      }
      if (previous != null) {
        _syncPlaybackQueueWithPlaybackState(previous, next);
      }
    });

    final library = ref.watch(libraryProvider);
    final playback = ref.watch(playbackProvider);
    final language = ref.watch(appSettingsProvider).language;
    final t = AppStrings(language);

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Stack(
        children: [
          Positioned.fill(
            child: DecoratedBox(
              decoration: const BoxDecoration(
                gradient: PrismWaveTheme.appGradient,
              ),
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
                child: Column(
                  children: [
                    Expanded(
                      child: Row(
                        children: [
                          SizedBox(
                            width: 260,
                            child: _buildSidebar(
                              library: library,
                              playback: playback,
                              t: t,
                            ),
                          ),
                          const SizedBox(width: 14),
                          Expanded(
                            child: Padding(
                              padding: const EdgeInsets.only(top: 32),
                              child: AnimatedSwitcher(
                                duration: const Duration(milliseconds: 280),
                                switchInCurve: Curves.easeOutCubic,
                                switchOutCurve: Curves.easeInCubic,
                                transitionBuilder: (child, animation) {
                                  return FadeTransition(
                                    opacity: animation,
                                    child: SlideTransition(
                                      position: Tween<Offset>(
                                        begin: const Offset(0.08, 0),
                                        end: Offset.zero,
                                      ).animate(animation),
                                      child: child,
                                    ),
                                  );
                                },
                                child: KeyedSubtree(
                                  key: ValueKey(
                                    '${_section.name}'
                                    '|${_topPlaylistOpen ? 'top' : 'root'}'
                                    '|${_openAlbumCard?.canonicalKey ?? ''}'
                                    '|${_selectedAlbum ?? ''}'
                                    '|${_selectedArtist ?? ''}',
                                  ),
                                  child: _buildSectionPanel(
                                    library: library,
                                    playback: playback,
                                    t: t,
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 14),
                    _buildPlayerBar(playback: playback, library: library, t: t),
                  ],
                ),
              ),
            ),
          ),
          Positioned(left: 0, top: 0, right: 0, child: const WindowTopBar()),
        ],
      ),
    );
  }

  Widget _buildSidebar({
    required LibraryState library,
    required PlaybackState playback,
    required AppStrings t,
  }) {
    final showingQueue = _showPlaybackQueue;

    return GlassPanel(
      lowEffects: library.lowEffects,
      radius: PrismWaveTheme.panelRadius,
      padding: const EdgeInsets.fromLTRB(14, 16, 14, 14),
      child: AnimatedSwitcher(
        duration: const Duration(milliseconds: 220),
        switchInCurve: Curves.easeOutCubic,
        switchOutCurve: Curves.easeInCubic,
        transitionBuilder: (child, animation) {
          return FadeTransition(
            opacity: animation,
            child: SlideTransition(
              position: Tween<Offset>(
                begin: const Offset(0.04, 0),
                end: Offset.zero,
              ).animate(animation),
              child: child,
            ),
          );
        },
        child: showingQueue
            ? KeyedSubtree(
                key: const ValueKey('sidebar-playback-queue'),
                child: _buildPlaybackQueueSidebar(
                  library: library,
                  playback: playback,
                  t: t,
                ),
              )
            : KeyedSubtree(
                key: const ValueKey('sidebar-navigation'),
                child: _buildNavigationSidebar(library: library, t: t),
              ),
      ),
    );
  }

  Widget _buildNavigationSidebar({
    required LibraryState library,
    required AppStrings t,
  }) {
    final onlineEnabled = ref.watch(
      appSettingsProvider.select((s) => s.onlineModeEnabled),
    );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: const Text(
                'PrismWave',
                style: TextStyle(
                  color: PrismWaveTheme.textPrimary,
                  fontSize: 23,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 18),
        if (onlineEnabled) ...[
          _navButton(
            section: MainSection.home,
            icon: Icons.explore_rounded,
            label: t.navHome,
          ),
          const SizedBox(height: 8),
          _navButton(
            section: MainSection.search,
            icon: Icons.search_rounded,
            label: t.navSearch,
          ),
          const SizedBox(height: 8),
        ],
        _navButton(
          section: MainSection.library,
          icon: Icons.library_music_rounded,
          label: t.library,
        ),
        const SizedBox(height: 8),
        _navButton(
          section: MainSection.albums,
          icon: Icons.album_rounded,
          label: t.albums,
        ),
        const SizedBox(height: 8),
        _navButton(
          section: MainSection.artists,
          icon: Icons.mic_rounded,
          label: t.artists,
        ),
        const SizedBox(height: 8),
        _navButton(
          section: MainSection.favorites,
          icon: Icons.favorite_rounded,
          label: t.favorites,
        ),
        const SizedBox(height: 8),
        _buildHitsNavButton(t),
        const Spacer(),
        Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${t.folders}: ${library.libraryFolders.length}',
                    style: TextStyle(
                      color: PrismWaveTheme.textSecondary.withValues(
                        alpha: 0.78,
                      ),
                      fontSize: 12,
                    ),
                  ),
                  Text(
                    '${t.tracks}: ${library.tracks.length}',
                    style: TextStyle(
                      color: PrismWaveTheme.textSecondary.withValues(
                        alpha: 0.78,
                      ),
                      fontSize: 12,
                    ),
                  ),
                  Text(
                    '${t.favoriteCountLabel}: ${library.favoritePaths.length}',
                    style: TextStyle(
                      color: PrismWaveTheme.textSecondary.withValues(
                        alpha: 0.78,
                      ),
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            _buildSettingsActionButton(t),
          ],
        ),
      ],
    );
  }

  Widget _buildHitsNavButton(AppStrings t) {
    return SizedBox(
      width: double.infinity,
      child: TextButton.icon(
        onPressed: _openHitsTransition,
        style: PrismWaveTheme.rectangularButtonStyle(),
        icon: SvgPicture.asset(
          'assets/icons/hits.svg',
          width: 19,
          height: 19,
          colorFilter: const ColorFilter.mode(
            PrismWaveTheme.textSecondary,
            BlendMode.srcIn,
          ),
        ),
        label: const Align(
          alignment: Alignment.centerLeft,
          child: Text(
            'HITS',
            style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700),
          ),
        ),
      ),
    );
  }

  Widget _buildSettingsActionButton(AppStrings t) {
    return Tooltip(
      message: t.settings,
      child: SizedBox(
        width: 46,
        height: 42,
        child: TextButton(
          onPressed: _openSettings,
          style: PrismWaveTheme.rectangularButtonStyle(
            selected: _section == MainSection.settings,
            padding: EdgeInsets.zero,
          ),
          child: SvgPicture.asset(
            'assets/icons/settings.svg',
            width: 19,
            height: 19,
            colorFilter: ColorFilter.mode(
              _section == MainSection.settings
                  ? PrismWaveTheme.accentSoft
                  : PrismWaveTheme.textSecondary,
              BlendMode.srcIn,
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildPlaybackQueueSidebar({
    required LibraryState library,
    required PlaybackState playback,
    required AppStrings t,
  }) {
    final playbackCtrl = ref.read(playbackProvider.notifier);
    final playlist = playback.currentPlaylist;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    t.playbackQueue,
                    style: const TextStyle(
                      fontSize: 22,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    t.trackCountText(playlist.length),
                    style: TextStyle(
                      color: Colors.white.withValues(alpha: 0.68),
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
            IconButton(
              tooltip: t.back,
              onPressed: _hidePlaybackQueue,
              icon: const Icon(Icons.close_rounded),
            ),
          ],
        ),
        const SizedBox(height: 14),
        Expanded(
          child: playlist.isEmpty
              ? Center(
                  child: Text(
                    t.noActivePlaylist,
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: Colors.white.withValues(alpha: 0.68),
                    ),
                  ),
                )
              : MiddleClickAutoScrollView(
                  builder: (context, controller) => ReorderableListView.builder(
                    buildDefaultDragHandles: false,
                    padding: EdgeInsets.zero,
                    scrollController: controller,
                    proxyDecorator: (child, _, animation) =>
                        _buildReorderProxy(child, animation, radius: 12),
                    onReorder: playbackCtrl.reorderQueue,
                    itemCount: playlist.length,
                    itemBuilder: (_, index) {
                      final track = playlist[index];
                      final active = playback.currentTrack?.id == track.id;
                      final coverBytes = library.coverBytesOf(track);

                      return ReorderableDelayedDragStartListener(
                        key: ValueKey('queue-track-${track.path}'),
                        index: index,
                        child: Padding(
                          padding: const EdgeInsets.only(bottom: 6),
                          child: _PlaybackQueueTrackTile(
                            track: track,
                            index: index,
                            isActive: active,
                            coverBytes: coverBytes,
                            onTap: () =>
                                playbackCtrl.playFromCurrentQueue(track),
                            onRemove: () =>
                                playbackCtrl.removeFromQueueAt(index),
                          ),
                        ),
                      );
                    },
                  ),
                ),
        ),
        const SizedBox(height: 10),
        Text(
          switch (playback.playbackMode) {
            PlaybackMode.loop => t.listLoop,
            PlaybackMode.single => t.singleLoop,
            PlaybackMode.shuffle => t.shuffle,
          },
          style: TextStyle(
            color: Colors.white.withValues(alpha: 0.62),
            fontSize: 12,
          ),
        ),
      ],
    );
  }

  Widget _navButton({
    required MainSection section,
    required IconData icon,
    required String label,
  }) {
    final selected = _section == section;

    return SizedBox(
      width: double.infinity,
      child: TextButton.icon(
        onPressed: () {
          setState(() {
            _section = section;
            _selectedAlbum = null;
            _selectedArtist = null;
            _topPlaylistOpen = false;
            _openAlbumCard = null;
          });
        },
        style: PrismWaveTheme.rectangularButtonStyle(selected: selected),
        icon: Icon(icon, size: 19),
        label: Align(
          alignment: Alignment.centerLeft,
          child: Text(
            label,
            style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ),
    );
  }

  Widget _buildSectionPanel({
    required LibraryState library,
    required PlaybackState playback,
    required AppStrings t,
  }) {
    switch (_section) {
      case MainSection.home:
        if (_openAlbumCard != null) {
          return OnlineAlbumDetailPanel(
            t: t,
            album: _openAlbumCard!,
            onBack: () {
              setState(() {
                _openAlbumCard = null;
              });
            },
          );
        }
        if (_topPlaylistOpen) {
          return OnlineTopPlaylistPanel(
            t: t,
            onBack: () {
              setState(() {
                _topPlaylistOpen = false;
              });
            },
          );
        }
        return OnlineHomePanel(
          t: t,
          onOpenTopPlaylist: () {
            setState(() {
              _topPlaylistOpen = true;
            });
          },
          onOpenAlbum: (album) {
            setState(() {
              _openAlbumCard = album;
            });
          },
        );
      case MainSection.search:
        return OnlineSearchPanel(t: t);
      case MainSection.library:
        return _buildTracksPanel(
          library: library,
          playback: playback,
          title: t.musicLibrary,
          tracks: library.filteredTracks,
          playbackContextTracks: library.tracks,
          forceLibraryContext: true,
          emptyMessage: library.libraryFolders.isEmpty
              ? t.addFolderFirst
              : t.noTrackMatch,
          t: t,
        );
      case MainSection.favorites:
        return _buildTracksPanel(
          library: library,
          playback: playback,
          title: t.favorites,
          tracks: library.favoriteTracks,
          showPlayAllButton: true,
          onPlayAll: library.favoriteTracks.isEmpty
              ? null
              : () => ref
                    .read(playbackProvider.notifier)
                    .playFromPlaylist(
                      library.favoriteTracks.first,
                      library.favoriteTracks,
                    ),
          emptyMessage: t.noFavoriteTracks,
          t: t,
        );
      case MainSection.albums:
        if (_selectedAlbum != null) {
          return _buildAlbumTracksPanel(
            library: library,
            playback: playback,
            album: _selectedAlbum!,
            t: t,
          );
        }
        return _buildAlbumsPanel(library: library, t: t);
      case MainSection.artists:
        if (_selectedArtist != null) {
          return _buildArtistTracksPanel(
            library: library,
            playback: playback,
            artist: _selectedArtist!,
            t: t,
          );
        }
        return _buildArtistsPanel(library: library, t: t);
      case MainSection.settings:
        return _SettingsPanel(
          onClose: () {
            setState(() {
              _section = MainSection.library;
            });
          },
        );
    }
  }

  Widget _buildTracksPanel({
    required LibraryState library,
    required PlaybackState playback,
    required AppStrings t,
    required String title,
    required List<Track> tracks,
    List<Track>? playbackContextTracks,
    bool forceLibraryContext = false,
    bool showPlayAllButton = false,
    VoidCallback? onPlayAll,
    required String emptyMessage,
  }) {
    final libraryCtrl = ref.read(libraryProvider.notifier);
    final playbackCtrl = ref.read(playbackProvider.notifier);
    final playbackContext = playbackContextTracks ?? tracks;
    final useLibraryContext =
        forceLibraryContext && playbackContextTracks != null;
    final canReorder = tracks.length > 1;

    return GlassPanel(
      lowEffects: library.lowEffects,
      child: Column(
        children: [
          Row(
            children: [
              Text(
                title,
                style: const TextStyle(
                  fontSize: 20,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(width: 12),
              Text(
                t.trackCountText(tracks.length),
                style: TextStyle(color: Colors.white.withValues(alpha: 0.72)),
              ),
              const Spacer(),
              if (showPlayAllButton) ...[
                _GlassPlayAllButton(
                  label: t.playAll,
                  enabled: tracks.isNotEmpty,
                  onPressed: tracks.isEmpty ? null : onPlayAll,
                ),
                if (library.isScanning) const SizedBox(width: 12),
              ],
              if (library.isScanning)
                const SizedBox(
                  width: 20,
                  height: 20,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
            ],
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _searchController,
            decoration: InputDecoration(
              hintText: t.searchTrackArtistAlbum,
              prefixIcon: const Icon(Icons.search_rounded),
              suffixIcon: library.searchQuery.isEmpty
                  ? null
                  : IconButton(
                      onPressed: _searchController.clear,
                      icon: const Icon(Icons.clear_rounded),
                    ),
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(14),
              ),
            ),
          ),
          const SizedBox(height: 12),
          _buildTrackHeader(t: t),
          const SizedBox(height: 8),
          Expanded(
            child: tracks.isEmpty
                ? Center(
                    child: Text(
                      emptyMessage,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.68),
                      ),
                    ),
                  )
                : MiddleClickAutoScrollView(
                    builder: (context, controller) => ReorderableListView.builder(
                      buildDefaultDragHandles: false,
                      padding: EdgeInsets.zero,
                      scrollController: controller,
                      proxyDecorator: (child, _, animation) =>
                          _buildReorderProxy(child, animation, radius: 10),
                      onReorder: (oldIndex, newIndex) {
                        if (forceLibraryContext) {
                          libraryCtrl.reorderLibraryTracks(
                            visibleTracks: tracks,
                            oldIndex: oldIndex,
                            newIndex: newIndex,
                          );
                          return;
                        }
                        libraryCtrl.reorderFavoriteTracks(
                          visibleTracks: tracks,
                          oldIndex: oldIndex,
                          newIndex: newIndex,
                        );
                      },
                      itemCount: tracks.length,
                      itemBuilder: (_, index) {
                        final track = tracks[index];
                        final active = playback.currentTrack?.id == track.id;
                        final isFavorite = libraryCtrl.isFavorite(track);
                        final duration = library.durationOf(track);
                        final coverBytes = library.coverBytesOf(track);

                        return ReorderableDelayedDragStartListener(
                          key: ValueKey('track-list-row-${track.path}'),
                          index: index,
                          enabled: canReorder,
                          child: Padding(
                            padding: const EdgeInsets.symmetric(vertical: 2),
                            child: GestureDetector(
                              behavior: HitTestBehavior.opaque,
                              onSecondaryTapDown: (_) => _openTrackDetails(
                                context: context,
                                track: track,
                                duration: duration,
                                coverBytes: coverBytes,
                              ),
                              child: Material(
                                color: active
                                    ? const Color(
                                        0xFF39C0FF,
                                      ).withValues(alpha: 0.16)
                                    : Colors.transparent,
                                borderRadius: BorderRadius.circular(10),
                                child: InkWell(
                                  borderRadius: BorderRadius.circular(10),
                                  onTap: () => useLibraryContext
                                      ? playbackCtrl.playFromLibrary(
                                          track,
                                          playbackContext,
                                        )
                                      : playbackCtrl.playFromPlaylist(
                                          track,
                                          playbackContext,
                                        ),
                                  child: SizedBox(
                                    height: 56,
                                    child: Row(
                                      children: [
                                        const SizedBox(width: 10),
                                        SizedBox(
                                          width: 52,
                                          child: _TrackCover(
                                            track: track,
                                            isActive: active,
                                            coverBytes: coverBytes,
                                          ),
                                        ),
                                        const SizedBox(width: 12),
                                        Expanded(
                                          flex: 5,
                                          child: Text(
                                            track.title,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: const TextStyle(
                                              fontWeight: FontWeight.w600,
                                            ),
                                          ),
                                        ),
                                        Expanded(
                                          flex: 3,
                                          child: Text(
                                            track.artist,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: TextStyle(
                                              color: Colors.white.withValues(
                                                alpha: 0.75,
                                              ),
                                            ),
                                          ),
                                        ),
                                        SizedBox(
                                          width: 84,
                                          child: Text(
                                            _formatDuration(duration),
                                            textAlign: TextAlign.right,
                                            style: TextStyle(
                                              color: Colors.white.withValues(
                                                alpha: 0.82,
                                              ),
                                            ),
                                          ),
                                        ),
                                        const SizedBox(width: 4),
                                        IconButton(
                                          tooltip: isFavorite
                                              ? t.uncollect
                                              : t.collect,
                                          onPressed: () =>
                                              libraryCtrl.toggleFavorite(track),
                                          icon: Icon(
                                            isFavorite
                                                ? Icons.favorite_rounded
                                                : Icons.favorite_border_rounded,
                                            color: isFavorite
                                                ? const Color(0xFF39C0FF)
                                                : Colors.white.withValues(
                                                    alpha: 0.78,
                                                  ),
                                            size: 18,
                                          ),
                                        ),
                                        const SizedBox(width: 6),
                                      ],
                                    ),
                                  ),
                                ),
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  ),
          ),
        ],
      ),
    );
  }

  Widget _buildTrackHeader({required AppStrings t}) {
    return Container(
      height: 40,
      padding: const EdgeInsets.symmetric(horizontal: 12),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(10),
        color: Colors.white.withValues(alpha: 0.06),
      ),
      child: Row(
        children: [
          SizedBox(
            width: 52,
            child: Text(
              t.cover,
              style: TextStyle(
                color: Colors.white.withValues(alpha: 0.62),
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            flex: 5,
            child: Text(
              t.trackName,
              style: TextStyle(
                color: Colors.white.withValues(alpha: 0.62),
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          Expanded(
            flex: 3,
            child: Text(
              t.singer,
              style: TextStyle(
                color: Colors.white.withValues(alpha: 0.62),
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          SizedBox(
            width: 84,
            child: Text(
              t.duration,
              textAlign: TextAlign.right,
              style: TextStyle(
                color: Colors.white.withValues(alpha: 0.62),
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          const SizedBox(width: 38),
        ],
      ),
    );
  }

  Widget _buildAlbumsPanel({
    required LibraryState library,
    required AppStrings t,
  }) {
    final groups = <String, List<Track>>{};
    for (final track in library.filteredTracks) {
      groups.putIfAbsent(track.album, () => <Track>[]).add(track);
    }
    final albums = groups.entries.toList(growable: false)
      ..sort((a, b) => a.key.compareTo(b.key));

    return GlassPanel(
      lowEffects: library.lowEffects,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            t.albums,
            style: TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _searchController,
            decoration: InputDecoration(
              hintText: t.searchAlbumArtistTrack,
              prefixIcon: const Icon(Icons.search_rounded),
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(14),
              ),
            ),
          ),
          const SizedBox(height: 14),
          Expanded(
            child: albums.isEmpty
                ? Center(
                    child: Text(
                      t.noAlbumMatch,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.68),
                      ),
                    ),
                  )
                : MiddleClickAutoScrollView(
                    builder: (context, controller) => GridView.builder(
                      controller: controller,
                      itemCount: albums.length,
                      gridDelegate:
                          const SliverGridDelegateWithMaxCrossAxisExtent(
                            maxCrossAxisExtent: 220,
                            mainAxisSpacing: 12,
                            crossAxisSpacing: 12,
                            childAspectRatio: 0.80,
                          ),
                      itemBuilder: (_, index) {
                        final album = albums[index];
                        final representativeTrack = _representativeCoverTrack(
                          library,
                          album.value,
                        );
                        final coverBytes = library.coverBytesOf(
                          representativeTrack,
                        );

                        return Material(
                          color: Colors.white.withValues(alpha: 0.04),
                          borderRadius: BorderRadius.circular(14),
                          child: InkWell(
                            borderRadius: BorderRadius.circular(14),
                            onTap: () {
                              setState(() {
                                _selectedAlbum = album.key;
                              });
                            },
                            child: Padding(
                              padding: const EdgeInsets.all(12),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Expanded(
                                    child: ClipRRect(
                                      borderRadius: BorderRadius.circular(10),
                                      child: _CoverImage(
                                        coverPath:
                                            representativeTrack.coverPath,
                                        coverBytes: coverBytes,
                                      ),
                                    ),
                                  ),
                                  const SizedBox(height: 8),
                                  Text(
                                    album.key,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(
                                      fontWeight: FontWeight.w700,
                                    ),
                                  ),
                                  const SizedBox(height: 2),
                                  Text(
                                    t.albumTrackCountText(album.value.length),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                      fontSize: 12,
                                      color: Colors.white.withValues(
                                        alpha: 0.66,
                                      ),
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  ),
          ),
        ],
      ),
    );
  }

  Widget _buildArtistsPanel({
    required LibraryState library,
    required AppStrings t,
  }) {
    final artists =
        library.filteredTracks
            .map((track) => track.artist)
            .toSet()
            .toList(growable: false)
          ..sort((a, b) => a.compareTo(b));

    return GlassPanel(
      lowEffects: library.lowEffects,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            t.artists,
            style: TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _searchController,
            decoration: InputDecoration(
              hintText: t.searchArtist,
              prefixIcon: const Icon(Icons.search_rounded),
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(14),
              ),
            ),
          ),
          const SizedBox(height: 14),
          Expanded(
            child: artists.isEmpty
                ? Center(
                    child: Text(
                      t.noArtistMatch,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.68),
                      ),
                    ),
                  )
                : MiddleClickAutoScrollView(
                    builder: (context, controller) => ListView.separated(
                      controller: controller,
                      itemCount: artists.length,
                      separatorBuilder: (_, _) => Divider(
                        color: Colors.white.withValues(alpha: 0.08),
                        height: 1,
                      ),
                      itemBuilder: (_, index) {
                        final artist = artists[index];
                        return Material(
                          color: Colors.transparent,
                          child: InkWell(
                            onTap: () {
                              setState(() {
                                _selectedArtist = artist;
                              });
                            },
                            borderRadius: BorderRadius.circular(8),
                            child: SizedBox(
                              height: 56,
                              child: Row(
                                children: [
                                  const SizedBox(width: 8),
                                  Text(
                                    artist,
                                    style: const TextStyle(
                                      fontSize: 16,
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                  const Spacer(),
                                  Icon(
                                    Icons.chevron_right_rounded,
                                    color: Colors.white.withValues(alpha: 0.65),
                                  ),
                                  const SizedBox(width: 8),
                                ],
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  ),
          ),
        ],
      ),
    );
  }

  Widget _buildAlbumTracksPanel({
    required LibraryState library,
    required PlaybackState playback,
    required String album,
    required AppStrings t,
  }) {
    final tracks = library.filteredTracks
        .where((track) => track.album == album)
        .toList(growable: false);
    return _buildDetailTracksPanel(
      library: library,
      playback: playback,
      title: album,
      subtitle: t.albumSubtitle(tracks.length),
      tracks: tracks,
      t: t,
      onBack: () {
        setState(() {
          _selectedAlbum = null;
        });
      },
    );
  }

  Widget _buildArtistTracksPanel({
    required LibraryState library,
    required PlaybackState playback,
    required String artist,
    required AppStrings t,
  }) {
    final tracks = library.filteredTracks
        .where((track) => track.artist == artist)
        .toList(growable: false);
    return _buildDetailTracksPanel(
      library: library,
      playback: playback,
      title: artist,
      subtitle: t.artistSubtitle(tracks.length),
      tracks: tracks,
      t: t,
      onBack: () {
        setState(() {
          _selectedArtist = null;
        });
      },
    );
  }

  Widget _buildDetailTracksPanel({
    required LibraryState library,
    required PlaybackState playback,
    required AppStrings t,
    required String title,
    required String subtitle,
    required List<Track> tracks,
    required VoidCallback onBack,
  }) {
    final libraryCtrl = ref.read(libraryProvider.notifier);
    final playbackCtrl = ref.read(playbackProvider.notifier);

    return GlassPanel(
      lowEffects: library.lowEffects,
      child: Column(
        children: [
          Row(
            children: [
              IconButton(
                onPressed: onBack,
                icon: const Icon(Icons.arrow_back_rounded),
              ),
              const SizedBox(width: 6),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 20,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    Text(
                      subtitle,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.68),
                      ),
                    ),
                  ],
                ),
              ),
              _GlassPlayAllButton(
                label: t.playAll,
                enabled: tracks.isNotEmpty,
                onPressed: tracks.isEmpty
                    ? null
                    : () => playbackCtrl.playFromPlaylist(tracks.first, tracks),
              ),
            ],
          ),
          const SizedBox(height: 10),
          _buildTrackHeader(t: t),
          const SizedBox(height: 8),
          Expanded(
            child: tracks.isEmpty
                ? Center(
                    child: Text(
                      t.noTracks,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.68),
                      ),
                    ),
                  )
                : MiddleClickAutoScrollView(
                    builder: (context, controller) => ListView.builder(
                      controller: controller,
                      itemCount: tracks.length,
                      itemBuilder: (_, index) {
                        final track = tracks[index];
                        final active = playback.currentTrack?.id == track.id;
                        final isFavorite = libraryCtrl.isFavorite(track);
                        final duration = library.durationOf(track);
                        final coverBytes = library.coverBytesOf(track);

                        return Padding(
                          padding: const EdgeInsets.symmetric(vertical: 2),
                          child: GestureDetector(
                            behavior: HitTestBehavior.opaque,
                            onSecondaryTapDown: (_) => _openTrackDetails(
                              context: context,
                              track: track,
                              duration: duration,
                              coverBytes: coverBytes,
                            ),
                            child: Material(
                              color: active
                                  ? const Color(
                                      0xFF39C0FF,
                                    ).withValues(alpha: 0.16)
                                  : Colors.transparent,
                              borderRadius: BorderRadius.circular(10),
                              child: InkWell(
                                borderRadius: BorderRadius.circular(10),
                                onTap: () => playbackCtrl.playFromPlaylist(
                                  track,
                                  tracks,
                                ),
                                child: SizedBox(
                                  height: 56,
                                  child: Row(
                                    children: [
                                      const SizedBox(width: 10),
                                      SizedBox(
                                        width: 52,
                                        child: _TrackCover(
                                          track: track,
                                          isActive: active,
                                          coverBytes: coverBytes,
                                        ),
                                      ),
                                      const SizedBox(width: 12),
                                      Expanded(
                                        flex: 5,
                                        child: Text(
                                          track.title,
                                          maxLines: 1,
                                          overflow: TextOverflow.ellipsis,
                                          style: const TextStyle(
                                            fontWeight: FontWeight.w600,
                                          ),
                                        ),
                                      ),
                                      Expanded(
                                        flex: 3,
                                        child: Text(
                                          track.artist,
                                          maxLines: 1,
                                          overflow: TextOverflow.ellipsis,
                                          style: TextStyle(
                                            color: Colors.white.withValues(
                                              alpha: 0.75,
                                            ),
                                          ),
                                        ),
                                      ),
                                      SizedBox(
                                        width: 84,
                                        child: Text(
                                          _formatDuration(duration),
                                          textAlign: TextAlign.right,
                                          style: TextStyle(
                                            color: Colors.white.withValues(
                                              alpha: 0.82,
                                            ),
                                          ),
                                        ),
                                      ),
                                      const SizedBox(width: 4),
                                      IconButton(
                                        tooltip: isFavorite
                                            ? t.uncollect
                                            : t.collect,
                                        onPressed: () =>
                                            libraryCtrl.toggleFavorite(track),
                                        icon: Icon(
                                          isFavorite
                                              ? Icons.favorite_rounded
                                              : Icons.favorite_border_rounded,
                                          color: isFavorite
                                              ? const Color(0xFF39C0FF)
                                              : Colors.white.withValues(
                                                  alpha: 0.78,
                                                ),
                                          size: 18,
                                        ),
                                      ),
                                      const SizedBox(width: 6),
                                    ],
                                  ),
                                ),
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  ),
          ),
        ],
      ),
    );
  }

  Widget _buildPlayerBar({
    required PlaybackState playback,
    required LibraryState library,
    required AppStrings t,
  }) {
    final ctrl = ref.read(playbackProvider.notifier);
    final canToggleQueue = playback.currentPlaylist.isNotEmpty;
    final duration = playback.duration.inMilliseconds.toDouble();
    final position = playback.currentTime.inMilliseconds.toDouble();
    final safeDuration = duration > 0 ? duration : 1.0;
    final safePosition = position.clamp(0.0, safeDuration);

    return GlassPanel(
      lowEffects: library.lowEffects,
      radius: PrismWaveTheme.panelRadius,
      padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
      child: Row(
        children: [
          SizedBox(
            width: 280,
            child: _NowPlayingInfo(
              track: playback.currentTrack,
              t: t,
              duration: playback.duration > Duration.zero
                  ? playback.duration
                  : (playback.currentTrack == null
                        ? null
                        : library.durationOf(playback.currentTrack!)),
              coverBytes: playback.currentTrack == null
                  ? null
                  : library.coverBytesOf(playback.currentTrack!),
              onTap: playback.currentTrack == null
                  ? null
                  : () => _openFullPlay(playback.currentTrack!),
            ),
          ),
          Expanded(
            child: Align(
              alignment: Alignment.center,
              child: SizedBox(
                width: 700,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        _PlayerTransportButton(
                          tooltip: 'Previous',
                          onPressed: playback.hasTrack ? ctrl.previous : null,
                          icon: const Icon(Icons.skip_previous_rounded),
                        ),
                        const SizedBox(width: 10),
                        _PlaybackToggleButton(
                          onPressed: playback.hasTrack
                              ? ctrl.togglePlayPause
                              : null,
                          isPlaying: playback.isPlaying,
                        ),
                        const SizedBox(width: 10),
                        _PlayerTransportButton(
                          tooltip: 'Next',
                          onPressed: playback.hasTrack ? ctrl.next : null,
                          icon: const Icon(Icons.skip_next_rounded),
                        ),
                        const SizedBox(width: 10),
                        _PlaybackModeButton(
                          t: t,
                          mode: playback.playbackMode,
                          onPressed: ctrl.cycleMode,
                        ),
                        const SizedBox(width: 10),
                        _PlaybackQueueButton(
                          tooltip: t.playbackQueue,
                          onPressed: canToggleQueue
                              ? () => _togglePlaybackQueue(playback)
                              : null,
                          isActive: _showPlaybackQueue,
                        ),
                      ],
                    ),
                    Row(
                      children: [
                        SizedBox(
                          width: 52,
                          child: Text(
                            _formatDuration(playback.currentTime),
                            textAlign: TextAlign.right,
                            style: TextStyle(
                              color: PrismWaveTheme.textSecondary.withValues(
                                alpha: 0.78,
                              ),
                              fontSize: 12,
                              fontWeight: FontWeight.w500,
                            ),
                          ),
                        ),
                        Expanded(
                          child: SliderTheme(
                            data: SliderTheme.of(context).copyWith(
                              activeTrackColor: Colors.white,
                              inactiveTrackColor: Colors.white.withValues(
                                alpha: 0.16,
                              ),
                              thumbColor: Colors.white,
                              overlayColor: Colors.white.withValues(
                                alpha: 0.14,
                              ),
                              trackHeight: 3.2,
                            ),
                            child: Slider(
                              value: safePosition,
                              min: 0,
                              max: safeDuration,
                              onChanged: playback.hasTrack
                                  ? (value) => ctrl.seekTo(
                                      Duration(milliseconds: value.round()),
                                    )
                                  : null,
                            ),
                          ),
                        ),
                        SizedBox(
                          width: 52,
                          child: Text(
                            _formatDuration(playback.duration),
                            style: TextStyle(
                              color: PrismWaveTheme.textSecondary.withValues(
                                alpha: 0.78,
                              ),
                              fontSize: 12,
                              fontWeight: FontWeight.w500,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ),
          SizedBox(
            width: 190,
            child: Row(
              children: [
                Icon(
                  Icons.volume_up_rounded,
                  size: 18,
                  color: PrismWaveTheme.textSecondary,
                ),
                Expanded(
                  child: SliderTheme(
                    data: SliderTheme.of(context).copyWith(
                      activeTrackColor: PrismWaveTheme.textPrimary,
                      inactiveTrackColor: Colors.white.withValues(alpha: 0.15),
                      thumbColor: PrismWaveTheme.textPrimary,
                      overlayColor: Colors.white.withValues(alpha: 0.10),
                      trackHeight: 3,
                    ),
                    child: Slider(
                      value: playback.volume,
                      onChanged: ctrl.setVolume,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _openFullPlay(Track track) async {
    final playback = ref.read(playbackProvider);
    unawaited(
      ref
          .read(libraryProvider.notifier)
          .ensureLyricsLoaded(track, durationHint: playback.duration),
    );
    if (!mounted) return;
    await Navigator.of(context).push(
      PageRouteBuilder<void>(
        transitionDuration: const Duration(milliseconds: 420),
        reverseTransitionDuration: const Duration(milliseconds: 320),
        pageBuilder: (context, animation, secondaryAnimation) {
          return SlideTransition(
            position: Tween<Offset>(begin: const Offset(0, 1), end: Offset.zero)
                .animate(
                  CurvedAnimation(
                    parent: animation,
                    curve: Curves.easeOutCubic,
                  ),
                ),
            child: const FullPlayPage(),
          );
        },
      ),
    );
  }

  Widget _buildReorderProxy(
    Widget child,
    Animation<double> animation, {
    required double radius,
  }) {
    return AnimatedBuilder(
      animation: animation,
      child: child,
      builder: (context, child) {
        final progress = Curves.easeOutCubic.transform(animation.value);
        final borderRadius = BorderRadius.circular(radius);

        return Transform.scale(
          scale: 1 + (0.01 * progress),
          child: DecoratedBox(
            decoration: BoxDecoration(
              borderRadius: borderRadius,
              boxShadow: [
                BoxShadow(
                  color: Colors.black.withValues(
                    alpha: 0.12 + (0.10 * progress),
                  ),
                  blurRadius: 18 + (8 * progress),
                  offset: Offset(0, 8 + (3 * progress)),
                ),
              ],
            ),
            child: Material(
              type: MaterialType.transparency,
              borderRadius: borderRadius,
              child: child,
            ),
          ),
        );
      },
    );
  }

  void _openSettings() {
    setState(() {
      _section = MainSection.settings;
    });
  }

  Future<void> _openHitsTransition() async {
    final availabilityFuture = HitsAvailabilityResolver.resolve();
    final playback = ref.read(playbackProvider);
    final track = playback.currentTrack;
    if (track != null) {
      await ref
          .read(libraryProvider.notifier)
          .ensureLyricsLoaded(track, durationHint: playback.duration);
    }
    if (!mounted) return;
    await ref
        .read(playbackProvider.notifier)
        .setAudioOutputMode(AudioOutputMode.wasapiShared);
    if (!mounted) return;
    await Navigator.of(context).push(
      PageRouteBuilder<void>(
        transitionDuration: const Duration(milliseconds: 480),
        reverseTransitionDuration: const Duration(milliseconds: 340),
        pageBuilder: (_, _, _) =>
            HitsTransitionPage(availabilityFuture: availabilityFuture),
        transitionsBuilder: (context, animation, secondaryAnimation, child) {
          final curved = CurvedAnimation(
            parent: animation,
            curve: Curves.easeOutCubic,
            reverseCurve: Curves.easeInCubic,
          );
          return AnimatedBuilder(
            animation: curved,
            builder: (context, _) {
              final progress = curved.value;
              return ImageFiltered(
                imageFilter: ui.ImageFilter.blur(
                  sigmaX: (1.0 - progress) * 18,
                  sigmaY: (1.0 - progress) * 4,
                ),
                child: Transform.translate(
                  offset: Offset(
                    (1.0 - progress) * MediaQuery.of(context).size.width * 0.42,
                    0,
                  ),
                  child: Opacity(
                    opacity: progress.clamp(0.0, 1.0),
                    child: child,
                  ),
                ),
              );
            },
          );
        },
      ),
    );
  }

  String _formatDuration(Duration? duration) {
    if (duration == null || duration <= Duration.zero) {
      return '--:--';
    }

    final minutes = duration.inMinutes.remainder(60).toString().padLeft(2, '0');
    final seconds = duration.inSeconds.remainder(60).toString().padLeft(2, '0');
    final hours = duration.inHours;
    if (hours > 0) {
      return '${hours.toString().padLeft(2, '0')}:$minutes:$seconds';
    }
    return '$minutes:$seconds';
  }

  Track _representativeCoverTrack(LibraryState library, List<Track> tracks) {
    for (final track in tracks) {
      final coverBytes = library.coverBytesOf(track);
      if (coverBytes != null && coverBytes.isNotEmpty) {
        return track;
      }
      final coverPath = track.coverPath?.trim() ?? '';
      if (coverPath.isNotEmpty && File(coverPath).existsSync()) {
        return track;
      }
    }
    return tracks.first;
  }
}

bool _looksLikeDsdTrack(Track? track) {
  if (track == null || track.isRemote) {
    return false;
  }
  final lowerPath = track.path.toLowerCase();
  return lowerPath.endsWith('.dsf') || lowerPath.endsWith('.dff');
}

enum _SettingsCategory { basic, playback }

class _SettingsPanel extends ConsumerStatefulWidget {
  const _SettingsPanel({required this.onClose});

  final VoidCallback onClose;

  @override
  ConsumerState<_SettingsPanel> createState() => _SettingsPanelState();
}

class _SettingsPanelState extends ConsumerState<_SettingsPanel> {
  _SettingsCategory _selectedCategory = _SettingsCategory.basic;

  @override
  Widget build(BuildContext context) {
    final appSettings = ref.watch(appSettingsProvider);
    final appSettingsController = ref.read(appSettingsProvider.notifier);
    final t = AppStrings(appSettings.language);
    final library = ref.watch(libraryProvider);
    final libraryController = ref.read(libraryProvider.notifier);
    final playback = ref.watch(playbackProvider);
    final playbackController = ref.read(playbackProvider.notifier);
    final audioDevices = playback.availableAudioOutputDevices;
    final selectedAudioDeviceId =
        audioDevices.any((device) => device.id == playback.audioOutputDeviceId)
        ? playback.audioOutputDeviceId
        : audioDevices.first.id;
    final dsdDevices = playback.availableWindowsDsdDevices;
    final selectedWindowsDsdDeviceId =
        playback.selectedWindowsDsdDeviceId == 'auto' ||
            dsdDevices.any(
              (device) =>
                  device.id.toString() == playback.selectedWindowsDsdDeviceId,
            )
        ? playback.selectedWindowsDsdDeviceId
        : 'auto';
    final currentTrackIsDsd = _looksLikeDsdTrack(playback.currentTrack);
    final dsdBackendSummary =
        playback.backendKind == PlaybackBackendKind.windowsDsd
        ? t.windowsDsdBackendActive
        : currentTrackIsDsd &&
              (playback.windowsDsdFallbackReason?.trim().isNotEmpty ?? false)
        ? t.windowsDsdBackendFallback
        : t.windowsDsdBackendIdle;
    final dsdRuntimeSummary = playback.windowsDsdAvailable
        ? t.windowsDsdRuntimeReady
        : t.windowsDsdRuntimeMissing;
    final dsdDeviceSummary = dsdDevices.isEmpty
        ? t.windowsDsdNoDevice
        : t.windowsDsdDeviceCountValue(dsdDevices.length);
    final dsdFallbackReason = playback.windowsDsdFallbackReason?.trim();

    return GlassPanel(
      lowEffects: library.lowEffects,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconButton(
                tooltip: t.back,
                onPressed: widget.onClose,
                icon: const Icon(Icons.arrow_back_rounded),
              ),
              const SizedBox(width: 6),
              Text(
                t.settings,
                style: const TextStyle(
                  fontSize: 22,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const Spacer(),
              if (library.isScanning)
                const SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
            ],
          ),
          const SizedBox(height: 12),
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 260),
            child: _SettingsCategoryTabs(
              selectedCategory: _selectedCategory,
              onChanged: (value) {
                if (_selectedCategory == value) return;
                setState(() {
                  _selectedCategory = value;
                });
              },
              t: t,
            ),
          ),
          const SizedBox(height: 16),
          Expanded(
            child: MiddleClickAutoScrollView(
              builder: (context, controller) => SingleChildScrollView(
                key: ValueKey(_selectedCategory),
                controller: controller,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (_selectedCategory == _SettingsCategory.basic) ...[
                      _SettingsBlock(
                        title: t.folderSection,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Wrap(
                              spacing: 10,
                              runSpacing: 10,
                              children: [
                                FilledButton.icon(
                                  onPressed: library.isScanning
                                      ? null
                                      : libraryController.addMusicFolder,
                                  icon: const Icon(Icons.add_rounded),
                                  label: Text(t.addMusicFolder),
                                ),
                                OutlinedButton.icon(
                                  onPressed: library.isScanning
                                      ? null
                                      : libraryController.rescanAllFolders,
                                  icon: const Icon(Icons.refresh_rounded),
                                  label: Text(t.rescanAll),
                                ),
                              ],
                            ),
                            const SizedBox(height: 12),
                            _SettingsFoldersCard(
                              folders: library.libraryFolders,
                              emptyText: t.noFolderConfigured,
                              removeLabel: t.remove,
                              onRemove: libraryController.removeMusicFolder,
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 14),
                      _SettingsBlock(
                        title: t.languageTitle,
                        child: _GlassSelectField<AppLanguage>(
                          value: appSettings.language,
                          lowEffects: library.lowEffects,
                          onChanged: (value) {
                            appSettingsController.setLanguage(value);
                          },
                          items: AppLanguage.values
                              .map(
                                (lang) => _GlassSelectEntry<AppLanguage>(
                                  value: lang,
                                  label: t.languageLabel(lang),
                                ),
                              )
                              .toList(growable: false),
                        ),
                      ),
                      const SizedBox(height: 14),
                      _SettingsBlock(
                        title: t.topBarDisplayTitle,
                        child: Column(
                          children: [
                            Align(
                              alignment: Alignment.centerLeft,
                              child: Text(
                                t.topBarIdleModeTitle,
                                style: TextStyle(
                                  color: Colors.white.withValues(alpha: 0.64),
                                  fontSize: 12,
                                  fontWeight: FontWeight.w500,
                                ),
                              ),
                            ),
                            const SizedBox(height: 8),
                            _GlassSelectField<TopBarIdleMode>(
                              value: appSettings.topBarIdleMode,
                              lowEffects: library.lowEffects,
                              onChanged: (value) {
                                appSettingsController.setTopBarIdleMode(value);
                              },
                              items: TopBarIdleMode.values
                                  .map(
                                    (mode) => _GlassSelectEntry<TopBarIdleMode>(
                                      value: mode,
                                      label: t.topBarIdleModeLabel(mode),
                                    ),
                                  )
                                  .toList(growable: false),
                            ),
                            const SizedBox(height: 10),
                            _IdleTopBarTextField(
                              initialValue: appSettings.topBarIdleText,
                              label: t.topBarCustomTextTitle,
                              hint: t.topBarCustomTextHint,
                              onSubmitted:
                                  appSettingsController.setTopBarIdleText,
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 14),
                    ],
                    if (_selectedCategory == _SettingsCategory.playback) ...[
                      _SettingsBlock(
                        title: t.audioOutputMode,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            _GlassSelectField<AudioOutputMode>(
                              key: ValueKey(
                                'audio-mode-${playback.audioOutputMode.id}',
                              ),
                              value: playback.audioOutputMode,
                              lowEffects: library.lowEffects,
                              onChanged: (value) {
                                playbackController.setAudioOutputMode(value);
                              },
                              items: AudioOutputMode.values
                                  .map(
                                    (mode) =>
                                        _GlassSelectEntry<AudioOutputMode>(
                                          value: mode,
                                          label: t.outputModeLabel(mode),
                                        ),
                                  )
                                  .toList(growable: false),
                            ),
                            const SizedBox(height: 8),
                            Text(
                              t.outputModeDescription(playback.audioOutputMode),
                              style: TextStyle(
                                color: Colors.white.withValues(alpha: 0.66),
                                fontSize: 12,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 14),
                      _SettingsBlock(
                        title: t.audioOutputDevice,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            _GlassSelectField<String>(
                              key: ValueKey(
                                'audio-device-$selectedAudioDeviceId-${audioDevices.length}',
                              ),
                              value: selectedAudioDeviceId,
                              lowEffects: library.lowEffects,
                              onChanged: (value) {
                                playbackController.setAudioOutputDevice(value);
                              },
                              items: audioDevices
                                  .map(
                                    (device) => _GlassSelectEntry<String>(
                                      value: device.id,
                                      label: t.audioDeviceLabel(
                                        device.label,
                                        isAuto: device.id == 'auto',
                                      ),
                                    ),
                                  )
                                  .toList(growable: false),
                            ),
                            const SizedBox(height: 8),
                            Text(
                              t.audioOutputDeviceHint,
                              style: TextStyle(
                                color: Colors.white.withValues(alpha: 0.66),
                                fontSize: 12,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 14),
                      _SettingsBlock(
                        title: t.windowsDsdDevice,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            _GlassSelectField<String>(
                              key: ValueKey(
                                'windows-dsd-device-$selectedWindowsDsdDeviceId-${dsdDevices.length}',
                              ),
                              value: selectedWindowsDsdDeviceId,
                              lowEffects: library.lowEffects,
                              onChanged: (value) {
                                if (dsdDevices.isEmpty && value != 'auto') {
                                  return;
                                }
                                playbackController.setWindowsDsdDevice(value);
                              },
                              items: <_GlassSelectEntry<String>>[
                                _GlassSelectEntry<String>(
                                  value: 'auto',
                                  label: t.windowsDsdDeviceLabel(
                                    t.defaultAudioDevice,
                                    isAuto: true,
                                    supportsNativeDsd: false,
                                  ),
                                ),
                                ...dsdDevices.map(
                                  (device) => _GlassSelectEntry<String>(
                                    value: device.id.toString(),
                                    label: t.windowsDsdDeviceLabel(
                                      device.name,
                                      isAuto: false,
                                      supportsNativeDsd:
                                          device.supportsNativeDsd,
                                    ),
                                  ),
                                ),
                              ],
                            ),
                            const SizedBox(height: 8),
                            Text(
                              dsdDevices.isEmpty
                                  ? t.windowsDsdUnavailableHint
                                  : t.windowsDsdDeviceHint,
                              style: TextStyle(
                                color: Colors.white.withValues(alpha: 0.66),
                                fontSize: 12,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 14),
                      _SettingsBlock(
                        title: t.windowsDsdStatus,
                        child: Column(
                          children: [
                            _SettingsInfoRow(
                              label: t.windowsDsdRuntimeStatus,
                              value: dsdRuntimeSummary,
                            ),
                            const SizedBox(height: 10),
                            _SettingsInfoRow(
                              label: t.windowsDsdDeviceCountLabel,
                              value: dsdDeviceSummary,
                            ),
                            const SizedBox(height: 10),
                            _SettingsInfoRow(
                              label: t.windowsDsdCurrentBackend,
                              value: dsdBackendSummary,
                            ),
                            if ((playback.windowsDsdOutputModeLabel ?? '')
                                .isNotEmpty) ...[
                              const SizedBox(height: 10),
                              _SettingsInfoRow(
                                label: t.windowsDsdOutputModeStatus,
                                value: playback.windowsDsdOutputModeLabel!,
                              ),
                            ],
                            if ((playback.windowsDsdActiveDeviceName ?? '')
                                .isNotEmpty) ...[
                              const SizedBox(height: 10),
                              _SettingsInfoRow(
                                label: t.windowsDsdActiveDevice,
                                value: playback.windowsDsdActiveDeviceName!,
                              ),
                            ],
                          ],
                        ),
                      ),
                    ],
                    if (_selectedCategory == _SettingsCategory.basic) ...[
                      _SettingsBlock(
                        title: t.audioFade,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            _SettingsToggleTile(
                              title: t.audioFadeEnabled,
                              subtitle: playback.fadeEnabled
                                  ? t.audioFadeHint
                                  : t.audioFadeDisabledHint,
                              value: playback.fadeEnabled,
                              onChanged: playbackController.setFadeEnabled,
                            ),
                            const SizedBox(height: 12),
                            _SettingsDurationSlider(
                              label: t.audioFadeDuration,
                              valueLabel: t.audioFadeDurationValue(
                                playback.fadeDuration,
                              ),
                              valueMs: playback.fadeDuration.inMilliseconds,
                              enabled: playback.fadeEnabled,
                              onChanged: (milliseconds) {
                                playbackController.setFadeDuration(
                                  Duration(milliseconds: milliseconds),
                                );
                              },
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 14),
                      _SettingsBlock(
                        title: t.onlineModeSettingTitle,
                        child: _SettingsToggleTile(
                          title: t.onlineModeSettingTitle,
                          subtitle: t.onlineModeSettingDescription,
                          value: ref.watch(
                            appSettingsProvider.select(
                              (s) => s.onlineModeEnabled,
                            ),
                          ),
                          onChanged: (value) => ref
                              .read(appSettingsProvider.notifier)
                              .setOnlineModeEnabled(value),
                        ),
                      ),
                      const SizedBox(height: 14),
                      _SettingsBlock(
                        title: t.developerMode,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            SwitchListTile(
                              value: playback.developerMode,
                              onChanged: playbackController.setDeveloperMode,
                              contentPadding: EdgeInsets.zero,
                              title: Text(t.developerMode),
                              subtitle: Text(t.developerModeHint),
                            ),
                            if (playback.developerMode) ...[
                              if (currentTrackIsDsd ||
                                  playback.backendKind ==
                                      PlaybackBackendKind.windowsDsd ||
                                  (dsdFallbackReason != null &&
                                      dsdFallbackReason.isNotEmpty)) ...[
                                const SizedBox(height: 4),
                                Text(
                                  t.windowsDsdStatus,
                                  style: TextStyle(
                                    color: Colors.white.withValues(alpha: 0.86),
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                                const SizedBox(height: 10),
                                _SettingsInfoRow(
                                  label: t.windowsDsdCurrentBackend,
                                  value: dsdBackendSummary,
                                ),
                                if ((playback.windowsDsdOutputModeLabel ?? '')
                                    .isNotEmpty) ...[
                                  const SizedBox(height: 10),
                                  _SettingsInfoRow(
                                    label: t.windowsDsdOutputModeStatus,
                                    value: playback.windowsDsdOutputModeLabel!,
                                  ),
                                ],
                                if ((playback.windowsDsdActiveDeviceName ?? '')
                                    .isNotEmpty) ...[
                                  const SizedBox(height: 10),
                                  _SettingsInfoRow(
                                    label: t.windowsDsdActiveDevice,
                                    value: playback.windowsDsdActiveDeviceName!,
                                  ),
                                ],
                                if (dsdFallbackReason != null &&
                                    dsdFallbackReason.isNotEmpty) ...[
                                  const SizedBox(height: 10),
                                  _SettingsInfoRow(
                                    label: t.windowsDsdFallbackReason,
                                    value: dsdFallbackReason,
                                    multiline: true,
                                  ),
                                ],
                                const SizedBox(height: 14),
                              ],
                              const SizedBox(height: 6),
                              Row(
                                children: [
                                  Text(
                                    '${t.playbackLogs} (${playback.debugLogs.length})',
                                    style: TextStyle(
                                      color: Colors.white.withValues(
                                        alpha: 0.86,
                                      ),
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                  const Spacer(),
                                  TextButton.icon(
                                    onPressed: playback.debugLogs.isEmpty
                                        ? null
                                        : () async {
                                            await Clipboard.setData(
                                              ClipboardData(
                                                text: playback.debugLogs.join(
                                                  '\n',
                                                ),
                                              ),
                                            );
                                            if (!context.mounted) return;
                                            ScaffoldMessenger.of(
                                              context,
                                            ).showSnackBar(
                                              SnackBar(
                                                content: Text(t.logsCopied),
                                              ),
                                            );
                                          },
                                    icon: const Icon(
                                      Icons.copy_rounded,
                                      size: 16,
                                    ),
                                    label: Text(t.copy),
                                  ),
                                  const SizedBox(width: 4),
                                  TextButton.icon(
                                    onPressed: playback.debugLogs.isEmpty
                                        ? null
                                        : playbackController.clearDebugLogs,
                                    icon: const Icon(
                                      Icons.delete_sweep_rounded,
                                      size: 16,
                                    ),
                                    label: Text(t.clear),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 6),
                              SizedBox(
                                height: 160,
                                child: Container(
                                  decoration: BoxDecoration(
                                    color: Colors.black.withValues(alpha: 0.22),
                                    borderRadius: BorderRadius.circular(10),
                                    border: Border.all(
                                      color: Colors.white.withValues(
                                        alpha: 0.10,
                                      ),
                                    ),
                                  ),
                                  child: playback.debugLogs.isEmpty
                                      ? Center(
                                          child: Text(
                                            t.noLogsHint,
                                            style: TextStyle(
                                              color: Colors.white.withValues(
                                                alpha: 0.62,
                                              ),
                                              fontSize: 12,
                                            ),
                                          ),
                                        )
                                      : ListView.builder(
                                          reverse: true,
                                          itemCount: playback.debugLogs.length,
                                          itemBuilder: (_, index) {
                                            final line =
                                                playback.debugLogs[playback
                                                        .debugLogs
                                                        .length -
                                                    1 -
                                                    index];
                                            return Padding(
                                              padding:
                                                  const EdgeInsets.symmetric(
                                                    horizontal: 10,
                                                    vertical: 4,
                                                  ),
                                              child: Text(
                                                line,
                                                style: TextStyle(
                                                  fontSize: 11,
                                                  height: 1.35,
                                                  color: Colors.white
                                                      .withValues(alpha: 0.84),
                                                ),
                                              ),
                                            );
                                          },
                                        ),
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                      const SizedBox(height: 14),
                      _SettingsBlock(
                        title: t.updateCheckTitle,
                        child: _SettingsUpdateBlock(
                          currentVersion: appSettings.currentVersion,
                          latestVersion: appSettings.latestReleaseVersion,
                          status: appSettings.releaseUpdateStatus,
                          errorMessage: appSettings.releaseUpdateError,
                          onCheckUpdate: appSettingsController.checkForUpdates,
                          onOpenUpdate: () async {
                            final targetUrl =
                                appSettings.latestInstallerUrl.isNotEmpty
                                ? appSettings.latestInstallerUrl
                                : appSettings.latestReleaseUrl;
                            await _openExternalUrl(targetUrl);
                          },
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _SettingsCategoryTabs extends StatelessWidget {
  const _SettingsCategoryTabs({
    required this.selectedCategory,
    required this.onChanged,
    required this.t,
  });

  final _SettingsCategory selectedCategory;
  final ValueChanged<_SettingsCategory> onChanged;
  final AppStrings t;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(4),
      decoration: BoxDecoration(
        color: Colors.black.withValues(alpha: 0.18),
        borderRadius: BorderRadius.circular(18),
        border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
      ),
      child: Row(
        children: [
          Expanded(
            child: _SettingsCategoryButton(
              label: t.settingsBasicTab,
              selected: selectedCategory == _SettingsCategory.basic,
              onTap: () => onChanged(_SettingsCategory.basic),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: _SettingsCategoryButton(
              label: t.settingsPlaybackTab,
              selected: selectedCategory == _SettingsCategory.playback,
              onTap: () => onChanged(_SettingsCategory.playback),
            ),
          ),
        ],
      ),
    );
  }
}

class _SettingsCategoryButton extends StatelessWidget {
  const _SettingsCategoryButton({
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Material(
      type: MaterialType.transparency,
      child: InkWell(
        borderRadius: BorderRadius.circular(14),
        onTap: onTap,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 160),
          curve: Curves.easeOutCubic,
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 11),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(14),
            color: selected
                ? Colors.white.withValues(alpha: 0.16)
                : Colors.transparent,
            border: Border.all(
              color: selected
                  ? Colors.white.withValues(alpha: 0.18)
                  : Colors.transparent,
            ),
          ),
          child: Text(
            label,
            textAlign: TextAlign.center,
            style: TextStyle(
              color: Colors.white.withValues(alpha: selected ? 0.96 : 0.72),
              fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
            ),
          ),
        ),
      ),
    );
  }
}

class _SettingsBlock extends StatelessWidget {
  const _SettingsBlock({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(16),
        color: Colors.white.withValues(alpha: 0.04),
        border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: TextStyle(
              color: Colors.white.withValues(alpha: 0.92),
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 10),
          child,
        ],
      ),
    );
  }
}

class _SettingsInfoRow extends StatelessWidget {
  const _SettingsInfoRow({
    required this.label,
    required this.value,
    this.multiline = false,
  });

  final String label;
  final String value;
  final bool multiline;

  @override
  Widget build(BuildContext context) {
    final labelStyle = TextStyle(
      color: Colors.white.withValues(alpha: 0.60),
      fontSize: 12,
      fontWeight: FontWeight.w500,
    );
    final valueStyle = TextStyle(
      color: Colors.white.withValues(alpha: 0.92),
      fontSize: 12.5,
      fontWeight: FontWeight.w600,
    );

    if (multiline) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: labelStyle),
          const SizedBox(height: 4),
          Text(value, style: valueStyle),
        ],
      );
    }

    return Row(
      children: [
        Expanded(child: Text(label, style: labelStyle)),
        const SizedBox(width: 12),
        Flexible(
          child: Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            textAlign: TextAlign.right,
            style: valueStyle,
          ),
        ),
      ],
    );
  }
}

class _SettingsUpdateBlock extends ConsumerWidget {
  const _SettingsUpdateBlock({
    required this.currentVersion,
    required this.latestVersion,
    required this.status,
    required this.errorMessage,
    required this.onCheckUpdate,
    required this.onOpenUpdate,
  });

  final String currentVersion;
  final String latestVersion;
  final ReleaseUpdateStatus status;
  final String errorMessage;
  final Future<void> Function() onCheckUpdate;
  final Future<void> Function() onOpenUpdate;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final settings = ref.watch(appSettingsProvider);
    final t = AppStrings(settings.language);
    final checking = status == ReleaseUpdateStatus.checking;
    final updateAvailable = status == ReleaseUpdateStatus.updateAvailable;
    final showLatest =
        latestVersion.trim().isNotEmpty &&
        status != ReleaseUpdateStatus.idle &&
        status != ReleaseUpdateStatus.checking;

    String? message;
    if (status == ReleaseUpdateStatus.upToDate) {
      message = t.updateUpToDate;
    } else if (status == ReleaseUpdateStatus.updateAvailable) {
      message = t.updateAvailable(latestVersion);
    } else if (status == ReleaseUpdateStatus.failed) {
      message = t.updateCheckFailed;
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    t.currentVersionLabel,
                    style: TextStyle(
                      color: Colors.white.withValues(alpha: 0.64),
                      fontSize: 12,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    currentVersion,
                    style: TextStyle(
                      color: Colors.white.withValues(alpha: 0.92),
                      fontSize: 16,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            _SettingsActionButton(
              label: checking ? t.checkingUpdates : t.checkUpdates,
              onPressed: checking ? null : () => onCheckUpdate(),
            ),
          ],
        ),
        if (message != null) ...[
          const SizedBox(height: 12),
          Text(
            message,
            style: TextStyle(
              color: Colors.white.withValues(alpha: 0.78),
              fontSize: 13,
              fontWeight: FontWeight.w500,
            ),
          ),
        ],
        if (showLatest) ...[
          const SizedBox(height: 10),
          Row(
            children: [
              Text(
                '${t.latestVersionLabel}: ',
                style: TextStyle(
                  color: Colors.white.withValues(alpha: 0.64),
                  fontSize: 12,
                ),
              ),
              Expanded(
                child: Text(
                  latestVersion,
                  style: TextStyle(
                    color: Colors.white.withValues(alpha: 0.88),
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
            ],
          ),
        ],
        if (updateAvailable) ...[
          const SizedBox(height: 12),
          _SettingsActionButton(
            label: t.getUpdate,
            onPressed: () => onOpenUpdate(),
          ),
        ],
        if (status == ReleaseUpdateStatus.failed &&
            errorMessage.trim().isNotEmpty) ...[
          const SizedBox(height: 8),
          Text(
            errorMessage,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              color: Colors.white.withValues(alpha: 0.46),
              fontSize: 11,
            ),
          ),
        ],
      ],
    );
  }
}

class _SettingsActionButton extends StatelessWidget {
  const _SettingsActionButton({required this.label, required this.onPressed});

  final String label;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final enabled = onPressed != null;
    final foreground = Colors.white.withValues(alpha: enabled ? 0.94 : 0.46);
    final border = Colors.white.withValues(alpha: enabled ? 0.14 : 0.08);
    final fill = enabled
        ? const Color(0xFF0F1A2F).withValues(alpha: 0.30)
        : Colors.white.withValues(alpha: 0.03);

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onPressed,
        borderRadius: BorderRadius.circular(999),
        child: Ink(
          height: 40,
          padding: const EdgeInsets.symmetric(horizontal: 16),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(999),
            color: fill,
            border: Border.all(color: border),
          ),
          child: Center(
            child: Text(
              label,
              style: TextStyle(color: foreground, fontWeight: FontWeight.w600),
            ),
          ),
        ),
      ),
    );
  }
}

class _SettingsToggleTile extends StatelessWidget {
  const _SettingsToggleTile({
    required this.title,
    required this.subtitle,
    required this.value,
    required this.onChanged,
  });

  final String title;
  final String subtitle;
  final bool value;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(14),
        color: const Color(0xFF0D1526).withValues(alpha: 0.28),
        border: Border.all(color: Colors.white.withValues(alpha: 0.10)),
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: TextStyle(
                    color: Colors.white.withValues(alpha: 0.92),
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  subtitle,
                  style: TextStyle(
                    color: Colors.white.withValues(alpha: 0.64),
                    fontSize: 12,
                    height: 1.35,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 12),
          Switch.adaptive(
            value: value,
            onChanged: onChanged,
            activeThumbColor: Colors.white,
            activeTrackColor: Colors.white.withValues(alpha: 0.36),
            inactiveThumbColor: Colors.white.withValues(alpha: 0.82),
            inactiveTrackColor: Colors.white.withValues(alpha: 0.16),
          ),
        ],
      ),
    );
  }
}

class _SettingsDurationSlider extends StatefulWidget {
  const _SettingsDurationSlider({
    required this.label,
    required this.valueLabel,
    required this.valueMs,
    required this.enabled,
    required this.onChanged,
  });

  final String label;
  final String valueLabel;
  final int valueMs;
  final bool enabled;
  final ValueChanged<int> onChanged;

  @override
  State<_SettingsDurationSlider> createState() =>
      _SettingsDurationSliderState();
}

class _SettingsDurationSliderState extends State<_SettingsDurationSlider> {
  late double _sliderValue;

  @override
  void initState() {
    super.initState();
    _sliderValue = widget.valueMs.toDouble();
  }

  @override
  void didUpdateWidget(covariant _SettingsDurationSlider oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.valueMs != widget.valueMs) {
      _sliderValue = widget.valueMs.toDouble();
    }
  }

  int _normalizedSliderMs(double value) {
    final snapped = (value / 100).round() * 100;
    return snapped.clamp(100, 1200);
  }

  @override
  Widget build(BuildContext context) {
    final effectiveAlpha = widget.enabled ? 1.0 : 0.46;

    return AnimatedOpacity(
      duration: const Duration(milliseconds: 180),
      opacity: effectiveAlpha,
      child: IgnorePointer(
        ignoring: !widget.enabled,
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(14),
            color: const Color(0xFF0D1526).withValues(alpha: 0.28),
            border: Border.all(color: Colors.white.withValues(alpha: 0.10)),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      widget.label,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.9),
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                  Text(
                    widget.valueLabel,
                    style: TextStyle(
                      color: Colors.white.withValues(alpha: 0.72),
                      fontSize: 12,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              SliderTheme(
                data: SliderTheme.of(context).copyWith(
                  activeTrackColor: Colors.white,
                  inactiveTrackColor: Colors.white.withValues(alpha: 0.20),
                  thumbColor: Colors.white,
                  overlayColor: Colors.white.withValues(alpha: 0.14),
                  trackHeight: 3,
                ),
                child: Slider(
                  value: _sliderValue,
                  min: 100,
                  max: 1200,
                  divisions: 11,
                  label: widget.valueLabel,
                  onChanged: (value) {
                    setState(() {
                      _sliderValue = value;
                    });
                  },
                  onChangeEnd: (value) {
                    final normalized = _normalizedSliderMs(value);
                    setState(() {
                      _sliderValue = normalized.toDouble();
                    });
                    widget.onChanged(normalized);
                  },
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _SettingsFoldersCard extends StatefulWidget {
  const _SettingsFoldersCard({
    required this.folders,
    required this.emptyText,
    required this.removeLabel,
    required this.onRemove,
  });

  final List<String> folders;
  final String emptyText;
  final String removeLabel;
  final Future<void> Function(String folder) onRemove;

  @override
  State<_SettingsFoldersCard> createState() => _SettingsFoldersCardState();
}

class _SettingsFoldersCardState extends State<_SettingsFoldersCard> {
  final Map<String, String> _folderSizes = {};

  @override
  void initState() {
    super.initState();
    _computeFolderSizes();
  }

  @override
  void didUpdateWidget(_SettingsFoldersCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.folders.join('|') != widget.folders.join('|')) {
      _folderSizes.clear();
      _computeFolderSizes();
    }
  }

  Future<void> _computeFolderSizes() async {
    for (final folder in widget.folders) {
      if (_folderSizes.containsKey(folder)) continue;
      final size = await _getDirectorySize(folder);
      if (mounted) {
        setState(() => _folderSizes[folder] = _formatBytes(size));
      }
    }
  }

  Future<int> _getDirectorySize(String path) async {
    int total = 0;
    try {
      await for (final entity in Directory(
        path,
      ).list(recursive: true, followLinks: false)) {
        if (entity is File) {
          try {
            total += await entity.length();
          } catch (_) {}
        }
      }
    } catch (_) {}
    return total;
  }

  String _formatBytes(int bytes) {
    if (bytes <= 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    int i = 0;
    double size = bytes.toDouble();
    while (size >= 1024 && i < units.length - 1) {
      size /= 1024;
      i++;
    }
    return '${size.toStringAsFixed(i == 0 ? 0 : 1)} ${units[i]}';
  }

  @override
  Widget build(BuildContext context) {
    final folders = widget.folders;
    final contentHeight = folders.isEmpty
        ? 124.0
        : (folders.length * 56.0 + (folders.length - 1) * 1.0).clamp(
            124.0,
            220.0,
          );

    return Container(
      constraints: BoxConstraints(minHeight: 124, maxHeight: contentHeight),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(12),
        color: Colors.white.withValues(alpha: 0.04),
        border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
      ),
      child: folders.isEmpty
          ? Center(
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 20),
                child: Text(
                  widget.emptyText,
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.white.withValues(alpha: 0.62)),
                ),
              ),
            )
          : ListView.separated(
              shrinkWrap: true,
              physics: const ClampingScrollPhysics(),
              key: ValueKey(folders.join('|')),
              itemCount: folders.length,
              separatorBuilder: (_, _) => Divider(
                color: Colors.white.withValues(alpha: 0.08),
                height: 1,
              ),
              itemBuilder: (_, index) {
                final folder = folders[index];
                return ListTile(
                  dense: true,
                  leading: Icon(
                    Icons.folder_open_rounded,
                    color: Colors.white.withValues(alpha: 0.72),
                  ),
                  title: Text(
                    folder,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  subtitle: Text(
                    _folderSizes[folder] ?? '...',
                    style: TextStyle(
                      fontSize: 12,
                      color: Colors.white.withValues(alpha: 0.45),
                    ),
                  ),
                  trailing: IconButton(
                    tooltip: widget.removeLabel,
                    onPressed: () => widget.onRemove(folder),
                    icon: const Icon(Icons.delete_outline_rounded),
                  ),
                );
              },
            ),
    );
  }
}

class _GlassSelectEntry<T> {
  const _GlassSelectEntry({required this.value, required this.label});

  final T value;
  final String label;
}

class _GlassSelectField<T> extends StatefulWidget {
  const _GlassSelectField({
    super.key,
    required this.value,
    required this.items,
    required this.onChanged,
    required this.lowEffects,
    this.maxMenuHeight = 280,
  });

  final T value;
  final List<_GlassSelectEntry<T>> items;
  final ValueChanged<T> onChanged;
  final bool lowEffects;
  final double maxMenuHeight;

  @override
  State<_GlassSelectField<T>> createState() => _GlassSelectFieldState<T>();
}

class _GlassSelectFieldState<T> extends State<_GlassSelectField<T>> {
  bool _isOpen = false;

  void _toggleMenu() {
    if (widget.items.isEmpty) return;
    setState(() {
      _isOpen = !_isOpen;
    });
  }

  void _closeMenu() {
    if (!_isOpen) return;
    setState(() {
      _isOpen = false;
    });
  }

  void _selectValue(T value) {
    _closeMenu();
    if (value == widget.value) return;
    widget.onChanged(value);
  }

  @override
  Widget build(BuildContext context) {
    final selectedEntry = widget.items.cast<_GlassSelectEntry<T>?>().firstWhere(
      (entry) => entry?.value == widget.value,
      orElse: () => widget.items.isEmpty ? null : widget.items.first,
    );

    final foreground = Colors.white.withValues(alpha: 0.92);
    final border = Colors.white.withValues(alpha: 0.10);

    const itemHeight = 50.0;

    return TapRegion(
      onTapOutside: (_) => _closeMenu(),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Material(
            color: Colors.transparent,
            child: InkWell(
              onTap: _toggleMenu,
              borderRadius: BorderRadius.circular(14),
              child: Ink(
                height: 48,
                padding: const EdgeInsets.symmetric(horizontal: 14),
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(14),
                  color: const Color(0xFF0D1526).withValues(alpha: 0.28),
                  border: Border.all(color: border),
                ),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        selectedEntry?.label ?? '',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          color: foreground,
                          fontSize: 14,
                          fontWeight: FontWeight.w500,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    AnimatedRotation(
                      turns: _isOpen ? 0.5 : 0,
                      duration: const Duration(milliseconds: 220),
                      curve: Curves.easeOutCubic,
                      child: Icon(
                        Icons.keyboard_arrow_down_rounded,
                        size: 20,
                        color: Colors.white.withValues(alpha: 0.72),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
          ClipRect(
            child: AnimatedSize(
              duration: const Duration(milliseconds: 240),
              curve: Curves.easeOutCubic,
              alignment: Alignment.topCenter,
              child: !_isOpen
                  ? const SizedBox.shrink()
                  : Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: GlassPanel(
                        lowEffects: widget.lowEffects,
                        radius: 18,
                        padding: const EdgeInsets.all(8),
                        child: ConstrainedBox(
                          constraints: BoxConstraints(
                            maxHeight: widget.maxMenuHeight,
                          ),
                          child: ListView.separated(
                            padding: EdgeInsets.zero,
                            shrinkWrap: true,
                            physics: const ClampingScrollPhysics(),
                            itemCount: widget.items.length,
                            separatorBuilder: (context, index) =>
                                const SizedBox(height: 6),
                            itemBuilder: (context, index) {
                              final item = widget.items[index];
                              final selected = item.value == widget.value;
                              return Material(
                                color: Colors.transparent,
                                child: InkWell(
                                  onTap: () => _selectValue(item.value),
                                  borderRadius: BorderRadius.circular(12),
                                  child: Ink(
                                    height: itemHeight,
                                    padding: const EdgeInsets.symmetric(
                                      horizontal: 12,
                                    ),
                                    decoration: BoxDecoration(
                                      borderRadius: BorderRadius.circular(12),
                                      color: selected
                                          ? Colors.white.withValues(alpha: 0.12)
                                          : Colors.white.withValues(
                                              alpha: 0.035,
                                            ),
                                      border: Border.all(
                                        color: selected
                                            ? Colors.white.withValues(
                                                alpha: 0.20,
                                              )
                                            : Colors.white.withValues(
                                                alpha: 0.05,
                                              ),
                                      ),
                                    ),
                                    child: Row(
                                      children: [
                                        Expanded(
                                          child: Text(
                                            item.label,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: TextStyle(
                                              color: Colors.white.withValues(
                                                alpha: selected ? 0.96 : 0.84,
                                              ),
                                              fontSize: 14,
                                              fontWeight: selected
                                                  ? FontWeight.w600
                                                  : FontWeight.w500,
                                            ),
                                          ),
                                        ),
                                        if (selected)
                                          Icon(
                                            Icons.check_rounded,
                                            size: 18,
                                            color: Colors.white.withValues(
                                              alpha: 0.90,
                                            ),
                                          ),
                                      ],
                                    ),
                                  ),
                                ),
                              );
                            },
                          ),
                        ),
                      ),
                    ),
            ),
          ),
        ],
      ),
    );
  }
}

class _GlassPlayAllButton extends StatelessWidget {
  const _GlassPlayAllButton({
    required this.label,
    required this.enabled,
    required this.onPressed,
  });

  final String label;
  final bool enabled;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final foreground = Colors.white.withValues(alpha: enabled ? 0.94 : 0.42);
    final border = Colors.white.withValues(alpha: enabled ? 0.16 : 0.08);
    final fill = enabled
        ? const Color(0xFF0F1A2F).withValues(alpha: 0.26)
        : Colors.white.withValues(alpha: 0.03);

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: enabled ? onPressed : null,
        borderRadius: BorderRadius.circular(999),
        child: Ink(
          height: 42,
          padding: const EdgeInsets.symmetric(horizontal: 16),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(999),
            color: fill,
            border: Border.all(color: border),
            boxShadow: enabled
                ? [
                    BoxShadow(
                      color: Colors.black.withValues(alpha: 0.12),
                      blurRadius: 16,
                      offset: const Offset(0, 6),
                    ),
                  ]
                : null,
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.play_arrow_rounded, size: 18, color: foreground),
              const SizedBox(width: 8),
              Text(
                label,
                style: TextStyle(
                  color: foreground,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _IdleTopBarTextField extends StatefulWidget {
  const _IdleTopBarTextField({
    required this.initialValue,
    required this.label,
    required this.hint,
    required this.onSubmitted,
  });

  final String initialValue;
  final String label;
  final String hint;
  final Future<void> Function(String value) onSubmitted;

  @override
  State<_IdleTopBarTextField> createState() => _IdleTopBarTextFieldState();
}

class _IdleTopBarTextFieldState extends State<_IdleTopBarTextField> {
  late final TextEditingController _controller;

  @override
  void initState() {
    super.initState();
    _controller = TextEditingController(text: widget.initialValue);
  }

  @override
  void didUpdateWidget(covariant _IdleTopBarTextField oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.initialValue != oldWidget.initialValue &&
        widget.initialValue != _controller.text) {
      _controller.text = widget.initialValue;
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    await widget.onSubmitted(_controller.text.trim());
  }

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: _controller,
      onSubmitted: (_) => _submit(),
      onTapOutside: (_) => _submit(),
      decoration: InputDecoration(
        labelText: widget.label,
        hintText: widget.hint,
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 12,
          vertical: 10,
        ),
      ),
    );
  }
}

class _TrackCover extends StatelessWidget {
  const _TrackCover({
    required this.track,
    required this.isActive,
    required this.coverBytes,
  });

  final Track track;
  final bool isActive;
  final Uint8List? coverBytes;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 44,
      height: 44,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(8),
        child: Stack(
          fit: StackFit.expand,
          children: [
            _CoverImage(coverPath: track.coverPath, coverBytes: coverBytes),
            if (isActive)
              Container(
                color: Colors.black.withValues(alpha: 0.34),
                child: const Icon(
                  Icons.graphic_eq_rounded,
                  color: Colors.white,
                  size: 20,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _NowPlayingInfo extends StatelessWidget {
  const _NowPlayingInfo({
    required this.track,
    required this.t,
    required this.duration,
    required this.coverBytes,
    this.onTap,
  });

  final Track? track;
  final AppStrings t;
  final Duration? duration;
  final Uint8List? coverBytes;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final body = Row(
      children: [
        SizedBox(
          width: 58,
          height: 58,
          child: ClipRRect(
            borderRadius: BorderRadius.circular(10),
            child: _CoverImage(
              coverPath: track?.coverPath,
              coverBytes: coverBytes,
            ),
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                track?.title ?? t.noTrackSelected,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 3),
              Text(
                track == null
                    ? '--'
                    : '${track!.artist} - ${_durationToText(duration)}',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 12,
                  color: Colors.white.withValues(alpha: 0.66),
                ),
              ),
            ],
          ),
        ),
      ],
    );

    if (onTap == null) return body;
    return MouseRegion(
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        onTap: onTap,
        behavior: HitTestBehavior.opaque,
        child: body,
      ),
    );
  }

  String _durationToText(Duration? d) {
    if (d == null || d <= Duration.zero) return '--:--';
    final m = d.inMinutes.remainder(60).toString().padLeft(2, '0');
    final s = d.inSeconds.remainder(60).toString().padLeft(2, '0');
    final h = d.inHours;
    if (h > 0) return '${h.toString().padLeft(2, '0')}:$m:$s';
    return '$m:$s';
  }
}

class _PlaybackQueueTrackTile extends StatefulWidget {
  const _PlaybackQueueTrackTile({
    required this.track,
    required this.index,
    required this.isActive,
    required this.coverBytes,
    required this.onTap,
    required this.onRemove,
  });

  final Track track;
  final int index;
  final bool isActive;
  final Uint8List? coverBytes;
  final VoidCallback onTap;
  final VoidCallback onRemove;

  @override
  State<_PlaybackQueueTrackTile> createState() =>
      _PlaybackQueueTrackTileState();
}

class _PlaybackQueueTrackTileState extends State<_PlaybackQueueTrackTile> {
  bool _hovering = false;

  @override
  Widget build(BuildContext context) {
    final active = widget.isActive;

    return MouseRegion(
      onEnter: (_) => setState(() => _hovering = true),
      onExit: (_) => setState(() => _hovering = false),
      child: Material(
        color: active
            ? const Color(0xFF39C0FF).withValues(alpha: 0.18)
            : Colors.white.withValues(alpha: 0.04),
        borderRadius: BorderRadius.circular(12),
        child: InkWell(
          borderRadius: BorderRadius.circular(12),
          onTap: widget.onTap,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(8, 8, 8, 8),
            child: Row(
              children: [
                SizedBox(
                  width: 20,
                  child: Text(
                    '${widget.index + 1}',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: Colors.white.withValues(
                        alpha: active ? 0.94 : 0.54,
                      ),
                      fontSize: 12,
                      fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                _TrackCover(
                  track: widget.track,
                  isActive: active,
                  coverBytes: widget.coverBytes,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        widget.track.title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          fontWeight: active
                              ? FontWeight.w700
                              : FontWeight.w600,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        widget.track.artist,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          color: Colors.white.withValues(alpha: 0.66),
                          fontSize: 12,
                        ),
                      ),
                    ],
                  ),
                ),
                SizedBox(
                  width: 40,
                  child: IgnorePointer(
                    ignoring: !_hovering,
                    child: AnimatedOpacity(
                      opacity: _hovering ? 1 : 0,
                      duration: const Duration(milliseconds: 180),
                      curve: Curves.easeOutCubic,
                      child: IconButton(
                        onPressed: widget.onRemove,
                        icon: Icon(
                          Icons.close_rounded,
                          size: 18,
                          color: Colors.redAccent.withValues(alpha: 0.92),
                        ),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _CoverImage extends ConsumerWidget {
  const _CoverImage({required this.coverPath, required this.coverBytes});

  final String? coverPath;
  final Uint8List? coverBytes;

  bool get _isNetworkPath {
    final p = coverPath?.trim() ?? '';
    return p.startsWith('http://') || p.startsWith('https://');
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (coverBytes != null && coverBytes!.isNotEmpty) {
      return Image.memory(
        coverBytes!,
        fit: BoxFit.cover,
        gaplessPlayback: true,
        errorBuilder: (_, _, _) => _fallbackImage(ref),
      );
    }
    return _fallbackImage(ref);
  }

  Widget _fallbackImage(WidgetRef ref) {
    final path = coverPath;
    if (path != null && path.isNotEmpty) {
      if (_isNetworkPath) {
        return OnlineCoverImage(
          coverCache: ref.read(onlineCoverCacheProvider),
          cacheKey: path,
          coverUrl: path,
        );
      }
      if (File(path).existsSync()) {
        return Image.file(
          File(path),
          fit: BoxFit.cover,
          errorBuilder: (_, _, _) => _placeholder(),
        );
      }
    }
    return _placeholder();
  }

  Widget _placeholder() {
    return Container(
      decoration: const BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xFF1C2A46), Color(0xFF23365A)],
        ),
      ),
      child: const Icon(Icons.music_note_rounded, color: Colors.white70),
    );
  }
}

class _TrackDetailsPage extends ConsumerWidget {
  const _TrackDetailsPage({
    required this.track,
    required this.duration,
    required this.coverBytes,
    required this.onReveal,
  });

  final Track track;
  final Duration? duration;
  final Uint8List? coverBytes;
  final Future<void> Function() onReveal;

  Future<void> _deleteTrack(
    BuildContext context,
    WidgetRef ref,
    AppStrings t,
    bool lowEffects,
  ) async {
    final decision = await showDialog<_TrackDeleteDecision>(
      context: context,
      barrierColor: Colors.black.withValues(alpha: 0.34),
      builder: (_) => _TrackDeleteDialog(t: t, lowEffects: lowEffects),
    );
    if (decision == null || !context.mounted) return;

    try {
      await ref
          .read(libraryProvider.notifier)
          .removeTrackFromLibrary(
            track,
            deleteSourceFile: decision.deleteSourceFile,
          );
      if (!context.mounted) return;
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            decision.deleteSourceFile
                ? t.trackRemovedAndDeleted
                : t.trackRemoved,
          ),
        ),
      );
    } catch (error) {
      if (!context.mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text('${t.deleteTrack}: $error')));
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final settings = ref.watch(appSettingsProvider);
    final library = ref.watch(libraryProvider);
    final t = AppStrings(settings.language);

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Stack(
        children: [
          Positioned.fill(
            child: DecoratedBox(
              decoration: const BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                  colors: [
                    Color(0x24090F1D),
                    Color(0x240C1323),
                    Color(0x240E1526),
                  ],
                ),
              ),
              child: Padding(
                padding: const EdgeInsets.fromLTRB(20, 58, 20, 20),
                child: GlassPanel(
                  lowEffects: library.lowEffects,
                  padding: const EdgeInsets.all(24),
                  child: FutureBuilder<AudioFileDetails>(
                    future: readAudioFileDetails(
                      track,
                      fallbackDuration: duration,
                    ),
                    builder: (context, snapshot) {
                      final details =
                          snapshot.data ??
                          AudioFileDetails(
                            durationLabel: _formatDuration(duration),
                            trackNumberLabel: '--',
                            bitrateLabel: '--',
                            sampleRateLabel: '--',
                            path: track.path,
                          );

                      return Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              IconButton(
                                tooltip: t.back,
                                onPressed: () =>
                                    Navigator.of(context).maybePop(),
                                icon: const Icon(Icons.arrow_back_rounded),
                              ),
                              const SizedBox(width: 6),
                              Text(
                                t.detailsTitle,
                                style: const TextStyle(
                                  fontSize: 22,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 18),
                          Expanded(
                            child: SingleChildScrollView(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Row(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      SizedBox(
                                        width: 156,
                                        height: 156,
                                        child: ClipRRect(
                                          borderRadius: BorderRadius.circular(
                                            16,
                                          ),
                                          child: _CoverImage(
                                            coverPath: track.coverPath,
                                            coverBytes: coverBytes,
                                          ),
                                        ),
                                      ),
                                      const SizedBox(width: 20),
                                      Expanded(
                                        child: Padding(
                                          padding: const EdgeInsets.only(
                                            top: 6,
                                          ),
                                          child: Column(
                                            crossAxisAlignment:
                                                CrossAxisAlignment.start,
                                            children: [
                                              Text(
                                                track.title,
                                                style: const TextStyle(
                                                  fontSize: 28,
                                                  fontWeight: FontWeight.w700,
                                                  height: 1.1,
                                                ),
                                              ),
                                              const SizedBox(height: 10),
                                              Text(
                                                track.artist,
                                                style: TextStyle(
                                                  fontSize: 16,
                                                  color: Colors.white
                                                      .withValues(alpha: 0.76),
                                                ),
                                              ),
                                            ],
                                          ),
                                        ),
                                      ),
                                    ],
                                  ),
                                  const SizedBox(height: 26),
                                  _TrackDetailsItem(
                                    label: t.duration,
                                    value: details.durationLabel,
                                  ),
                                  _TrackDetailsItem(
                                    label: t.audioTrack,
                                    value: details.trackNumberLabel,
                                  ),
                                  _TrackDetailsItem(
                                    label: t.bitrate,
                                    value: details.bitrateLabel,
                                  ),
                                  _TrackDetailsItem(
                                    label: t.sampleRate,
                                    value: details.sampleRateLabel,
                                  ),
                                  _TrackDetailsItem(
                                    label: t.pathLabel,
                                    value: details.path,
                                    action: Row(
                                      mainAxisSize: MainAxisSize.min,
                                      children: [
                                        Tooltip(
                                          message: t.revealInExplorer,
                                          child: IconButton(
                                            onPressed: onReveal,
                                            icon: SvgPicture.string(
                                              _folderRevealSvg,
                                              width: 19,
                                              height: 19,
                                              colorFilter:
                                                  const ColorFilter.mode(
                                                    Colors.white,
                                                    BlendMode.srcIn,
                                                  ),
                                            ),
                                          ),
                                        ),
                                        Tooltip(
                                          message: t.deleteTrack,
                                          child: IconButton(
                                            onPressed: () => _deleteTrack(
                                              context,
                                              ref,
                                              t,
                                              library.lowEffects,
                                            ),
                                            icon: SvgPicture.string(
                                              _deleteTrackSvg,
                                              width: 18,
                                              height: 18,
                                              colorFilter:
                                                  const ColorFilter.mode(
                                                    Color(0xFFFF6B6B),
                                                    BlendMode.srcIn,
                                                  ),
                                            ),
                                          ),
                                        ),
                                      ],
                                    ),
                                    selectable: true,
                                  ),
                                  if (snapshot.connectionState ==
                                      ConnectionState.waiting) ...[
                                    const SizedBox(height: 10),
                                    Row(
                                      children: [
                                        SizedBox(
                                          width: 16,
                                          height: 16,
                                          child: CircularProgressIndicator(
                                            strokeWidth: 2,
                                            color: Colors.white.withValues(
                                              alpha: 0.82,
                                            ),
                                          ),
                                        ),
                                        const SizedBox(width: 10),
                                        Text(
                                          t.loading,
                                          style: TextStyle(
                                            color: Colors.white.withValues(
                                              alpha: 0.72,
                                            ),
                                          ),
                                        ),
                                      ],
                                    ),
                                  ],
                                ],
                              ),
                            ),
                          ),
                        ],
                      );
                    },
                  ),
                ),
              ),
            ),
          ),
          const Positioned(
            left: 0,
            top: 0,
            right: 0,
            child: WindowTopBar(showBrand: false, showLyricBox: false),
          ),
        ],
      ),
    );
  }

  String _formatDuration(Duration? value) {
    if (value == null || value <= Duration.zero) {
      return '--';
    }

    final minutes = value.inMinutes.remainder(60).toString().padLeft(2, '0');
    final seconds = value.inSeconds.remainder(60).toString().padLeft(2, '0');
    final hours = value.inHours;
    if (hours > 0) {
      return '${hours.toString().padLeft(2, '0')}:$minutes:$seconds';
    }
    return '$minutes:$seconds';
  }
}

class _TrackDeleteDecision {
  const _TrackDeleteDecision({required this.deleteSourceFile});

  final bool deleteSourceFile;
}

class _TrackDeleteDialog extends StatefulWidget {
  const _TrackDeleteDialog({required this.t, required this.lowEffects});

  final AppStrings t;
  final bool lowEffects;

  @override
  State<_TrackDeleteDialog> createState() => _TrackDeleteDialogState();
}

class _TrackDeleteDialogState extends State<_TrackDeleteDialog> {
  bool _deleteSourceFile = false;

  @override
  Widget build(BuildContext context) {
    final blur = widget.lowEffects ? 10.0 : 18.0;

    return Dialog(
      backgroundColor: Colors.transparent,
      insetPadding: const EdgeInsets.symmetric(horizontal: 42, vertical: 36),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(24),
        child: BackdropFilter(
          filter: ui.ImageFilter.blur(sigmaX: blur, sigmaY: blur),
          child: Container(
            width: 420,
            decoration: BoxDecoration(
              color: const Color(0xFF0B1220).withValues(alpha: 0.24),
              borderRadius: BorderRadius.circular(24),
              border: Border.all(color: Colors.white.withValues(alpha: 0.18)),
              boxShadow: [
                BoxShadow(
                  color: Colors.black.withValues(alpha: 0.18),
                  blurRadius: 28,
                  offset: const Offset(0, 14),
                ),
              ],
            ),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(22, 20, 22, 20),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    widget.t.deleteTrack,
                    style: const TextStyle(
                      fontSize: 19,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: 14),
                  Text(
                    widget.t.removeFromListPrompt,
                    style: TextStyle(
                      fontSize: 14.5,
                      color: Colors.white.withValues(alpha: 0.88),
                    ),
                  ),
                  const SizedBox(height: 16),
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 12,
                      vertical: 10,
                    ),
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(14),
                      color: Colors.white.withValues(alpha: 0.05),
                      border: Border.all(
                        color: Colors.white.withValues(alpha: 0.12),
                      ),
                    ),
                    child: Row(
                      children: [
                        Checkbox(
                          value: _deleteSourceFile,
                          onChanged: (value) {
                            setState(() {
                              _deleteSourceFile = value ?? false;
                            });
                          },
                        ),
                        const SizedBox(width: 4),
                        Expanded(
                          child: Text(
                            widget.t.deleteSourceFileToo,
                            style: TextStyle(
                              color: Colors.white.withValues(alpha: 0.84),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 18),
                  Row(
                    children: [
                      Expanded(
                        child: OutlinedButton(
                          onPressed: () => Navigator.of(context).pop(),
                          style: OutlinedButton.styleFrom(
                            foregroundColor: Colors.white.withValues(
                              alpha: 0.84,
                            ),
                            side: BorderSide(
                              color: Colors.white.withValues(alpha: 0.18),
                            ),
                            padding: const EdgeInsets.symmetric(vertical: 13),
                          ),
                          child: Text(widget.t.confirmNo),
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: FilledButton(
                          onPressed: () => Navigator.of(context).pop(
                            _TrackDeleteDecision(
                              deleteSourceFile: _deleteSourceFile,
                            ),
                          ),
                          style: FilledButton.styleFrom(
                            backgroundColor: const Color(0xFFD74B4B),
                            foregroundColor: Colors.white,
                            padding: const EdgeInsets.symmetric(vertical: 13),
                          ),
                          child: Text(widget.t.confirmYes),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _TrackDetailsItem extends StatelessWidget {
  const _TrackDetailsItem({
    required this.label,
    required this.value,
    this.action,
    this.selectable = false,
  });

  final String label;
  final String value;
  final Widget? action;
  final bool selectable;

  @override
  Widget build(BuildContext context) {
    final valueWidget = selectable
        ? SelectableText(
            value,
            style: const TextStyle(fontSize: 14, height: 1.45),
          )
        : Text(value, style: const TextStyle(fontSize: 14, height: 1.45));

    return Container(
      width: double.infinity,
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(14),
        color: Colors.white.withValues(alpha: 0.04),
        border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(
                label,
                style: TextStyle(
                  fontWeight: FontWeight.w600,
                  color: Colors.white.withValues(alpha: 0.72),
                ),
              ),
              const Spacer(),
              if (action != null) ...[action!],
            ],
          ),
          const SizedBox(height: 8),
          valueWidget,
        ],
      ),
    );
  }
}

const String _folderRevealSvg = '''
<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <path d="M3.75 7.25C3.75 6.14543 4.64543 5.25 5.75 5.25H9.20711C9.73754 5.25 10.2463 5.46071 10.6213 5.83579L11.1642 6.37868C11.5393 6.75376 12.048 6.96447 12.5784 6.96447H18.25C19.3546 6.96447 20.25 7.8599 20.25 8.96447V9.25" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
  <path d="M5.75 9.75H18.25C19.3546 9.75 20.25 10.6454 20.25 11.75V16.25C20.25 17.3546 19.3546 18.25 18.25 18.25H5.75C4.64543 18.25 3.75 17.3546 3.75 16.25V11.75C3.75 10.6454 4.64543 9.75 5.75 9.75Z" stroke="currentColor" stroke-width="1.5"/>
  <path d="M13.25 12.25H17.75V16.75" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M12.75 17.25L17.75 12.25" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
</svg>
''';

const String _deleteTrackSvg = '''
<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <path d="M9.25 4.75H14.75L15.25 6.25H18.25" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M5.75 6.25H18.25" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
  <path d="M7.25 8.25L7.95 17.32C8.01 18.11 8.67 18.72 9.46 18.72H14.54C15.33 18.72 15.99 18.11 16.05 17.32L16.75 8.25" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
  <path d="M10 10.75V15.25" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
  <path d="M14 10.75V15.25" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
</svg>
''';

class _PlaybackModeButton extends StatelessWidget {
  const _PlaybackModeButton({
    required this.mode,
    required this.onPressed,
    required this.t,
  });

  final PlaybackMode mode;
  final VoidCallback onPressed;
  final AppStrings t;

  @override
  Widget build(BuildContext context) {
    final iconPath = switch (mode) {
      PlaybackMode.loop => 'assets/icons/mode_loop.svg',
      PlaybackMode.single => 'assets/icons/mode_single.svg',
      PlaybackMode.shuffle => 'assets/icons/mode_shuffle.svg',
    };
    final tooltip = switch (mode) {
      PlaybackMode.loop => t.listLoop,
      PlaybackMode.single => t.singleLoop,
      PlaybackMode.shuffle => t.shuffle,
    };

    return Tooltip(
      message: tooltip,
      child: SizedBox(
        width: 46,
        height: 38,
        child: TextButton(
          onPressed: onPressed,
          style: PrismWaveTheme.rectangularButtonStyle(
            padding: EdgeInsets.zero,
          ),
          child: AnimatedSwitcher(
            duration: const Duration(milliseconds: 180),
            switchInCurve: Curves.easeOutCubic,
            switchOutCurve: Curves.easeInCubic,
            transitionBuilder: (child, animation) {
              return FadeTransition(
                opacity: animation,
                child: ScaleTransition(
                  scale: Tween<double>(begin: 0.92, end: 1).animate(animation),
                  child: child,
                ),
              );
            },
            child: SvgPicture.asset(
              iconPath,
              key: ValueKey<String>('main-playback-mode-${mode.name}'),
              width: 18,
              height: 18,
              semanticsLabel: tooltip,
              colorFilter: const ColorFilter.mode(
                PrismWaveTheme.textSecondary,
                BlendMode.srcIn,
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _PlayerTransportButton extends StatelessWidget {
  const _PlayerTransportButton({
    required this.tooltip,
    required this.onPressed,
    required this.icon,
  });

  final String tooltip;
  final VoidCallback? onPressed;
  final Icon icon;

  @override
  Widget build(BuildContext context) {
    final iconColor = onPressed == null
        ? PrismWaveTheme.textMuted.withValues(alpha: 0.56)
        : PrismWaveTheme.textSecondary;

    return Tooltip(
      message: tooltip,
      child: SizedBox(
        width: 46,
        height: 38,
        child: TextButton(
          onPressed: onPressed,
          style: PrismWaveTheme.rectangularButtonStyle(
            padding: EdgeInsets.zero,
          ),
          child: Icon(icon.icon, size: 22, color: iconColor),
        ),
      ),
    );
  }
}

class _PlaybackQueueButton extends StatelessWidget {
  const _PlaybackQueueButton({
    required this.tooltip,
    required this.onPressed,
    required this.isActive,
  });

  final String tooltip;
  final VoidCallback? onPressed;
  final bool isActive;

  @override
  Widget build(BuildContext context) {
    return Tooltip(
      message: tooltip,
      child: SizedBox(
        width: 46,
        height: 38,
        child: TextButton(
          onPressed: onPressed,
          style: PrismWaveTheme.rectangularButtonStyle(
            selected: isActive,
            padding: EdgeInsets.zero,
          ),
          child: SvgPicture.asset(
            'assets/icons/player_queue.svg',
            width: 18,
            height: 18,
            semanticsLabel: tooltip,
            colorFilter: ColorFilter.mode(
              onPressed == null
                  ? PrismWaveTheme.textMuted.withValues(alpha: 0.56)
                  : (isActive
                        ? PrismWaveTheme.textPrimary
                        : PrismWaveTheme.textSecondary),
              BlendMode.srcIn,
            ),
          ),
        ),
      ),
    );
  }
}

class _PlaybackToggleButton extends StatelessWidget {
  const _PlaybackToggleButton({
    required this.onPressed,
    required this.isPlaying,
  });

  final VoidCallback? onPressed;
  final bool isPlaying;

  @override
  Widget build(BuildContext context) {
    final iconPath = isPlaying
        ? 'assets/icons/player_pause.svg'
        : 'assets/icons/player_play.svg';

    return SizedBox(
      width: 58,
      height: 42,
      child: TextButton(
        onPressed: onPressed,
        style: ButtonStyle(
          minimumSize: const WidgetStatePropertyAll(ui.Size(58, 42)),
          padding: const WidgetStatePropertyAll(EdgeInsets.zero),
          backgroundColor: WidgetStateProperty.resolveWith((states) {
            if (states.contains(WidgetState.disabled)) {
              return Colors.white.withValues(alpha: 0.045);
            }
            if (states.contains(WidgetState.pressed)) {
              return Colors.white.withValues(alpha: 0.20);
            }
            if (states.contains(WidgetState.hovered)) {
              return Colors.white.withValues(alpha: 0.16);
            }
            return Colors.white.withValues(alpha: 0.12);
          }),
          side: WidgetStateProperty.resolveWith((states) {
            return BorderSide(
              color: Colors.white.withValues(
                alpha: states.contains(WidgetState.hovered) ? 0.26 : 0.18,
              ),
            );
          }),
          shape: WidgetStatePropertyAll(
            RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(PrismWaveTheme.controlRadius),
            ),
          ),
          overlayColor: WidgetStatePropertyAll(
            Colors.white.withValues(alpha: 0.08),
          ),
        ),
        child: SvgPicture.asset(
          iconPath,
          width: 24,
          height: 24,
          colorFilter: ColorFilter.mode(
            onPressed == null
                ? PrismWaveTheme.textMuted.withValues(alpha: 0.56)
                : PrismWaveTheme.textPrimary,
            BlendMode.srcIn,
          ),
        ),
      ),
    );
  }
}
