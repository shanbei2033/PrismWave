import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../i18n/app_strings.dart';
import '../models/hits_manifest.dart';
import '../models/track.dart';
import '../providers.dart';
import '../state/hits_state.dart';
import '../state/library_state.dart';
import 'glass_panel.dart';
import 'hits_ui_shared.dart';
import 'prismwave_theme.dart';
import 'window_top_bar.dart';

class HitsFullPlayPage extends ConsumerStatefulWidget {
  const HitsFullPlayPage({super.key});

  @override
  ConsumerState<HitsFullPlayPage> createState() => _HitsFullPlayPageState();
}

class _HitsFullPlayPageState extends ConsumerState<HitsFullPlayPage> {
  static const Color _developmentStatusColor = Color(0xFF7FD4FF);
  static const Color _offAirStatusColor = Color(0xFFF2C66D);
  static const Color _warningStatusColor = Color(0xFFFFB366);
  static const Color _errorStatusColor = Color(0xFFFF7B7B);

  @override
  void initState() {
    super.initState();
    Future<void>.microtask(() {
      if (!mounted) return;
      unawaited(ref.read(hitsProvider.notifier).initialize());
    });
  }

  @override
  Widget build(BuildContext context) {
    final language = ref.watch(appSettingsProvider).language;
    final t = AppStrings(language);
    final library = ref.watch(libraryProvider);
    final hits = ref.watch(hitsProvider);
    final playback = ref.watch(playbackProvider);

    final usingLivePlaybackPosition =
        hits.status == HitsStatus.ready && hits.isPlaying;
    final effectiveIsPlaying = usingLivePlaybackPosition
        ? playback.isPlaying
        : hits.isPlaying;

    final body = switch (hits.status) {
      HitsStatus.ready when hits.currentScheduleTrack != null => _HitsBody(
        scheduleTrack: hits.currentScheduleTrack!,
        matchedTrack: hits.matchedLibraryTrack,
        remoteCoverBytes: hits.currentCoverBytes,
        library: library,
        t: t,
        isPlaying: effectiveIsPlaying,
        onTogglePlayback: hits.canTogglePlayback
            ? () => unawaited(ref.read(hitsProvider.notifier).togglePlayback())
            : null,
      ),
      HitsStatus.offAir => _HitsEmptyView(
        lowEffects: library.lowEffects,
        t: t,
        statusText: t.hitsOffAir,
        statusColor: _offAirStatusColor,
      ),
      HitsStatus.noNetwork => _HitsEmptyView(
        lowEffects: library.lowEffects,
        t: t,
        statusText: t.hitsNoNetwork,
        statusColor: _errorStatusColor,
      ),
      HitsStatus.cloudTimeout => _HitsEmptyView(
        lowEffects: library.lowEffects,
        t: t,
        statusText: t.hitsCloudTimeout,
        statusColor: _warningStatusColor,
      ),
      HitsStatus.unavailable => _HitsEmptyView(
        lowEffects: library.lowEffects,
        t: t,
        statusText: t.hitsUnavailable,
        statusColor: _errorStatusColor,
      ),
      HitsStatus.standby => _HitsEmptyView(
        lowEffects: library.lowEffects,
        t: t,
        statusText: t.hitsScheduleStandby,
        statusColor: _developmentStatusColor,
      ),
      HitsStatus.loading || HitsStatus.idle => _HitsEmptyView(
        lowEffects: library.lowEffects,
        t: t,
        statusText: t.hitsLoadingSchedule,
        statusColor: _developmentStatusColor,
      ),
      _ => _HitsEmptyView(
        lowEffects: library.lowEffects,
        t: t,
        statusText: t.hitsInDevelopment,
        statusColor: _developmentStatusColor,
      ),
    };

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Stack(
        children: [
          Positioned.fill(child: body),
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
}

class _HitsEmptyView extends StatelessWidget {
  const _HitsEmptyView({
    required this.lowEffects,
    required this.t,
    required this.statusText,
    required this.statusColor,
  });

  final bool lowEffects;
  final AppStrings t;
  final String statusText;
  final Color statusColor;

  @override
  Widget build(BuildContext context) {
    return Stack(
      fit: StackFit.expand,
      children: [
        const _HitsPageBackground(),
        Padding(
          padding: kHitsViewportPadding,
          child: Column(
            children: [
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.only(top: kHitsPanelTopInset),
                  child: GlassPanel(
                    lowEffects: lowEffects,
                    padding: kHitsGlassPanelPadding,
                    child: Column(
                      children: [
                        HitsHeaderBar(title: t.hits),
                        Expanded(
                          child: Center(
                            child: LayoutBuilder(
                              builder: (context, constraints) {
                                final coverSide =
                                    (constraints.maxWidth * 0.30)
                                        .clamp(180.0, 320.0);

                                return Column(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    Container(
                                      width: coverSide,
                                      height: coverSide,
                                      decoration: BoxDecoration(
                                        borderRadius: BorderRadius.circular(14),
                                        gradient: const LinearGradient(
                                          begin: Alignment.topLeft,
                                          end: Alignment.bottomRight,
                                          colors: [
                                            Color(0xFF1B2B49),
                                            Color(0xFF244069),
                                          ],
                                        ),
                                        border: Border.all(
                                          color: Colors.white.withValues(
                                            alpha: 0.10,
                                          ),
                                        ),
                                      ),
                                      child: Center(
                                        child: Icon(
                                          Icons.graphic_eq_rounded,
                                          size: coverSide * 0.20,
                                          color: Colors.white.withValues(
                                            alpha: 0.55,
                                          ),
                                        ),
                                      ),
                                    ),
                                    const SizedBox(height: 24),
                                    _HitsStatusBadge(
                                      label: statusText,
                                      color: statusColor,
                                    ),
                                    const SizedBox(height: 18),
                                    const _HitsPlaybackToggleButton(
                                      onPressed: null,
                                      isPlaying: false,
                                    ),
                                  ],
                                );
                              },
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _HitsBody extends StatelessWidget {
  const _HitsBody({
    required this.scheduleTrack,
    required this.matchedTrack,
    required this.remoteCoverBytes,
    required this.library,
    required this.t,
    required this.isPlaying,
    required this.onTogglePlayback,
  });

  final HitsScheduleTrack scheduleTrack;
  final Track? matchedTrack;
  final Uint8List? remoteCoverBytes;
  final LibraryState library;
  final AppStrings t;
  final bool isPlaying;
  final VoidCallback? onTogglePlayback;

  @override
  Widget build(BuildContext context) {
    final coverBytes = matchedTrack == null
        ? remoteCoverBytes
        : (library.coverBytesOf(matchedTrack!) ?? remoteCoverBytes);
    final effectiveCoverUrl = _looksLikePlaceholderCover(
      scheduleTrack.coverUrl?.toString(),
    )
        ? null
        : scheduleTrack.coverUrl?.toString();

    return Stack(
      fit: StackFit.expand,
      children: [
        const _HitsPageBackground(),
        Padding(
          padding: kHitsViewportPadding,
          child: Column(
            children: [
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.only(top: kHitsPanelTopInset),
                  child: GlassPanel(
                    lowEffects: library.lowEffects,
                    padding: kHitsGlassPanelPadding,
                    child: Column(
                      children: [
                        HitsHeaderBar(title: t.hits),
                        Expanded(
                          child: Center(
                            child: SingleChildScrollView(
                              child: LayoutBuilder(
                                builder: (context, constraints) {
                                  final coverSide =
                                      (constraints.maxWidth * 0.32)
                                          .clamp(200.0, 360.0);

                                  return Column(
                                    mainAxisSize: MainAxisSize.min,
                                    children: [
                                      ClipRRect(
                                        borderRadius: BorderRadius.circular(14),
                                        child: SizedBox(
                                          width: coverSide,
                                          height: coverSide,
                                          child: _HitsCoverImage(
                                            coverUrl: effectiveCoverUrl,
                                            coverPath: matchedTrack?.coverPath,
                                            coverBytes: coverBytes,
                                            fit: BoxFit.cover,
                                          ),
                                        ),
                                      ),
                                      const SizedBox(height: 24),
                                      ConstrainedBox(
                                        constraints: BoxConstraints(
                                          maxWidth:
                                              constraints.maxWidth * 0.72,
                                        ),
                                        child: Text(
                                          scheduleTrack.title,
                                          maxLines: 2,
                                          overflow: TextOverflow.ellipsis,
                                          textAlign: TextAlign.center,
                                          style: const TextStyle(
                                            fontSize: 26,
                                            fontWeight: FontWeight.w700,
                                            height: 1.2,
                                          ),
                                        ),
                                      ),
                                      const SizedBox(height: 8),
                                      ConstrainedBox(
                                        constraints: BoxConstraints(
                                          maxWidth:
                                              constraints.maxWidth * 0.60,
                                        ),
                                        child: Text(
                                          scheduleTrack.artist,
                                          maxLines: 1,
                                          overflow: TextOverflow.ellipsis,
                                          textAlign: TextAlign.center,
                                          style: TextStyle(
                                            fontSize: 15,
                                            color: Colors.white.withValues(
                                              alpha: 0.72,
                                            ),
                                          ),
                                        ),
                                      ),
                                      const SizedBox(height: 28),
                                      _HitsPlaybackToggleButton(
                                        onPressed: onTogglePlayback,
                                        isPlaying: isPlaying,
                                      ),
                                    ],
                                  );
                                },
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }

  bool _looksLikePlaceholderCover(String? coverUrl) {
    final normalized = (coverUrl ?? '').trim().toLowerCase();
    if (normalized.isEmpty) {
      return false;
    }
    return normalized.contains('2a96cbd8b46e442fc41c2b86b821562f') ||
        normalized.contains('/noimage/');
  }
}

class _HitsPageBackground extends StatelessWidget {
  const _HitsPageBackground();

  @override
  Widget build(BuildContext context) {
    return const DecoratedBox(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xFF05070B), Color(0xFF09101A), Color(0xFF040508)],
        ),
      ),
    );
  }
}

class _HitsStatusBadge extends StatelessWidget {
  const _HitsStatusBadge({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(999),
        color: color.withValues(alpha: 0.14),
        border: Border.all(color: color.withValues(alpha: 0.30)),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: Colors.white.withValues(alpha: 0.94),
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

class _HitsCoverImage extends StatelessWidget {
  static const Map<String, String> _networkImageHeaders = <String, String>{
    'User-Agent':
        'PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)',
  };

  const _HitsCoverImage({
    this.coverUrl,
    required this.coverPath,
    required this.coverBytes,
    this.fit = BoxFit.cover,
  });

  final String? coverUrl;
  final String? coverPath;
  final Uint8List? coverBytes;
  final BoxFit fit;

  @override
  Widget build(BuildContext context) {
    if (coverBytes != null && coverBytes!.isNotEmpty) {
      return Image.memory(
        coverBytes!,
        fit: fit,
        gaplessPlayback: true,
        errorBuilder: (_, _, _) => _fileOrNetworkOrPlaceholder(),
      );
    }
    if (coverPath != null && File(coverPath!).existsSync()) {
      return _fileOrNetworkOrPlaceholder();
    }
    if ((coverUrl ?? '').trim().isNotEmpty) {
      return Image.network(
        coverUrl!,
        headers: _networkImageHeaders,
        fit: fit,
        gaplessPlayback: true,
        errorBuilder: (_, _, _) => _placeholder(),
      );
    }
    return _placeholder();
  }

  Widget _fileOrNetworkOrPlaceholder() {
    if (coverPath != null && File(coverPath!).existsSync()) {
      return Image.file(
        File(coverPath!),
        fit: fit,
        errorBuilder: (_, _, _) => _networkOrPlaceholder(),
      );
    }
    return _networkOrPlaceholder();
  }

  Widget _networkOrPlaceholder() {
    if ((coverUrl ?? '').trim().isNotEmpty) {
      return Image.network(
        coverUrl!,
        headers: _networkImageHeaders,
        fit: fit,
        gaplessPlayback: true,
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
          colors: [Color(0xFF1B2B49), Color(0xFF244069)],
        ),
      ),
      child: const Icon(Icons.music_note_rounded, color: Colors.white70),
    );
  }
}

class _HitsPlaybackToggleButton extends StatelessWidget {
  const _HitsPlaybackToggleButton({
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
      width: 62,
      height: 62,
      child: TextButton(
        onPressed: onPressed,
        style: ButtonStyle(
          padding: const WidgetStatePropertyAll(EdgeInsets.zero),
          backgroundColor: const WidgetStatePropertyAll(Colors.transparent),
          shape: WidgetStatePropertyAll(
            RoundedRectangleBorder(borderRadius: BorderRadius.circular(999)),
          ),
          overlayColor: WidgetStatePropertyAll(
            Colors.white.withValues(alpha: 0.08),
          ),
        ),
        child: Ink(
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            gradient: onPressed == null ? null : PrismWaveTheme.accentGradient,
            color: onPressed == null
                ? Colors.white.withValues(alpha: 0.045)
                : null,
            boxShadow: onPressed == null
                ? null
                : PrismWaveTheme.accentShadow(alpha: 0.32),
          ),
          child: Center(
            child: SvgPicture.asset(
              iconPath,
              width: 32,
              height: 32,
              colorFilter: ColorFilter.mode(
                Colors.white.withValues(alpha: onPressed == null ? 0.42 : 0.96),
                BlendMode.srcIn,
              ),
            ),
          ),
        ),
      ),
    );
  }
}
