import 'dart:math' as math;
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../i18n/app_strings.dart';
import '../models/online_recommendation.dart';
import '../providers.dart';
import '../services/online_media_cache_service.dart';
import '../state/app_settings_state.dart';
import '../state/online_state.dart';
import 'components/prism_components.dart';
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
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      ref.read(onlineProvider.notifier).ensureHomeLoaded();
    });
  }

  @override
  Widget build(BuildContext context) {
    final t = widget.t;
    final settings = ref.watch(appSettingsProvider);
    final home = ref.watch(onlineProvider.select((s) => s.home));
    final coverCache = ref.watch(onlineCoverCacheProvider);
    final canRefresh = home.status != OnlineHomeStatus.loading;

    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 0, 12, 0),
      child: Stack(
        children: [
          Positioned.fill(child: _buildContent(home, t, settings, coverCache)),
          Positioned(
            top: 10,
            right: 18,
            child: Tooltip(
              message: t.onlineHomeRetry,
              child: SizedBox(
                width: 44,
                height: 40,
                child: TextButton(
                  onPressed: canRefresh ? _refreshHomeRecommendations : null,
                  style: PrismWaveTheme.iconButtonStyle(),
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
          ),
        ],
      ),
    );
  }

  Widget _buildContent(
    OnlineHomeView home,
    AppStrings t,
    AppSettingsState settings,
    OnlineMediaCacheService coverCache,
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

    return LayoutBuilder(
      builder: (context, constraints) {
        final showRightRail =
            constraints.maxWidth >= PrismWaveTheme.wideHomeBreakpoint;
        if (!showRightRail) {
          return _buildHomeFeed(
            data: data,
            t: t,
            settings: settings,
            coverCache: coverCache,
            hasBanner: hasBanner,
            topPlaylist: topPlaylist,
            albums: albums,
          );
        }
        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: _buildHomeFeed(
                data: data,
                t: t,
                settings: settings,
                coverCache: coverCache,
                hasBanner: hasBanner,
                topPlaylist: topPlaylist,
                albums: albums,
              ),
            ),
            const SizedBox(width: 22),
            SizedBox(
              width: PrismWaveTheme.rightRailWidth,
              height: double.infinity,
              child: _HomeRightRail(
                t: t,
                settings: settings,
                data: data,
                coverCache: coverCache,
                onPlay: (section, track) => _playSection(section, track),
              ),
            ),
          ],
        );
      },
    );
  }

  Widget _buildHomeFeed({
    required OnlineHomeData data,
    required AppStrings t,
    required AppSettingsState settings,
    required OnlineMediaCacheService coverCache,
    required bool hasBanner,
    required OnlineSection? topPlaylist,
    required List<OnlineAlbumCard> albums,
  }) {
    return CustomScrollView(
      slivers: [
        if (hasBanner)
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.only(right: 8, bottom: 26),
              child: _TopPlaylistBanner(
                t: t,
                playlist: topPlaylist!,
                coverCache: coverCache,
                onTap: widget.onOpenTopPlaylist!,
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
                coverCache: coverCache,
                onPlay: (track) => _playSection(section, track),
              ),
            );
          },
        ),
        if (albums.isNotEmpty)
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.only(top: 28, right: 8, bottom: 6),
              child: _AlbumRow(
                t: t,
                albums: albums,
                coverCache: coverCache,
                onOpenAlbum: _openAlbum,
              ),
            ),
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
          child: SectionHeader(
            title: section.localizedTitle(settings.language),
            subtitle: section.subtitle,
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
      child: HoverGlassCard(
        onTap: onTap,
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
            errorBuilder: (_, error, _) {
              widget.coverCache.recordDecodeFailure(
                cacheKey: widget.cacheKey,
                coverUrl: widget.coverUrl,
                error: error,
              );
              return _placeholder();
            },
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
        .take(8)
        .toList(growable: false);
    final subtitle = playlist.subtitle?.trim().isNotEmpty == true
        ? playlist.subtitle!.trim()
        : t.onlineTopPlaylistSubtitle;

    return Material(
      color: Colors.transparent,
      borderRadius: BorderRadius.circular(18),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Container(
          height: 238,
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
          child: Stack(
            children: [
              Positioned(
                left: -32,
                top: -32,
                right: -32,
                bottom: -32,
                child: _BannerBlurredCoverBackground(
                  covers: featuredCovers,
                  coverCache: coverCache,
                ),
              ),
              Positioned.fill(
                child: IgnorePointer(
                  child: DecoratedBox(
                    decoration: BoxDecoration(
                      color: Colors.black.withValues(alpha: 0.24),
                    ),
                  ),
                ),
              ),
              Positioned(
                top: 10,
                right: 10,
                child: _BannerCoverCollage(
                  covers: featuredCovers.take(4).toList(growable: false),
                  coverCache: coverCache,
                ),
              ),
              Positioned.fill(
                child: IgnorePointer(
                  child: DecoratedBox(
                    decoration: BoxDecoration(
                      gradient: LinearGradient(
                        begin: Alignment.centerLeft,
                        end: Alignment.centerRight,
                        colors: [
                          PrismWaveTheme.surfaceStrong.withValues(alpha: 0.84),
                          PrismWaveTheme.surfaceStrong.withValues(alpha: 0.70),
                          PrismWaveTheme.accentDeep.withValues(alpha: 0.16),
                          Colors.transparent,
                        ],
                        stops: const [0, 0.48, 0.76, 1],
                      ),
                    ),
                  ),
                ),
              ),
              Positioned(
                left: 32,
                top: 46,
                right: 210,
                bottom: 32,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
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
                              fontSize: 46,
                              fontWeight: FontWeight.w900,
                              height: 0.98,
                              letterSpacing: 0,
                            ),
                          ),
                        ),
                        const SizedBox(width: 12),
                        Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 10,
                            vertical: 5,
                          ),
                          decoration: BoxDecoration(
                            borderRadius: BorderRadius.circular(999),
                            color: PrismWaveTheme.accent.withValues(
                              alpha: 0.22,
                            ),
                            border: Border.all(
                              color: PrismWaveTheme.accentSoft.withValues(
                                alpha: 0.30,
                              ),
                            ),
                          ),
                          child: Text(
                            t.onlineTopPlaylistBadge,
                            style: const TextStyle(
                              color: PrismWaveTheme.accentSoft,
                              fontSize: 12,
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 14),
                    Text(
                      subtitle,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: PrismWaveTheme.textPrimary.withValues(
                          alpha: 0.86,
                        ),
                        fontSize: 15,
                        height: 1.35,
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                    const Spacer(),
                    Row(
                      children: [
                        Container(
                          width: 44,
                          height: 44,
                          decoration: BoxDecoration(
                            shape: BoxShape.circle,
                            gradient: PrismWaveTheme.accentGradient,
                            boxShadow: PrismWaveTheme.accentShadow(alpha: 0.28),
                          ),
                          child: const Icon(
                            Icons.play_arrow_rounded,
                            color: PrismWaveTheme.textPrimary,
                            size: 26,
                          ),
                        ),
                        const SizedBox(width: 14),
                        Text(
                          t.playAll,
                          style: const TextStyle(
                            color: PrismWaveTheme.textPrimary,
                            fontSize: 15,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        const SizedBox(width: 7),
                        Text(
                          t.trackCountText(tracks.length),
                          style: PrismWaveTheme.captionStyle(fontSize: 14),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _BannerBlurredCoverBackground extends StatelessWidget {
  const _BannerBlurredCoverBackground({
    required this.covers,
    required this.coverCache,
  });

  final List<OnlineTrackCandidate> covers;
  final OnlineMediaCacheService coverCache;

  @override
  Widget build(BuildContext context) {
    if (covers.isEmpty) {
      return ColoredBox(color: PrismWaveTheme.surfaceStrong);
    }

    final tiles = List.generate(8, (index) => covers[index % covers.length]);

    return LayoutBuilder(
      builder: (context, constraints) {
        final tileWidth = (constraints.maxWidth - 6) / 4;
        final tileHeight = (constraints.maxHeight - 2) / 2;

        return ImageFiltered(
          imageFilter: ui.ImageFilter.blur(sigmaX: 10, sigmaY: 10),
          child: Opacity(
            opacity: 0.84,
            child: Wrap(
              spacing: 2,
              runSpacing: 2,
              children: [
                for (final c in tiles)
                  SizedBox(
                    width: tileWidth,
                    height: tileHeight,
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
      },
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
      return const SizedBox(width: 170, height: 170);
    }

    final tiles = List.generate(4, (index) => covers[index % covers.length]);

    return Container(
      width: 170,
      height: 170,
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
          padding: EdgeInsets.zero,
          physics: const NeverScrollableScrollPhysics(),
          crossAxisCount: 2,
          mainAxisSpacing: 4,
          crossAxisSpacing: 4,
          children: [
            for (final c in tiles)
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
          child: SectionHeader(title: t.onlineNewAlbumsTitle),
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
      child: HoverGlassCard(
        onTap: onTap,
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
    );
  }
}

class _HomeRightRail extends StatelessWidget {
  const _HomeRightRail({
    required this.t,
    required this.settings,
    required this.data,
    required this.coverCache,
    required this.onPlay,
  });

  final AppStrings t;
  final AppSettingsState settings;
  final OnlineHomeData data;
  final OnlineMediaCacheService coverCache;
  final void Function(OnlineSection section, OnlineTrackCandidate track) onPlay;

  @override
  Widget build(BuildContext context) {
    final playlist = data.topPlaylist ?? data.sections.firstOrNull;
    final featuredTracks = <OnlineTrackCandidate>[
      if (data.topPlaylist != null) ...data.topPlaylist!.tracks.take(5),
      for (final section in data.sections.take(2)) ...section.tracks.take(2),
    ];
    final artists = _deriveArtists(data).take(4).toList(growable: false);

    return Container(
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 16),
      decoration: PrismWaveTheme.glassDecoration(
        radius: PrismWaveTheme.panelRadius,
        alpha: 0.72,
        borderAlpha: 0.10,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _RadarCard(
            title: t.onlineHomePrivateRadarTitle,
            subtitle: t.onlineHomePrivateRadarSubtitle,
          ),
          const SizedBox(height: 18),
          SectionHeader(title: t.onlineHomeRecommendedArtists),
          const SizedBox(height: 14),
          Row(
            children: [
              for (var i = 0; i < artists.length; i++) ...[
                if (i > 0) const SizedBox(width: 12),
                Expanded(
                  child: _ArtistBubble(
                    artist: artists[i],
                    coverCache: coverCache,
                  ),
                ),
              ],
            ],
          ),
          const SizedBox(height: 18),
          Divider(color: Colors.white.withValues(alpha: 0.08), height: 1),
          const SizedBox(height: 18),
          SectionHeader(title: t.onlineHomeForYou),
          const SizedBox(height: 12),
          Expanded(
            child: playlist == null || featuredTracks.isEmpty
                ? Center(
                    child: Text(
                      t.onlineHomeFailed,
                      style: PrismWaveTheme.captionStyle(),
                    ),
                  )
                : ListView.separated(
                    padding: EdgeInsets.zero,
                    itemCount: math.min(featuredTracks.length, 5),
                    separatorBuilder: (_, _) => const SizedBox(height: 8),
                    itemBuilder: (context, index) {
                      final track = featuredTracks[index];
                      final section = _sectionForTrack(data, track) ?? playlist;
                      return _RailTrackRow(
                        track: track,
                        coverCache: coverCache,
                        onTap: () => onPlay(section, track),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }

  List<_ArtistPreview> _deriveArtists(OnlineHomeData data) {
    final previews = <String, _ArtistPreview>{};
    void collect(OnlineTrackCandidate track) {
      final artist = track.artist.trim();
      if (artist.isEmpty || previews.containsKey(artist)) return;
      previews[artist] = _ArtistPreview(
        name: artist,
        coverUrl: track.coverUrl,
        cacheKey: track.canonicalKey,
      );
    }

    data.topPlaylist?.tracks.forEach(collect);
    for (final section in data.sections) {
      for (final track in section.tracks) {
        collect(track);
      }
    }
    return previews.values.toList(growable: false);
  }

  OnlineSection? _sectionForTrack(
    OnlineHomeData data,
    OnlineTrackCandidate track,
  ) {
    if (data.topPlaylist?.tracks.any(
          (t) => t.canonicalKey == track.canonicalKey,
        ) ==
        true) {
      return data.topPlaylist;
    }
    for (final section in data.sections) {
      if (section.tracks.any((t) => t.canonicalKey == track.canonicalKey)) {
        return section;
      }
    }
    return null;
  }
}

class _RadarCard extends StatelessWidget {
  const _RadarCard({required this.title, required this.subtitle});

  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 14),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(16),
        color: Colors.white.withValues(alpha: 0.045),
        border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
      ),
      child: Row(
        children: [
          Container(
            width: 48,
            height: 48,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              gradient: PrismWaveTheme.accentGradient,
              boxShadow: PrismWaveTheme.accentShadow(alpha: 0.20),
            ),
            child: const Icon(
              Icons.radar_rounded,
              color: PrismWaveTheme.textPrimary,
              size: 26,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: PrismWaveTheme.sectionTitleStyle(fontSize: 17),
                ),
                const SizedBox(height: 4),
                Text(
                  subtitle,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: PrismWaveTheme.captionStyle(),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _ArtistPreview {
  const _ArtistPreview({
    required this.name,
    required this.coverUrl,
    required this.cacheKey,
  });

  final String name;
  final String? coverUrl;
  final String cacheKey;
}

class _ArtistBubble extends StatelessWidget {
  const _ArtistBubble({required this.artist, required this.coverCache});

  final _ArtistPreview artist;
  final OnlineMediaCacheService coverCache;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        SizedBox(
          width: 54,
          height: 54,
          child: ClipOval(
            child: OnlineCoverImage(
              coverCache: coverCache,
              cacheKey: artist.cacheKey,
              coverUrl: artist.coverUrl,
            ),
          ),
        ),
        const SizedBox(height: 7),
        Text(
          artist.name,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          textAlign: TextAlign.center,
          style: const TextStyle(
            color: PrismWaveTheme.textSecondary,
            fontSize: 11.5,
            fontWeight: FontWeight.w600,
          ),
        ),
      ],
    );
  }
}

class _RailTrackRow extends StatelessWidget {
  const _RailTrackRow({
    required this.track,
    required this.coverCache,
    required this.onTap,
  });

  final OnlineTrackCandidate track;
  final OnlineMediaCacheService coverCache;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return HoverGlassCard(
      onTap: onTap,
      radius: 14,
      padding: const EdgeInsets.fromLTRB(8, 8, 8, 8),
      child: Row(
        children: [
          SizedBox(
            width: 44,
            height: 44,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(10),
              child: OnlineCoverImage(
                coverCache: coverCache,
                cacheKey: track.canonicalKey,
                coverUrl: track.coverUrl,
              ),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
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
                const SizedBox(height: 2),
                Text(
                  track.artist,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: PrismWaveTheme.captionStyle(fontSize: 12),
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Container(
            width: 34,
            height: 34,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: Colors.white.withValues(alpha: 0.07),
            ),
            child: const Icon(
              Icons.play_arrow_rounded,
              color: PrismWaveTheme.textPrimary,
              size: 22,
            ),
          ),
        ],
      ),
    );
  }
}
