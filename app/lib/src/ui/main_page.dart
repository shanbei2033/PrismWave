import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../i18n/app_strings.dart';
import '../models/audio_file_details.dart';
import '../models/app_language.dart';
import '../models/audio_output_mode.dart';
import '../models/playback_mode.dart';
import '../models/top_bar_idle_mode.dart';
import '../models/track.dart';
import '../providers.dart';
import '../services/audio_file_details_service.dart';
import '../state/library_state.dart';
import '../state/playback_state.dart';
import 'fullplay_page.dart';
import 'glass_panel.dart';
import 'window_top_bar.dart';

enum MainSection { library, albums, artists, favorites }

enum _TrackContextAction {
  favorite,
  reveal,
  details,
}

class PrismWaveHomePage extends ConsumerStatefulWidget {
  const PrismWaveHomePage({super.key});

  @override
  ConsumerState<PrismWaveHomePage> createState() => _PrismWaveHomePageState();
}

class _PrismWaveHomePageState extends ConsumerState<PrismWaveHomePage> {
  final _searchController = TextEditingController();
  MainSection _section = MainSection.library;
  String? _selectedAlbum;
  String? _selectedArtist;

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

  Future<void> _showTrackContextMenu({
    required BuildContext context,
    required Offset position,
    required Track track,
    required Duration? duration,
    required Uint8List? coverBytes,
    required AppStrings t,
  }) async {
    final isFavorite = ref.read(libraryProvider.notifier).isFavorite(track);
    final navigator = Navigator.of(context);
    final result = await showMenu<_TrackContextAction>(
      context: context,
      position: RelativeRect.fromLTRB(
        position.dx,
        position.dy,
        position.dx,
        position.dy,
      ),
      items: [
        PopupMenuItem<_TrackContextAction>(
          value: _TrackContextAction.favorite,
          child: Text(isFavorite ? t.uncollect : t.collect),
        ),
        PopupMenuItem<_TrackContextAction>(
          value: _TrackContextAction.reveal,
          child: Text(t.revealInExplorer),
        ),
        PopupMenuItem<_TrackContextAction>(
          value: _TrackContextAction.details,
          child: Text(t.details),
        ),
      ],
    );

    if (!mounted || result == null) return;
    switch (result) {
      case _TrackContextAction.favorite:
        await ref.read(libraryProvider.notifier).toggleFavorite(track);
        return;
      case _TrackContextAction.reveal:
        await _revealTrackInExplorer(track.path);
        return;
      case _TrackContextAction.details:
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
        return;
    }
  }

  Future<void> _revealTrackInExplorer(String path) async {
    final normalized = path.replaceAll('/', '\\');
    await Process.start('explorer.exe', ['/select,$normalized']);
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
              .ensureLyricsLoaded(next.currentTrack!),
        );
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
                padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
                child: Column(
                  children: [
                    Expanded(
                      child: Row(
                        children: [
                          SizedBox(
                            width: 260,
                            child: _buildSidebar(library: library, t: t),
                          ),
                          const SizedBox(width: 14),
                          Expanded(
                            child: Padding(
                              padding: const EdgeInsets.only(top: 32),
                              child: _buildSectionPanel(
                                library: library,
                                playback: playback,
                                t: t,
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

  Widget _buildSidebar({required LibraryState library, required AppStrings t}) {
    return GlassPanel(
      lowEffects: library.lowEffects,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Expanded(
                child: Text(
                  'PrismWave',
                  style: TextStyle(fontSize: 24, fontWeight: FontWeight.w700),
                ),
              ),
              IconButton(
                tooltip: t.settings,
                onPressed: _openSettings,
                icon: SvgPicture.asset(
                  'assets/icons/settings.svg',
                  width: 19,
                  height: 19,
                  colorFilter: const ColorFilter.mode(
                    Color(0xFFB9DEFF),
                    BlendMode.srcIn,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 18),
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
          const Spacer(),
          Text(
            '${t.folders}: ${library.libraryFolders.length}',
            style: TextStyle(color: Colors.white.withValues(alpha: 0.70)),
          ),
          Text(
            '${t.tracks}: ${library.tracks.length}',
            style: TextStyle(color: Colors.white.withValues(alpha: 0.70)),
          ),
          Text(
            '${t.favoriteCountLabel}: ${library.favoritePaths.length}',
            style: TextStyle(color: Colors.white.withValues(alpha: 0.70)),
          ),
        ],
      ),
    );
  }

  Widget _navButton({
    required MainSection section,
    required IconData icon,
    required String label,
  }) {
    final selected = _section == section;

    return Material(
      color: selected
          ? const Color(0xFF39C0FF).withValues(alpha: 0.16)
          : Colors.transparent,
      borderRadius: BorderRadius.circular(10),
      child: InkWell(
        borderRadius: BorderRadius.circular(10),
        onTap: () {
          setState(() {
            _section = section;
            _selectedAlbum = null;
            _selectedArtist = null;
          });
        },
        child: SizedBox(
          height: 44,
          child: Row(
            children: [
              const SizedBox(width: 12),
              Icon(icon, size: 20),
              const SizedBox(width: 10),
              Text(label, style: const TextStyle(fontSize: 15)),
            ],
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
    required String emptyMessage,
  }) {
    final libraryCtrl = ref.read(libraryProvider.notifier);
    final playbackCtrl = ref.read(playbackProvider.notifier);
    final playbackContext = playbackContextTracks ?? tracks;
    final useLibraryContext =
        forceLibraryContext && playbackContextTracks != null;

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
                : ListView.builder(
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
                          onSecondaryTapDown: (details) => _showTrackContextMenu(
                            context: context,
                            position: details.globalPosition,
                            track: track,
                            duration: duration,
                            coverBytes: coverBytes,
                            t: t,
                          ),
                          child: Material(
                            color: active
                                ? const Color(0xFF39C0FF).withValues(alpha: 0.16)
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
                      );
                    },
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

  Widget _buildAlbumsPanel({required LibraryState library, required AppStrings t}) {
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
                : GridView.builder(
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
                      final firstTrack = album.value.first;
                      final coverBytes = library.coverBytesOf(firstTrack);

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
                                      coverPath: firstTrack.coverPath,
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
                                    color: Colors.white.withValues(alpha: 0.66),
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
                : ListView.separated(
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
              FilledButton.tonalIcon(
                onPressed: tracks.isEmpty
                    ? null
                    : () => playbackCtrl.playFromPlaylist(tracks.first, tracks),
                icon: const Icon(Icons.play_arrow_rounded),
                label: Text(t.playAll),
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
                : ListView.builder(
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
                          onSecondaryTapDown: (details) => _showTrackContextMenu(
                            context: context,
                            position: details.globalPosition,
                            track: track,
                            duration: duration,
                            coverBytes: coverBytes,
                            t: t,
                          ),
                          child: Material(
                            color: active
                                ? const Color(0xFF39C0FF).withValues(alpha: 0.16)
                                : Colors.transparent,
                            borderRadius: BorderRadius.circular(10),
                            child: InkWell(
                              borderRadius: BorderRadius.circular(10),
                              onTap: () =>
                                  playbackCtrl.playFromPlaylist(track, tracks),
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
    final duration = playback.duration.inMilliseconds.toDouble();
    final position = playback.currentTime.inMilliseconds.toDouble();
    final safeDuration = duration > 0 ? duration : 1.0;
    final safePosition = position.clamp(0.0, safeDuration);

    return GlassPanel(
      lowEffects: library.lowEffects,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
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
                        IconButton(
                          onPressed: playback.hasTrack ? ctrl.previous : null,
                          icon: const Icon(Icons.skip_previous_rounded),
                        ),
                        const SizedBox(width: 8),
                        _PlaybackToggleButton(
                          onPressed: playback.hasTrack
                              ? ctrl.togglePlayPause
                              : null,
                          isPlaying: playback.isPlaying,
                        ),
                        const SizedBox(width: 8),
                        IconButton(
                          onPressed: playback.hasTrack ? ctrl.next : null,
                          icon: const Icon(Icons.skip_next_rounded),
                        ),
                        const SizedBox(width: 8),
                        _PlaybackModeButton(
                          t: t,
                          mode: playback.playbackMode,
                          onPressed: ctrl.cycleMode,
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
                          ),
                        ),
                        Expanded(
                          child: SliderTheme(
                            data: SliderTheme.of(context).copyWith(
                              activeTrackColor: Colors.white,
                              inactiveTrackColor: Colors.white.withValues(
                                alpha: 0.24,
                              ),
                              thumbColor: Colors.white,
                              overlayColor: Colors.white.withValues(
                                alpha: 0.14,
                              ),
                              trackHeight: 2.6,
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
                          child: Text(_formatDuration(playback.duration)),
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
                const Icon(Icons.volume_up_rounded, size: 18),
                Expanded(
                  child: SliderTheme(
                    data: SliderTheme.of(context).copyWith(
                      activeTrackColor: Colors.white,
                      inactiveTrackColor: Colors.white.withValues(alpha: 0.24),
                      thumbColor: Colors.white,
                      overlayColor: Colors.white.withValues(alpha: 0.14),
                      trackHeight: 2.6,
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
    await ref.read(libraryProvider.notifier).ensureLyricsLoaded(track);
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

  Future<void> _openSettings() async {
    await showDialog<void>(
      context: context,
      builder: (_) => const _SettingsDialog(),
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
}

class _SettingsDialog extends ConsumerWidget {
  const _SettingsDialog();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final appSettings = ref.watch(appSettingsProvider);
    final appSettingsController = ref.read(appSettingsProvider.notifier);
    final t = AppStrings(appSettings.language);
    final library = ref.watch(libraryProvider);
    final controller = ref.read(libraryProvider.notifier);
    final playback = ref.watch(playbackProvider);
    final playbackController = ref.read(playbackProvider.notifier);

    return Dialog(
      backgroundColor: const Color(0xFF0C1528),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 760, maxHeight: 640),
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(
                    t.settings,
                    style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w700),
                  ),
                  const Spacer(),
                  IconButton(
                    onPressed: () => Navigator.of(context).pop(),
                    icon: const Icon(Icons.close_rounded),
                  ),
                ],
              ),
              const SizedBox(height: 12),
              Text(
                t.folderSection,
                style: TextStyle(
                  fontSize: 16,
                  color: Colors.white.withValues(alpha: 0.9),
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  FilledButton.icon(
                    onPressed: library.isScanning
                        ? null
                        : controller.addMusicFolder,
                    icon: const Icon(Icons.add_rounded),
                    label: Text(t.addMusicFolder),
                  ),
                  const SizedBox(width: 10),
                  OutlinedButton.icon(
                    onPressed: library.isScanning
                        ? null
                        : controller.rescanAllFolders,
                    icon: const Icon(Icons.refresh_rounded),
                    label: Text(t.rescanAll),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Expanded(
                child: Container(
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(12),
                    color: Colors.white.withValues(alpha: 0.04),
                    border: Border.all(
                      color: Colors.white.withValues(alpha: 0.08),
                    ),
                  ),
                  child: library.libraryFolders.isEmpty
                      ? Center(
                          child: Text(
                            t.noFolderConfigured,
                            style: TextStyle(
                              color: Colors.white.withValues(alpha: 0.62),
                            ),
                          ),
                        )
                      : ListView.separated(
                          itemCount: library.libraryFolders.length,
                          separatorBuilder: (_, _) => Divider(
                            color: Colors.white.withValues(alpha: 0.08),
                            height: 1,
                          ),
                          itemBuilder: (_, index) {
                            final folder = library.libraryFolders[index];
                            return ListTile(
                              title: Text(
                                folder,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                              ),
                              trailing: IconButton(
                                tooltip: t.remove,
                                onPressed: () =>
                                    controller.removeMusicFolder(folder),
                                icon: const Icon(Icons.delete_outline_rounded),
                              ),
                            );
                          },
                        ),
                ),
              ),
              const SizedBox(height: 8),
              Text(
                t.languageTitle,
                style: TextStyle(
                  color: Colors.white.withValues(alpha: 0.92),
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 6),
              DropdownButtonFormField<AppLanguage>(
                initialValue: appSettings.language,
                isExpanded: true,
                onChanged: (value) {
                  if (value == null) return;
                  appSettingsController.setLanguage(value);
                },
                items: AppLanguage.values
                    .map(
                      (lang) => DropdownMenuItem<AppLanguage>(
                        value: lang,
                        child: Text(t.languageLabel(lang)),
                      ),
                    )
                    .toList(growable: false),
                decoration: InputDecoration(
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(12),
                  ),
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: 12,
                    vertical: 10,
                  ),
                ),
              ),
              const SizedBox(height: 8),
              Text(
                t.topBarDisplayTitle,
                style: TextStyle(
                  color: Colors.white.withValues(alpha: 0.92),
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 6),
              DropdownButtonFormField<TopBarIdleMode>(
                initialValue: appSettings.topBarIdleMode,
                isExpanded: true,
                onChanged: (value) {
                  if (value == null) return;
                  appSettingsController.setTopBarIdleMode(value);
                },
                items: TopBarIdleMode.values
                    .map(
                      (mode) => DropdownMenuItem<TopBarIdleMode>(
                        value: mode,
                        child: Text(t.topBarIdleModeLabel(mode)),
                      ),
                    )
                    .toList(growable: false),
                decoration: InputDecoration(
                  labelText: t.topBarIdleModeTitle,
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(12),
                  ),
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: 12,
                    vertical: 10,
                  ),
                ),
              ),
              const SizedBox(height: 8),
              _IdleTopBarTextField(
                initialValue: appSettings.topBarIdleText,
                label: t.topBarCustomTextTitle,
                hint: t.topBarCustomTextHint,
                onSubmitted: appSettingsController.setTopBarIdleText,
              ),
              const SizedBox(height: 8),
              Text(
                t.audioOutputMode,
                style: TextStyle(
                  color: Colors.white.withValues(alpha: 0.92),
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 6),
              DropdownButtonFormField<AudioOutputMode>(
                initialValue: playback.audioOutputMode,
                isExpanded: true,
                onChanged: (value) {
                  if (value == null) return;
                  playbackController.setAudioOutputMode(value);
                },
                items: AudioOutputMode.values
                    .map(
                      (mode) => DropdownMenuItem<AudioOutputMode>(
                        value: mode,
                        child: Text(t.outputModeLabel(mode)),
                      ),
                    )
                    .toList(growable: false),
                decoration: InputDecoration(
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(12),
                  ),
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: 12,
                    vertical: 10,
                  ),
                ),
              ),
              const SizedBox(height: 6),
              Text(
                t.outputModeDescription(playback.audioOutputMode),
                style: TextStyle(
                  color: Colors.white.withValues(alpha: 0.66),
                  fontSize: 12,
                ),
              ),
              const SizedBox(height: 8),
              SwitchListTile(
                value: playback.developerMode,
                onChanged: playbackController.setDeveloperMode,
                contentPadding: EdgeInsets.zero,
                title: Text(t.developerMode),
                subtitle: Text(t.developerModeHint),
              ),
              if (playback.developerMode) ...[
                const SizedBox(height: 6),
                Row(
                  children: [
                    Text(
                      '${t.playbackLogs} (${playback.debugLogs.length})',
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.86),
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
                                  text: playback.debugLogs.join('\n'),
                                ),
                              );
                              if (!context.mounted) return;
                              ScaffoldMessenger.of(context).showSnackBar(
                                SnackBar(content: Text(t.logsCopied)),
                              );
                            },
                      icon: const Icon(Icons.copy_rounded, size: 16),
                      label: Text(t.copy),
                    ),
                    const SizedBox(width: 4),
                    TextButton.icon(
                      onPressed: playback.debugLogs.isEmpty
                          ? null
                          : playbackController.clearDebugLogs,
                      icon: const Icon(Icons.delete_sweep_rounded, size: 16),
                      label: Text(t.clear),
                    ),
                  ],
                ),
                const SizedBox(height: 6),
                SizedBox(
                  height: 140,
                  child: Container(
                    decoration: BoxDecoration(
                      color: Colors.black.withValues(alpha: 0.22),
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(
                        color: Colors.white.withValues(alpha: 0.10),
                      ),
                    ),
                    child: playback.debugLogs.isEmpty
                        ? Center(
                            child: Text(
                              t.noLogsHint,
                              style: TextStyle(
                                color: Colors.white.withValues(alpha: 0.62),
                                fontSize: 12,
                              ),
                            ),
                          )
                        : ListView.builder(
                            reverse: true,
                            itemCount: playback.debugLogs.length,
                            itemBuilder: (_, index) {
                              final line =
                                  playback.debugLogs[playback.debugLogs.length -
                                      1 -
                                      index];
                              return Padding(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 10,
                                  vertical: 4,
                                ),
                                child: Text(
                                  line,
                                  style: TextStyle(
                                    fontFamily: 'Consolas',
                                    fontSize: 11,
                                    height: 1.35,
                                    color: Colors.white.withValues(alpha: 0.84),
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
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
        ),
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

class _CoverImage extends StatelessWidget {
  const _CoverImage({required this.coverPath, required this.coverBytes});

  final String? coverPath;
  final Uint8List? coverBytes;

  @override
  Widget build(BuildContext context) {
    if (coverBytes != null && coverBytes!.isNotEmpty) {
      return Image.memory(
        coverBytes!,
        fit: BoxFit.cover,
        gaplessPlayback: true,
        errorBuilder: (_, _, _) => _placeholder(),
      );
    }

    if (coverPath != null && File(coverPath!).existsSync()) {
      return Image.file(
        File(coverPath!),
        fit: BoxFit.cover,
        errorBuilder: (_, _, _) => _placeholder(),
      );
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
                                onPressed: () => Navigator.of(context).maybePop(),
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
                                    crossAxisAlignment: CrossAxisAlignment.start,
                                    children: [
                                      SizedBox(
                                        width: 156,
                                        height: 156,
                                        child: ClipRRect(
                                          borderRadius: BorderRadius.circular(16),
                                          child: _CoverImage(
                                            coverPath: track.coverPath,
                                            coverBytes: coverBytes,
                                          ),
                                        ),
                                      ),
                                      const SizedBox(width: 20),
                                      Expanded(
                                        child: Padding(
                                          padding: const EdgeInsets.only(top: 6),
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
                                                  color: Colors.white.withValues(
                                                    alpha: 0.76,
                                                  ),
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
                                    action: Tooltip(
                                      message: t.revealInExplorer,
                                      child: IconButton(
                                        onPressed: onReveal,
                                        icon: SvgPicture.string(
                                          _folderRevealSvg,
                                          width: 19,
                                          height: 19,
                                          colorFilter: const ColorFilter.mode(
                                            Colors.white,
                                            BlendMode.srcIn,
                                          ),
                                        ),
                                      ),
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
        : Text(
            value,
            style: const TextStyle(fontSize: 14, height: 1.45),
          );

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
              if (action != null) ...[
                const SizedBox(width: 8),
                action!,
              ],
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

    return Tooltip(
      message: switch (mode) {
        PlaybackMode.loop => t.listLoop,
        PlaybackMode.single => t.singleLoop,
        PlaybackMode.shuffle => t.shuffle,
      },
      child: IconButton(
        onPressed: onPressed,
        icon: SvgPicture.asset(
          iconPath,
          width: 18,
          height: 18,
          colorFilter: const ColorFilter.mode(Colors.white, BlendMode.srcIn),
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

    return IconButton(
      onPressed: onPressed,
      iconSize: 28,
      icon: SvgPicture.asset(
        iconPath,
        width: 28,
        height: 28,
        colorFilter: ColorFilter.mode(
          Colors.white.withValues(alpha: onPressed == null ? 0.42 : 0.94),
          BlendMode.srcIn,
        ),
      ),
    );
  }
}

