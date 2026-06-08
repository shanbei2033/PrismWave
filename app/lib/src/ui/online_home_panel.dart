import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../i18n/app_strings.dart';
import '../models/online_recommendation.dart';
import '../providers.dart';
import '../services/online_media_cache_service.dart';
import '../state/app_settings_state.dart';
import '../state/online_state.dart';
import 'prismwave_theme.dart';

class OnlineHomePanel extends ConsumerStatefulWidget {
  const OnlineHomePanel({
    super.key,
    required this.t,
    this.onOpenTopPlaylist,
    this.onOpenAlbum,
  });

  final AppStrings t;

  /// Called when the user taps the "Today's Top 10" banner. The host widget
  /// (main_page) is responsible for routing into the detail panel.
  final VoidCallback? onOpenTopPlaylist;

  /// Called when the user taps an album card. The host widget routes into
  /// the album detail panel.
  final ValueChanged<OnlineAlbumCard>? onOpenAlbum;

  @override
  ConsumerState<OnlineHomePanel> createState() => _OnlineHomePanelState();
}

class _OnlineHomePanelState extends ConsumerState<OnlineHomePanel> {
  final OnlineMediaCacheService _coverCache = OnlineMediaCacheService();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      ref.read(onlineProvider.notifier).ensureHomeLoaded();
    });
  }

  @override
  void dispose() {
    _coverCache.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final t = widget.t;
    final settings = ref.watch(appSettingsProvider);
    final home = ref.watch(onlineProvider.select((s) => s.home));
    final canRefresh = home.status != OnlineHomeStatus.loading;

    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 16, 12, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: Row(
              children: [
                Text(
                  t.navHome,
                  style: const TextStyle(
                    color: PrismWaveTheme.textPrimary,
                    fontSize: 24,
                    fontWeight: FontWeight.w800,
                    letterSpacing: 0,
                  ),
                ),
                const Spacer(),
                Tooltip(
                  message: t.onlineHomeRetry,
                  child: SizedBox(
                    width: 46,
                    height: 40,
                    child: TextButton(
                      onPressed: canRefresh
                          ? _refreshHomeRecommendations
                          : null,
                      style: PrismWaveTheme.rectangularButtonStyle(
                        padding: EdgeInsets.zero,
                      ),
                      child: Icon(
                        Icons.refresh_rounded,
                        size: 20,
                        color: canRefresh
                            ? PrismWaveTheme.textSecondary
                            : PrismWaveTheme.textMuted.withValues(alpha: 0.56),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          Expanded(child: _buildContent(home, t, settings)),
        ],
      ),
    );
  }

  Widget _buildContent(
    OnlineHomeView home,
    AppStrings t,
    AppSettingsState settings,
  ) {
    if (home.status == OnlineHomeStatus.loading && home.data == null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const CircularProgressIndicator(
              strokeWidth: 2,
              color: PrismWaveTheme.accent,
            ),
            const SizedBox(height: 12),
            Text(
              t.onlineHomeLoading,
              style: const TextStyle(color: PrismWaveTheme.textSecondary),
            ),
          ],
        ),
      );
    }
    if (home.status == OnlineHomeStatus.failed && home.data == null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              t.onlineHomeFailed,
              style: const TextStyle(color: PrismWaveTheme.textSecondary),
            ),
            if (home.errorMessage.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text(
                  home.errorMessage,
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    color: PrismWaveTheme.textMuted.withValues(alpha: 0.9),
                    fontSize: 12,
                  ),
                ),
              ),
            const SizedBox(height: 14),
            TextButton.icon(
              onPressed: () => ref
                  .read(onlineProvider.notifier)
                  .ensureHomeLoaded(forceRefresh: true),
              style: PrismWaveTheme.rectangularButtonStyle(selected: true),
              icon: const Icon(Icons.refresh_rounded, size: 18),
              label: Text(t.onlineHomeRetry),
            ),
          ],
        ),
      );
    }

    final data = home.data;
    if (data == null) return const SizedBox.shrink();

    final topPlaylist = data.topPlaylist;
    final hasBanner = topPlaylist != null && widget.onOpenTopPlaylist != null;
    final albums = data.albumRecommendations;

    return CustomScrollView(
      slivers: [
        if (hasBanner)
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.only(right: 8, bottom: 24),
              child: _TopPlaylistBanner(
                t: t,
                playlist: topPlaylist,
                coverCache: _coverCache,
                onTap: widget.onOpenTopPlaylist!,
              ),
            ),
          ),
        if (albums.isNotEmpty)
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.only(right: 8, bottom: 28),
              child: _AlbumRow(
                t: t,
                albums: albums,
                coverCache: _coverCache,
                onOpenAlbum: _openAlbum,
              ),
            ),
          ),
        SliverList.separated(
          itemCount: data.sections.length,
          separatorBuilder: (_, _) => const SizedBox(height: 28),
          itemBuilder: (context, index) {
            final section = data.sections[index];
            return Padding(
              padding: const EdgeInsets.only(right: 8),
              child: _OnlineSection(
                t: t,
                settings: settings,
                section: section,
                coverCache: _coverCache,
                onPlay: (track) => _playSection(section, track),
              ),
            );
          },
        ),
        const SliverToBoxAdapter(child: SizedBox(height: 24)),
      ],
    );
  }

  Future<void> _playSection(
    OnlineSection section,
    OnlineTrackCandidate picked,
  ) async {
    await ref
        .read(onlineProvider.notifier)
        .playOnlineTrack(picked: picked, contextTracks: section.tracks);
  }

  Future<void> _openAlbum(OnlineAlbumCard album) async {
    final cb = widget.onOpenAlbum;
    if (cb != null) {
      cb(album);
      return;
    }
    // Fallback: if no router callback is wired, fall back to playing the
    // album directly so the UI stays usable.
    await ref.read(onlineProvider.notifier).playOnlineAlbum(album);
  }

  Future<void> _refreshHomeRecommendations() async {
    final result = await ref
        .read(onlineProvider.notifier)
        .refreshHomeRecommendations();
    if (!mounted) return;
    if (result == OnlineHomeRefreshResult.fresh) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          result == OnlineHomeRefreshResult.latestAvailable
              ? widget.t.onlineFetchTodayChartUsingLatest
              : widget.t.onlineFetchTodayChartFailed,
        ),
      ),
    );
  }
}

class _OnlineSection extends StatelessWidget {
  const _OnlineSection({
    required this.t,
    required this.settings,
    required this.section,
    required this.coverCache,
    required this.onPlay,
  });

  final AppStrings t;
  final AppSettingsState settings;
  final OnlineSection section;
  final OnlineMediaCacheService coverCache;
  final ValueChanged<OnlineTrackCandidate> onPlay;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(left: 2, right: 8),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      section.localizedTitle(settings.language),
                      style: const TextStyle(
                        color: PrismWaveTheme.textPrimary,
                        fontSize: 19,
                        fontWeight: FontWeight.w800,
                        letterSpacing: 0,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 12),
        LayoutBuilder(
          builder: (context, constraints) {
            const cardWidth = 168.0;
            const gap = 12.0;
            final maxCount = math.max(
              1,
              ((constraints.maxWidth + gap) / (cardWidth + gap)).floor(),
            );
            final visible = section.tracks.take(maxCount).toList();
            return Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (var i = 0; i < visible.length; i++) ...[
                  if (i > 0) const SizedBox(width: gap),
                  SizedBox(
                    width: cardWidth,
                    child: _OnlineTrackCard(
                      track: visible[i],
                      coverCache: coverCache,
                      onTap: () => onPlay(visible[i]),
                    ),
                  ),
                ],
              ],
            );
          },
        ),
      ],
    );
  }
}

class _OnlineTrackCard extends StatelessWidget {
  const _OnlineTrackCard({
    required this.track,
    required this.coverCache,
    required this.onTap,
  });

  final OnlineTrackCandidate track;
  final OnlineMediaCacheService coverCache;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 168,
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(PrismWaveTheme.cardRadius),
        clipBehavior: Clip.antiAlias,
        child: Ink(
          decoration: BoxDecoration(
            gradient: PrismWaveTheme.glassGradient(alpha: 0.18),
            borderRadius: BorderRadius.circular(PrismWaveTheme.cardRadius),
            border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
          ),
          child: InkWell(
            borderRadius: BorderRadius.circular(PrismWaveTheme.cardRadius),
            onTap: onTap,
            child: Padding(
              padding: const EdgeInsets.all(8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  DecoratedBox(
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(12),
                      boxShadow: [
                        BoxShadow(
                          color: Colors.black.withValues(alpha: 0.16),
                          blurRadius: 18,
                          offset: const Offset(0, 10),
                        ),
                      ],
                    ),
                    child: AspectRatio(
                      aspectRatio: 1,
                      child: ClipRRect(
                        borderRadius: BorderRadius.circular(12),
                        child: OnlineCoverImage(
                          coverCache: coverCache,
                          cacheKey: track.canonicalKey,
                          coverUrl: track.coverUrl,
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 9),
                  Text(
                    track.title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: PrismWaveTheme.textPrimary,
                      fontSize: 13,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: 3),
                  Text(
                    track.artist,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: PrismWaveTheme.textMuted,
                      fontSize: 12,
                    ),
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

class OnlineCoverImage extends StatefulWidget {
  const OnlineCoverImage({
    super.key,
    required this.coverCache,
    required this.cacheKey,
    required this.coverUrl,
  });

  final OnlineMediaCacheService coverCache;
  final String cacheKey;
  final String? coverUrl;

  @override
  State<OnlineCoverImage> createState() => _OnlineCoverImageState();
}

class _OnlineCoverImageState extends State<OnlineCoverImage> {
  Future<Uint8List?>? _future;

  @override
  void initState() {
    super.initState();
    _future = _loadFuture();
  }

  @override
  void didUpdateWidget(covariant OnlineCoverImage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.cacheKey != widget.cacheKey ||
        oldWidget.coverUrl != widget.coverUrl) {
      _future = _loadFuture();
    }
  }

  Future<Uint8List?>? _loadFuture() {
    final url = widget.coverUrl;
    if (url == null || url.isEmpty) return null;
    return widget.coverCache.loadCoverBytes(
      cacheKey: widget.cacheKey,
      coverUrl: url,
    );
  }

  @override
  Widget build(BuildContext context) {
    final future = _future;
    if (future == null) return _placeholder();
    return FutureBuilder<Uint8List?>(
      future: future,
      builder: (context, snapshot) {
        final bytes = snapshot.data;
        if (bytes != null && bytes.isNotEmpty) {
          return Image.memory(
            bytes,
            fit: BoxFit.cover,
            gaplessPlayback: true,
            errorBuilder: (_, _, _) => _placeholder(),
          );
        }
        return _placeholder();
      },
    );
  }

  Widget _placeholder() {
    return Container(
      decoration: BoxDecoration(
        gradient: PrismWaveTheme.glassGradient(alpha: 0.24),
      ),
      alignment: Alignment.center,
      child: Container(
        width: 48,
        height: 38,
        decoration: BoxDecoration(
          color: PrismWaveTheme.accent.withValues(alpha: 0.10),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(
            color: PrismWaveTheme.accent.withValues(alpha: 0.16),
          ),
        ),
        child: Icon(
          Icons.music_note_rounded,
          size: 24,
          color: PrismWaveTheme.accentSoft.withValues(alpha: 0.8),
        ),
      ),
    );
  }
}

class _TopPlaylistBanner extends StatelessWidget {
  const _TopPlaylistBanner({
    required this.t,
    required this.playlist,
    required this.coverCache,
    required this.onTap,
  });

  final AppStrings t;
  final OnlineSection playlist;
  final OnlineMediaCacheService coverCache;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tracks = playlist.tracks;
    final featuredCovers = tracks
        .where((track) => (track.coverUrl ?? '').isNotEmpty)
        .take(4)
        .toList(growable: false);

    return Material(
      color: Colors.transparent,
      borderRadius: BorderRadius.circular(18),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Container(
          height: 168,
          decoration: BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.centerLeft,
              end: Alignment.centerRight,
              colors: [
                PrismWaveTheme.surfaceStrong.withValues(alpha: 0.42),
                PrismWaveTheme.accentDeep.withValues(alpha: 0.22),
                PrismWaveTheme.surface.withValues(alpha: 0.34),
              ],
            ),
            borderRadius: BorderRadius.circular(18),
            border: Border.all(color: Colors.white.withValues(alpha: 0.10)),
            boxShadow: PrismWaveTheme.panelShadow(alpha: 0.08),
          ),
          padding: const EdgeInsets.fromLTRB(22, 18, 18, 18),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 8,
                        vertical: 3,
                      ),
                      decoration: BoxDecoration(
                        color: PrismWaveTheme.accent.withValues(alpha: 0.16),
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(
                          color: PrismWaveTheme.accent.withValues(alpha: 0.24),
                        ),
                      ),
                      child: Text(
                        t.onlineTopPlaylistBadge,
                        style: const TextStyle(
                          color: PrismWaveTheme.accentSoft,
                          fontSize: 11,
                          fontWeight: FontWeight.w800,
                          letterSpacing: 0,
                        ),
                      ),
                    ),
                    const SizedBox(height: 10),
                    Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Flexible(
                          child: Text(
                            t.onlineTopPlaylistTitle,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              color: PrismWaveTheme.textPrimary,
                              fontSize: 26,
                              fontWeight: FontWeight.w800,
                              letterSpacing: 0,
                            ),
                          ),
                        ),
                      ],
                    ),
                    if ((playlist.subtitle ?? '').isNotEmpty)
                      Padding(
                        padding: const EdgeInsets.only(top: 4),
                        child: Text(
                          playlist.subtitle!,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            color: PrismWaveTheme.textSecondary.withValues(
                              alpha: 0.88,
                            ),
                            fontSize: 13,
                          ),
                        ),
                      ),
                    const SizedBox(height: 10),
                    Align(
                      alignment: Alignment.centerLeft,
                      child: TextButton.icon(
                        onPressed: onTap,
                        style: PrismWaveTheme.rectangularButtonStyle(
                          selected: true,
                          padding: const EdgeInsets.symmetric(
                            horizontal: 14,
                            vertical: 10,
                          ),
                        ),
                        icon: const Icon(Icons.play_arrow_rounded, size: 19),
                        label: Text(
                          t.onlineTopPlaylistOpen,
                          style: const TextStyle(fontWeight: FontWeight.w700),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 14),
              _BannerCoverCollage(
                covers: featuredCovers,
                coverCache: coverCache,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _BannerCoverCollage extends StatelessWidget {
  const _BannerCoverCollage({required this.covers, required this.coverCache});

  final List<OnlineTrackCandidate> covers;
  final OnlineMediaCacheService coverCache;

  @override
  Widget build(BuildContext context) {
    if (covers.isEmpty) {
      return const SizedBox(width: 132);
    }

    // 2x2 grid for 4 covers; degrades gracefully with fewer.
    return Container(
      width: 132,
      height: 132,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(14),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.28),
            blurRadius: 24,
            offset: const Offset(0, 14),
          ),
        ],
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(14),
        child: GridView.count(
          physics: const NeverScrollableScrollPhysics(),
          crossAxisCount: 2,
          mainAxisSpacing: 4,
          crossAxisSpacing: 4,
          children: [
            for (final c in covers)
              DecoratedBox(
                decoration: BoxDecoration(
                  border: Border.all(
                    color: Colors.white.withValues(alpha: 0.08),
                  ),
                ),
                child: OnlineCoverImage(
                  coverCache: coverCache,
                  cacheKey: c.canonicalKey,
                  coverUrl: c.coverUrl,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _AlbumRow extends StatelessWidget {
  const _AlbumRow({
    required this.t,
    required this.albums,
    required this.coverCache,
    required this.onOpenAlbum,
  });

  final AppStrings t;
  final List<OnlineAlbumCard> albums;
  final OnlineMediaCacheService coverCache;
  final ValueChanged<OnlineAlbumCard> onOpenAlbum;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(left: 2, right: 8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                t.onlineNewAlbumsTitle,
                style: const TextStyle(
                  color: PrismWaveTheme.textPrimary,
                  fontSize: 19,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 12),
        LayoutBuilder(
          builder: (context, constraints) {
            const cardWidth = 168.0;
            const gap = 12.0;
            final maxCount = math.max(
              1,
              ((constraints.maxWidth + gap) / (cardWidth + gap)).floor(),
            );
            final visible = albums.take(maxCount).toList();
            return Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (var i = 0; i < visible.length; i++) ...[
                  if (i > 0) const SizedBox(width: gap),
                  SizedBox(
                    width: cardWidth,
                    child: _AlbumCard(
                      t: t,
                      album: visible[i],
                      coverCache: coverCache,
                      onTap: () => onOpenAlbum(visible[i]),
                    ),
                  ),
                ],
              ],
            );
          },
        ),
      ],
    );
  }
}

class _AlbumCard extends StatelessWidget {
  const _AlbumCard({
    required this.t,
    required this.album,
    required this.coverCache,
    required this.onTap,
  });

  final AppStrings t;
  final OnlineAlbumCard album;
  final OnlineMediaCacheService coverCache;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 168,
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(PrismWaveTheme.cardRadius),
        clipBehavior: Clip.antiAlias,
        child: Ink(
          decoration: BoxDecoration(
            gradient: PrismWaveTheme.glassGradient(alpha: 0.18),
            borderRadius: BorderRadius.circular(PrismWaveTheme.cardRadius),
            border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
          ),
          child: InkWell(
            borderRadius: BorderRadius.circular(PrismWaveTheme.cardRadius),
            onTap: onTap,
            child: Padding(
              padding: const EdgeInsets.all(8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  DecoratedBox(
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(12),
                      boxShadow: [
                        BoxShadow(
                          color: Colors.black.withValues(alpha: 0.16),
                          blurRadius: 18,
                          offset: const Offset(0, 10),
                        ),
                      ],
                    ),
                    child: AspectRatio(
                      aspectRatio: 1,
                      child: ClipRRect(
                        borderRadius: BorderRadius.circular(12),
                        child: OnlineCoverImage(
                          coverCache: coverCache,
                          cacheKey: album.canonicalKey,
                          coverUrl: album.coverUrl,
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 9),
                  Text(
                    album.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: PrismWaveTheme.textPrimary,
                      fontSize: 13,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: 3),
                  Text(
                    album.artist.isEmpty ? t.onlinePlayAlbum : album.artist,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: PrismWaveTheme.textMuted,
                      fontSize: 12,
                    ),
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
