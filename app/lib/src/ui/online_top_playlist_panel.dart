import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../i18n/app_strings.dart';
import '../models/online_recommendation.dart';
import '../providers.dart';
import '../services/online_media_cache_service.dart';
import '../state/app_settings_state.dart';
import 'online_home_panel.dart' show OnlineCoverImage;

/// Detail page for the home banner's "Today's Top 10" playlist. Renders the
/// 10 tracks as a flat list with play-track / play-all controls. Sources its
/// data from the same `OnlineHomeData.topPlaylist` already cached by the
/// online controller.
class OnlineTopPlaylistPanel extends ConsumerStatefulWidget {
  const OnlineTopPlaylistPanel({
    super.key,
    required this.t,
    required this.onBack,
  });

  final AppStrings t;
  final VoidCallback onBack;

  @override
  ConsumerState<OnlineTopPlaylistPanel> createState() =>
      _OnlineTopPlaylistPanelState();
}

class _OnlineTopPlaylistPanelState
    extends ConsumerState<OnlineTopPlaylistPanel> {
  final OnlineMediaCacheService _coverCache = OnlineMediaCacheService();

  @override
  void dispose() {
    _coverCache.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final t = widget.t;
    final settings = ref.watch(appSettingsProvider);
    final recommendationsUnavailable = ref.watch(
      onlineProvider.select((s) => s.home.recommendationsUnavailable),
    );
    final recommendationsPendingGeneration = ref.watch(
      onlineProvider.select((s) => s.home.recommendationsPendingGeneration),
    );
    final playlist = ref.watch(
      onlineProvider.select((s) => s.home.data?.topPlaylist),
    );

    if (playlist == null) {
      // Banner shouldn't be tappable in this state, but handle gracefully.
      return Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _BackBar(label: t.back, onBack: widget.onBack),
            const SizedBox(height: 24),
            Text(
              t.onlineHomeFailed,
              style: TextStyle(color: Colors.white.withValues(alpha: 0.7)),
            ),
          ],
        ),
      );
    }

    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 16, 12, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _BackBar(label: t.back, onBack: widget.onBack),
          const SizedBox(height: 16),
          _Header(
            t: t,
            settings: settings,
            playlist: playlist,
            coverCache: _coverCache,
            onPlayAll: () => _playAll(playlist),
            recommendationsUnavailable: recommendationsUnavailable,
            recommendationsPendingGeneration: recommendationsPendingGeneration,
          ),
          const SizedBox(height: 18),
          Expanded(
            child: ListView.separated(
              padding: const EdgeInsets.only(right: 8, bottom: 24),
              itemCount: playlist.tracks.length,
              separatorBuilder: (_, _) => const SizedBox(height: 4),
              itemBuilder: (context, index) {
                final track = playlist.tracks[index];
                return _TrackRow(
                  index: index + 1,
                  track: track,
                  coverCache: _coverCache,
                  onTap: () => _playOne(playlist, track),
                );
              },
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _playOne(
    OnlineSection playlist,
    OnlineTrackCandidate picked,
  ) async {
    await ref
        .read(onlineProvider.notifier)
        .playOnlineTrack(picked: picked, contextTracks: playlist.tracks);
  }

  Future<void> _playAll(OnlineSection playlist) async {
    if (playlist.tracks.isEmpty) return;
    await ref
        .read(onlineProvider.notifier)
        .playOnlineTrack(
          picked: playlist.tracks.first,
          contextTracks: playlist.tracks,
        );
  }
}

class _BackBar extends StatelessWidget {
  const _BackBar({required this.label, required this.onBack});

  final String label;
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        IconButton(
          icon: const Icon(Icons.arrow_back_rounded),
          onPressed: onBack,
          tooltip: label,
        ),
        const SizedBox(width: 4),
        Text(
          label,
          style: TextStyle(color: Colors.white.withValues(alpha: 0.78)),
        ),
      ],
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({
    required this.t,
    required this.settings,
    required this.playlist,
    required this.coverCache,
    required this.onPlayAll,
    required this.recommendationsUnavailable,
    required this.recommendationsPendingGeneration,
  });

  final AppStrings t;
  final AppSettingsState settings;
  final OnlineSection playlist;
  final OnlineMediaCacheService coverCache;
  final VoidCallback onPlayAll;
  final bool recommendationsUnavailable;
  final bool recommendationsPendingGeneration;

  @override
  Widget build(BuildContext context) {
    final firstWithCover = playlist.tracks.firstWhere(
      (t) => (t.coverUrl ?? '').isNotEmpty,
      orElse: () => playlist.tracks.first,
    );

    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        SizedBox(
          width: 156,
          height: 156,
          child: ClipRRect(
            borderRadius: BorderRadius.circular(12),
            child: OnlineCoverImage(
              coverCache: coverCache,
              cacheKey: firstWithCover.canonicalKey,
              coverUrl: firstWithCover.coverUrl,
            ),
          ),
        ),
        const SizedBox(width: 18),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Flexible(
                    child: Text(
                      t.onlineTopPlaylistTitle,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 28,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ),
                  if (recommendationsUnavailable ||
                      recommendationsPendingGeneration) ...[
                    const SizedBox(width: 8),
                    _ChartStatusIcon(
                      t: t,
                      unavailable: recommendationsUnavailable,
                    ),
                  ],
                ],
              ),
              if ((playlist.subtitle ?? '').isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(
                    playlist.subtitle!,
                    style: TextStyle(
                      color: Colors.white.withValues(alpha: 0.65),
                      fontSize: 13,
                    ),
                  ),
                ),
              const SizedBox(height: 14),
              FilledButton.icon(
                onPressed: onPlayAll,
                icon: const Icon(Icons.play_arrow_rounded),
                label: Text(t.playAll),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _ChartStatusIcon extends StatelessWidget {
  const _ChartStatusIcon({required this.t, required this.unavailable});

  final AppStrings t;
  final bool unavailable;

  @override
  Widget build(BuildContext context) {
    final color = unavailable
        ? const Color(0xFFFFD166)
        : Colors.white.withValues(alpha: 0.72);
    return Tooltip(
      message: unavailable
          ? t.onlineRecommendationsUnavailableTooltip
          : t.onlineRecommendationsPendingTooltip,
      child: SvgPicture.asset(
        'assets/icons/chart_notice.svg',
        width: 21,
        height: 21,
        colorFilter: ColorFilter.mode(color, BlendMode.srcIn),
      ),
    );
  }
}

class _TrackRow extends StatelessWidget {
  const _TrackRow({
    required this.index,
    required this.track,
    required this.coverCache,
    required this.onTap,
  });

  final int index;
  final OnlineTrackCandidate track;
  final OnlineMediaCacheService coverCache;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        borderRadius: BorderRadius.circular(8),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 8, horizontal: 6),
          child: Row(
            children: [
              SizedBox(
                width: 32,
                child: Text(
                  '$index',
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    color: Colors.white.withValues(alpha: 0.55),
                    fontSize: 14,
                    fontFeatures: const [FontFeature.tabularFigures()],
                  ),
                ),
              ),
              const SizedBox(width: 8),
              SizedBox(
                width: 44,
                height: 44,
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(6),
                  child: OnlineCoverImage(
                    coverCache: coverCache,
                    cacheKey: track.canonicalKey,
                    coverUrl: track.coverUrl,
                  ),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      track.title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 14,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    Text(
                      track.artist,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.6),
                        fontSize: 12,
                      ),
                    ),
                  ],
                ),
              ),
              const Icon(Icons.play_arrow_rounded, size: 22),
            ],
          ),
        ),
      ),
    );
  }
}
