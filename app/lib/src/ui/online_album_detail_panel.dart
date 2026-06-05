import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../i18n/app_strings.dart';
import '../models/online_recommendation.dart';
import '../providers.dart';
import '../services/online_media_cache_service.dart';
import '../state/online_state.dart';
import 'online_home_panel.dart' show OnlineCoverImage;

class OnlineAlbumDetailPanel extends ConsumerStatefulWidget {
  const OnlineAlbumDetailPanel({
    super.key,
    required this.t,
    required this.album,
    required this.onBack,
  });

  final AppStrings t;
  final OnlineAlbumCard album;
  final VoidCallback onBack;

  @override
  ConsumerState<OnlineAlbumDetailPanel> createState() =>
      _OnlineAlbumDetailPanelState();
}

class _OnlineAlbumDetailPanelState
    extends ConsumerState<OnlineAlbumDetailPanel> {
  final OnlineMediaCacheService _coverCache = OnlineMediaCacheService();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(onlineProvider.notifier).loadAlbumDetail(widget.album);
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
    final detail = ref.watch(onlineProvider.select((s) => s.albumDetail));
    final showingThisAlbum =
        detail.album?.canonicalKey == widget.album.canonicalKey;
    final tracks = showingThisAlbum ? detail.tracks : const <OnlineTrackCandidate>[];
    final loading = !showingThisAlbum ||
        detail.status == OnlineAlbumDetailStatus.loading ||
        detail.status == OnlineAlbumDetailStatus.idle;
    final failed = showingThisAlbum &&
        detail.status == OnlineAlbumDetailStatus.failed;

    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 16, 12, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _BackBar(label: t.back, onBack: widget.onBack),
          const SizedBox(height: 16),
          _Header(
            t: t,
            album: widget.album,
            coverCache: _coverCache,
            trackCount: tracks.length,
            onPlayAll: tracks.isEmpty ? null : () => _playAll(tracks),
          ),
          const SizedBox(height: 18),
          Expanded(
            child: _Body(
              t: t,
              loading: loading,
              failed: failed,
              tracks: tracks,
              coverCache: _coverCache,
              errorMessage: detail.errorMessage,
              onTapTrack: (track) => _playOne(tracks, track),
              onRetry: () =>
                  ref.read(onlineProvider.notifier).loadAlbumDetail(widget.album),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _playOne(
    List<OnlineTrackCandidate> tracks,
    OnlineTrackCandidate picked,
  ) async {
    await ref.read(onlineProvider.notifier).playOnlineTrack(
          picked: picked,
          contextTracks: tracks,
        );
  }

  Future<void> _playAll(List<OnlineTrackCandidate> tracks) async {
    if (tracks.isEmpty) return;
    await ref.read(onlineProvider.notifier).playOnlineTrack(
          picked: tracks.first,
          contextTracks: tracks,
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
    required this.album,
    required this.coverCache,
    required this.trackCount,
    required this.onPlayAll,
  });

  final AppStrings t;
  final OnlineAlbumCard album;
  final OnlineMediaCacheService coverCache;
  final int trackCount;
  final VoidCallback? onPlayAll;

  @override
  Widget build(BuildContext context) {
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
              cacheKey: album.canonicalKey,
              coverUrl: album.coverUrl,
            ),
          ),
        ),
        const SizedBox(width: 18),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                album.name,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  fontSize: 28,
                  fontWeight: FontWeight.w800,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                album.artist,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  color: Colors.white.withValues(alpha: 0.7),
                  fontSize: 14,
                ),
              ),
              const SizedBox(height: 14),
              Row(
                children: [
                  FilledButton.icon(
                    icon: const Icon(Icons.play_arrow_rounded),
                    label: Text(t.onlinePlayAlbumAll),
                    onPressed: onPlayAll,
                  ),
                  const SizedBox(width: 12),
                  if (trackCount > 0)
                    Text(
                      '$trackCount ${t.onlineAlbumTrackCount}',
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.6),
                        fontSize: 12,
                      ),
                    ),
                ],
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _Body extends StatelessWidget {
  const _Body({
    required this.t,
    required this.loading,
    required this.failed,
    required this.tracks,
    required this.coverCache,
    required this.errorMessage,
    required this.onTapTrack,
    required this.onRetry,
  });

  final AppStrings t;
  final bool loading;
  final bool failed;
  final List<OnlineTrackCandidate> tracks;
  final OnlineMediaCacheService coverCache;
  final String errorMessage;
  final ValueChanged<OnlineTrackCandidate> onTapTrack;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    if (loading) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const CircularProgressIndicator(strokeWidth: 2),
            const SizedBox(height: 12),
            Text(t.onlineHomeLoading),
          ],
        ),
      );
    }
    if (failed) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              t.onlineHomeFailed,
              style: TextStyle(color: Colors.white.withValues(alpha: 0.78)),
            ),
            if (errorMessage.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text(
                  errorMessage,
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    color: Colors.white.withValues(alpha: 0.55),
                    fontSize: 12,
                  ),
                ),
              ),
            const SizedBox(height: 14),
            FilledButton.tonal(
              onPressed: onRetry,
              child: Text(t.onlineHomeRetry),
            ),
          ],
        ),
      );
    }
    return ListView.separated(
      padding: const EdgeInsets.only(right: 8, bottom: 24),
      itemCount: tracks.length,
      separatorBuilder: (_, _) => const SizedBox(height: 4),
      itemBuilder: (context, index) {
        final track = tracks[index];
        return _TrackRow(
          index: index + 1,
          track: track,
          coverCache: coverCache,
          onTap: () => onTapTrack(track),
        );
      },
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
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 8),
          child: Row(
            children: [
              SizedBox(
                width: 28,
                child: Text(
                  '$index',
                  style: TextStyle(
                    color: Colors.white.withValues(alpha: 0.55),
                    fontSize: 12,
                  ),
                ),
              ),
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
