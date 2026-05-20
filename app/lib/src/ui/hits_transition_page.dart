import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/track.dart';
import '../providers.dart';
import '../state/hits_state.dart';
import '../state/library_state.dart';
import '../state/playback_state.dart';
import 'hits_availability.dart';
import 'hits_fullplay_page.dart';
import 'hits_unavailable_page.dart';
import 'window_top_bar.dart';

enum _HitsTransitionTarget { pending, available, unavailable }

class HitsTransitionPage extends ConsumerStatefulWidget {
  const HitsTransitionPage({super.key, this.availabilityFuture});

  final Future<HitsAvailability>? availabilityFuture;

  @override
  ConsumerState<HitsTransitionPage> createState() => _HitsTransitionPageState();
}

class _HitsTransitionPageState extends ConsumerState<HitsTransitionPage>
    with TickerProviderStateMixin {
  // --- Animation controllers ---
  late final AnimationController _entrance;
  late final AnimationController _breathing;
  late final AnimationController _exit;

  // --- Cached entrance animations (created once, not per-frame) ---
  late final Animation<double> _hAnim;
  late final Animation<double> _tsAnim;
  late final Animation<double> _iAnim;
  late final Animation<double> _settleAnim;

  // --- Cached exit animation ---
  late final Animation<double> _exitAnim;

  // --- Cached unavailable exit animation ---
  late final Animation<double> _unavailExitAnim;

  // --- Cached decorations (not rebuilt per-frame) ---
  static final _glowDecoration = BoxDecoration(
    gradient: RadialGradient(
      center: const Alignment(0, -0.12),
      radius: 0.88,
      colors: [
        Colors.white.withValues(alpha: 0.04),
        Colors.transparent,
      ],
    ),
  );

  bool _entranceCompleted = false;
  bool _exiting = false;
  bool _navigated = false;
  bool _navigationQueued = false;
  _HitsTransitionTarget _target = _HitsTransitionTarget.pending;

  @override
  void initState() {
    super.initState();

    // Entrance — plays once, 1280ms.
    _entrance = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1280),
    )..addStatusListener((status) {
        if (status == AnimationStatus.completed && !_entranceCompleted) {
          if (mounted) {
            setState(() => _entranceCompleted = true);
          } else {
            _entranceCompleted = true;
          }
          _breathing.repeat(reverse: true);
          _maybeScheduleNavigation();
        }
      });

    // Breathing — gentle scale oscillation, 1800ms per half-cycle.
    _breathing = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1800),
    );

    // Exit — fade out before navigating, 420ms.
    _exit = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 420),
    )..addStatusListener((status) {
        if (status == AnimationStatus.completed) _doNavigate();
      });

    // Pre-build all curved animations once.
    _hAnim = CurvedAnimation(
      parent: _entrance,
      curve: const Interval(0.00, 0.34, curve: Curves.easeOutCubic),
    );
    _tsAnim = CurvedAnimation(
      parent: _entrance,
      curve: const Interval(0.18, 0.56, curve: Curves.easeOutCubic),
    );
    _iAnim = CurvedAnimation(
      parent: _entrance,
      curve: const Interval(0.34, 0.62, curve: Curves.easeOutBack),
    );
    _settleAnim = CurvedAnimation(
      parent: _entrance,
      curve: const Interval(0.54, 0.82, curve: Curves.easeOutCubic),
    );
    _exitAnim = CurvedAnimation(
      parent: _exit,
      curve: Curves.easeInCubic,
    );
    _unavailExitAnim = CurvedAnimation(
      parent: _entrance,
      curve: const Interval(0.74, 1.00, curve: Curves.easeInCubic),
    );

    unawaited(
      _resolveTransitionTarget(
        widget.availabilityFuture ?? HitsAvailabilityResolver.resolve(),
      ),
    );
    _entrance.forward();
  }

  // ── Navigation logic (unchanged) ──────────────────────────────────────────

  Future<void> _resolveTransitionTarget(
    Future<HitsAvailability> availabilityFuture,
  ) async {
    final availability = await availabilityFuture;
    if (!mounted) return;
    if (_target != _HitsTransitionTarget.pending) return;

    setState(() {
      _target = availability == HitsAvailability.available
          ? _HitsTransitionTarget.available
          : _HitsTransitionTarget.unavailable;
    });

    if (_target == _HitsTransitionTarget.available) {
      unawaited(ref.read(hitsProvider.notifier).initialize());
    }
    _maybeScheduleNavigation();
  }

  void _maybeScheduleNavigation() {
    if (!_entranceCompleted || _navigated || _navigationQueued || !mounted) {
      return;
    }

    final effectiveTarget = _resolveNavigationTarget(
      hits: ref.read(hitsProvider),
      playback: ref.read(playbackProvider),
      library: ref.read(libraryProvider),
    );
    if (effectiveTarget == null) return;

    _navigationQueued = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _navigationQueued = false;
      if (!mounted || _navigated) return;
      final latestTarget = _resolveNavigationTarget(
        hits: ref.read(hitsProvider),
        playback: ref.read(playbackProvider),
        library: ref.read(libraryProvider),
      );
      if (latestTarget == null) return;
      unawaited(_scheduleNavigation(latestTarget));
    });
  }

  _HitsTransitionTarget? _resolveNavigationTarget({
    required HitsState hits,
    required PlaybackState playback,
    required LibraryState library,
  }) {
    if (!_entranceCompleted) return null;

    final hitsUnavailable =
        hits.status == HitsStatus.noNetwork ||
        hits.status == HitsStatus.cloudTimeout ||
        hits.status == HitsStatus.unavailable;
    if (_target == _HitsTransitionTarget.unavailable || hitsUnavailable) {
      return _HitsTransitionTarget.unavailable;
    }

    if (_target != _HitsTransitionTarget.available) return null;
    if (_canEnterHitsPage(hits: hits, playback: playback, library: library)) {
      return _HitsTransitionTarget.available;
    }
    return null;
  }

  bool _canEnterHitsPage({
    required HitsState hits,
    required PlaybackState playback,
    required LibraryState library,
  }) {
    switch (hits.status) {
      case HitsStatus.offAir:
      case HitsStatus.standby:
        return !hits.isRefreshing;
      case HitsStatus.ready:
        break;
      case HitsStatus.loading:
      case HitsStatus.idle:
      case HitsStatus.noNetwork:
      case HitsStatus.cloudTimeout:
      case HitsStatus.unavailable:
        return false;
    }

    final playbackTrack = hits.resolvedPlaybackTrack;
    if (playbackTrack == null || hits.isResolvingPlaybackSource) return false;
    if (!_isEntryAudioReady(playbackTrack: playbackTrack, playback: playback)) {
      return false;
    }
    if (!_isEntryCoverReady(hits: hits, library: library)) return false;
    if (!_isEntryLyricsReady(hits: hits, library: library)) return false;
    return true;
  }

  bool _isEntryAudioReady({
    required Track playbackTrack,
    required PlaybackState playback,
  }) {
    if (!playbackTrack.isRemote) return true;
    final uri = Uri.tryParse(playbackTrack.playbackSource);
    if (uri != null && uri.scheme.toLowerCase() == 'file') return true;
    if (playback.currentTrack?.id != playbackTrack.id) return false;
    if (playback.isLoading) return false;
    return playback.isPlaying ||
        playback.currentTime > Duration.zero ||
        playback.duration > Duration.zero;
  }

  bool _isEntryCoverReady({
    required HitsState hits,
    required LibraryState library,
  }) {
    final matchedTrack = hits.matchedLibraryTrack;
    final hasLocalCoverBytes =
        matchedTrack != null &&
        (library.coverBytesOf(matchedTrack)?.isNotEmpty ?? false);
    final localCoverPath = matchedTrack?.coverPath ?? '';
    final hasLocalCoverPath =
        localCoverPath.trim().isNotEmpty && File(localCoverPath).existsSync();
    if (hasLocalCoverBytes || hasLocalCoverPath) return true;
    if (hits.currentCoverBytes?.isNotEmpty ?? false) return true;
    return !hits.isCoverLoading;
  }

  bool _isEntryLyricsReady({
    required HitsState hits,
    required LibraryState library,
  }) {
    final matchedTrack = hits.matchedLibraryTrack;
    if (matchedTrack != null && library.lyricsOf(matchedTrack).isNotEmpty) {
      return true;
    }
    if (hits.onlineLyricsDocument?.lines.isNotEmpty ?? false) return true;
    final localLyricsLoading =
        matchedTrack != null && library.isLyricsLoading(matchedTrack);
    return !localLyricsLoading && !hits.isOnlineLyricsLoading;
  }

  Future<void> _scheduleNavigation(
    _HitsTransitionTarget effectiveTarget,
  ) async {
    if (_navigated || _exiting) return;
    _navigated = true;

    if (effectiveTarget == _HitsTransitionTarget.unavailable) {
      _doNavigate();
      return;
    }

    if (mounted) {
      setState(() => _exiting = true);
      _breathing.stop();
      _exit.forward();
    }
  }

  void _doNavigate() {
    if (!mounted) return;
    final effectiveTarget = _resolveNavigationTarget(
      hits: ref.read(hitsProvider),
      playback: ref.read(playbackProvider),
      library: ref.read(libraryProvider),
    );
    final target = effectiveTarget ?? _HitsTransitionTarget.available;

    final route = switch (target) {
      _HitsTransitionTarget.unavailable => PageRouteBuilder<void>(
          transitionDuration: Duration.zero,
          reverseTransitionDuration: Duration.zero,
          pageBuilder: (_, _, _) => const HitsUnavailablePage(),
        ),
      _ => PageRouteBuilder<void>(
          transitionDuration: const Duration(milliseconds: 380),
          reverseTransitionDuration: const Duration(milliseconds: 320),
          pageBuilder: (_, _, _) => const HitsFullPlayPage(),
          transitionsBuilder: (context, animation, secondaryAnimation, child) {
            final curved = CurvedAnimation(
              parent: animation,
              curve: Curves.easeOutCubic,
              reverseCurve: Curves.easeInOutCubic,
            );
            return FadeTransition(
              opacity: curved,
              child: SlideTransition(
                position: Tween<Offset>(
                  begin: const Offset(0, 0.035),
                  end: Offset.zero,
                ).animate(curved),
                child: ScaleTransition(
                  scale: Tween<double>(begin: 0.992, end: 1.0).animate(curved),
                  child: child,
                ),
              ),
            );
          },
        ),
    };
    Navigator.of(context).pushReplacement(route);
  }

  @override
  void dispose() {
    _entrance.dispose();
    _breathing.dispose();
    _exit.dispose();
    super.dispose();
  }

  // ── Build ─────────────────────────────────────────────────────────────────

  @override
  Widget build(BuildContext context) {
    final library = ref.watch(libraryProvider);
    final hits = ref.watch(hitsProvider);
    final playback = ref.watch(playbackProvider);
    final previewTrack = hits.matchedLibraryTrack ??
        hits.resolvedPlaybackTrack ??
        playback.currentTrack;
    final coverBytes = hits.matchedLibraryTrack != null
        ? (library.coverBytesOf(hits.matchedLibraryTrack!) ??
            hits.currentCoverBytes)
        : (hits.currentCoverBytes ??
            (previewTrack == null
                ? null
                : library.coverBytesOf(previewTrack)));
    _maybeScheduleNavigation();

    // LayoutBuilder outside AnimatedBuilder — only rebuilds on resize.
    return LayoutBuilder(
      builder: (context, constraints) {
        final width = constraints.maxWidth;
        final height = constraints.maxHeight;

        return Scaffold(
          backgroundColor: Colors.transparent,
          body: Stack(
            fit: StackFit.expand,
            children: [
              _HitsTransitionBackdrop(
                lowEffects: library.lowEffects,
                coverPath: previewTrack?.coverPath,
                coverBytes: coverBytes,
              ),
              AnimatedBuilder(
                animation: Listenable.merge([
                  _entrance,
                  _breathing,
                  _exit,
                ]),
                builder: (context, _) {
                  // Read cached animation values — no allocations.
                  final hVal = _hAnim.value;
                  final tsVal = _tsAnim.value;
                  final iVal = _iAnim.value;
                  final settleVal = _settleAnim.value;
                  final exitVal = _exitAnim.value;

                  // Breathing: scale 1.0 ↔ 1.035.
                  final breathe = _breathing.value;
                  final breathScale =
                      _entranceCompleted ? 1.0 + breathe * 0.035 : 1.0;
                  final glowBase = settleVal +
                      (_entranceCompleted ? breathe * 0.18 : 0.0);

                  // Exit transforms.
                  final exitScale = 1.0 - exitVal * 0.04;
                  final exitOpacity = 1.0 - exitVal;

                  // Unavailable exit (rare path — only when HITS is down).
                  final isUnavailExit =
                      _target == _HitsTransitionTarget.unavailable && !_exiting;
                  final unavailVal =
                      isUnavailExit ? _unavailExitAnim.value : 0.0;

                  final combinedScale =
                      breathScale * exitScale *
                      (isUnavailExit
                          ? (lerpDouble(1.0, 0.90, unavailVal) ?? 1.0)
                          : 1.0);
                  final combinedOpacity = exitOpacity *
                      (isUnavailExit
                          ? (1.0 - unavailVal)
                          : 1.0);

                  final unavailOffsetX = isUnavailExit
                      ? (lerpDouble(0, width * 0.86, unavailVal) ?? 0)
                      : 0.0;
                  final unavailBlurX =
                      isUnavailExit ? (lerpDouble(0, 26, unavailVal) ?? 0) : 0.0;
                  final unavailBlurY =
                      isUnavailExit ? (lerpDouble(0, 6, unavailVal) ?? 0) : 0.0;
                  final needsBlur = unavailBlurX > 0.1 || unavailBlurY > 0.1;

                  // Build the word mark once, wrap conditionally.
                  Widget wordMark = _HitsWordMark(
                    horizontalTravel: width * 0.74,
                    verticalTravel: height * 0.28,
                    hProgress: hVal,
                    iProgress: iVal,
                    tsProgress: tsVal,
                    glowStrength: glowBase,
                  );

                  // Only wrap in ImageFiltered when blur is actually needed.
                  if (needsBlur) {
                    wordMark = ImageFiltered(
                      imageFilter: ImageFilter.blur(
                        sigmaX: unavailBlurX,
                        sigmaY: unavailBlurY,
                      ),
                      child: wordMark,
                    );
                  }

                  return Stack(
                    fit: StackFit.expand,
                    children: [
                      // Radial glow — static decoration, only opacity changes.
                      Positioned.fill(
                        child: IgnorePointer(
                          child: Opacity(
                            opacity: (0.10 + glowBase * 0.14) * exitOpacity,
                            child: DecoratedBox(
                              decoration: _glowDecoration,
                            ),
                          ),
                        ),
                      ),
                      // HITS word mark.
                      Center(
                        child: Transform.translate(
                          offset: Offset(unavailOffsetX, 0),
                          child: Opacity(
                            opacity: combinedOpacity,
                            child: Transform.scale(
                              scale: combinedScale,
                              child: wordMark,
                            ),
                          ),
                        ),
                      ),
                    ],
                  );
                },
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
      },
    );
  }
}

// ---------------------------------------------------------------------------
// Backdrop — blurred cover image.
// ---------------------------------------------------------------------------

class _HitsTransitionBackdrop extends StatelessWidget {
  const _HitsTransitionBackdrop({
    required this.lowEffects,
    required this.coverPath,
    required this.coverBytes,
  });

  final bool lowEffects;
  final String? coverPath;
  final Uint8List? coverBytes;

  @override
  Widget build(BuildContext context) {
    return Stack(
      fit: StackFit.expand,
      children: [
        const DecoratedBox(
          decoration: BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
              colors: [Color(0x24090F1D), Color(0x240C1323), Color(0x240E1526)],
            ),
          ),
        ),
        IgnorePointer(
          child: Opacity(
            opacity: lowEffects ? 0.05 : 0.08,
            child: ImageFiltered(
              imageFilter: ImageFilter.blur(
                sigmaX: lowEffects ? 8 : 16,
                sigmaY: lowEffects ? 8 : 16,
              ),
              child: _TransitionCoverImage(
                coverPath: coverPath,
                coverBytes: coverBytes,
                fit: BoxFit.cover,
              ),
            ),
          ),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// HITS word mark — H · I · TS with staggered entrance.
// ---------------------------------------------------------------------------

class _HitsWordMark extends StatelessWidget {
  const _HitsWordMark({
    required this.horizontalTravel,
    required this.verticalTravel,
    required this.hProgress,
    required this.iProgress,
    required this.tsProgress,
    required this.glowStrength,
  });

  final double horizontalTravel;
  final double verticalTravel;
  final double hProgress;
  final double iProgress;
  final double tsProgress;
  final double glowStrength;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Transform.translate(
          offset: Offset(lerpDouble(-horizontalTravel, 0, hProgress) ?? 0, 0),
          child: Opacity(
            opacity: hProgress.clamp(0.0, 1.0),
            child: _HitsGlyph(label: 'H', glowStrength: glowStrength),
          ),
        ),
        const SizedBox(width: 6),
        Transform.translate(
          offset: Offset(0, lerpDouble(-verticalTravel, 0, iProgress) ?? 0),
          child: Opacity(
            opacity: iProgress.clamp(0.0, 1.0),
            child: _HitsGlyph(label: 'I', glowStrength: glowStrength),
          ),
        ),
        const SizedBox(width: 8),
        Transform.translate(
          offset: Offset(lerpDouble(horizontalTravel, 0, tsProgress) ?? 0, 0),
          child: Opacity(
            opacity: tsProgress.clamp(0.0, 1.0),
            child: _HitsGlyph(label: 'TS', glowStrength: glowStrength),
          ),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Cover image helper for the backdrop.
// ---------------------------------------------------------------------------

class _TransitionCoverImage extends StatelessWidget {
  const _TransitionCoverImage({
    required this.coverPath,
    required this.coverBytes,
    this.fit = BoxFit.cover,
  });

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
        errorBuilder: (_, _, _) => _fileImageOrPlaceholder(),
      );
    }
    if (coverPath != null && File(coverPath!).existsSync()) {
      return _fileImageOrPlaceholder();
    }
    return _placeholder();
  }

  Widget _fileImageOrPlaceholder() {
    if (coverPath != null && File(coverPath!).existsSync()) {
      return Image.file(
        File(coverPath!),
        fit: fit,
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
          colors: [Color(0xFF14233C), Color(0xFF0D1830)],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Individual letter glyph with glow shadow.
// ---------------------------------------------------------------------------

class _HitsGlyph extends StatelessWidget {
  const _HitsGlyph({required this.label, required this.glowStrength});

  final String label;
  final double glowStrength;

  @override
  Widget build(BuildContext context) {
    return Text(
      label,
      style: TextStyle(
        fontSize: 146,
        fontWeight: FontWeight.w900,
        letterSpacing: label == 'TS' ? 1.0 : 2.0,
        color: Colors.white.withValues(alpha: 0.98),
        shadows: [
          Shadow(
            color: Colors.white
                .withValues(alpha: 0.08 + glowStrength * 0.14),
            blurRadius: 18 + glowStrength * 20,
          ),
          Shadow(
            color: const Color(0xFF7FD4FF)
                .withValues(alpha: 0.06 + glowStrength * 0.18),
            blurRadius: 24 + glowStrength * 26,
          ),
        ],
      ),
    );
  }
}
