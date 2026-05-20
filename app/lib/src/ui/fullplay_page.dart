import 'dart:async';
import 'dart:io';
import 'dart:math' as math;
import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../i18n/app_strings.dart';
import '../models/lyric_line.dart';
import '../models/lyrics_source_type.dart';
import '../models/online_cover_search_result.dart';
import '../models/online_lyrics_search_result.dart';
import '../models/playback_mode.dart';
import '../models/track.dart';
import '../providers.dart';
import '../state/library_state.dart';
import '../state/playback_state.dart';
import 'middle_click_autoscroll.dart';
import 'window_top_bar.dart';

class FullPlayPage extends ConsumerStatefulWidget {
  const FullPlayPage({super.key});

  @override
  ConsumerState<FullPlayPage> createState() => _FullPlayPageState();
}

class _FullPlayPageState extends ConsumerState<FullPlayPage> {
  @override
  Widget build(BuildContext context) {
    final language = ref.watch(appSettingsProvider).language;
    final t = AppStrings(language);
    final library = ref.watch(libraryProvider);
    final playback = ref.watch(playbackProvider);
    final track = playback.currentTrack;
    if (track != null) {
      unawaited(ref.read(libraryProvider.notifier).ensureLyricsLoaded(track));
    }

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Stack(
        children: [
          Positioned.fill(
            child: track == null
                ? _EmptyFullPlay(lowEffects: library.lowEffects, t: t)
                : _FullPlayBody(
                    track: track,
                    library: library,
                    playback: playback,
                    t: t,
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
}

class _EmptyFullPlay extends StatelessWidget {
  const _EmptyFullPlay({required this.lowEffects, required this.t});

  final bool lowEffects;
  final AppStrings t;

  @override
  Widget build(BuildContext context) {
    final blur = lowEffects ? 12.0 : 20.0;
    return Stack(
      fit: StackFit.expand,
      children: [
        ImageFiltered(
          imageFilter: ImageFilter.blur(sigmaX: blur, sigmaY: blur),
          child: Container(color: const Color(0xFF0A1020)),
        ),
        DecoratedBox(
          decoration: BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
              colors: [
                Colors.black.withValues(alpha: 0.52),
                const Color(0xFF0B1324).withValues(alpha: 0.72),
                const Color(0xFF0D1629).withValues(alpha: 0.82),
              ],
            ),
          ),
        ),
        Center(
          child: Padding(
            padding: const EdgeInsets.only(top: 42),
            child: Text(
              t.noTrackPlaying,
              style: TextStyle(
                color: Colors.white.withValues(alpha: 0.78),
                fontSize: 18,
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _FullPlayBody extends ConsumerWidget {
  const _FullPlayBody({
    required this.track,
    required this.library,
    required this.playback,
    required this.t,
  });

  final Track track;
  final LibraryState library;
  final PlaybackState playback;
  final AppStrings t;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final playbackCtrl = ref.read(playbackProvider.notifier);
    final coverBytes = library.coverBytesOf(track);
    final lyrics = library.lyricsOf(track);
    final effectiveLyricsSource = library.effectiveLyricsSourceOf(track);
    final lyricsLoading = library.isLyricsLoading(track);
    final lyricsOffsetSeconds = library.lyricsOffsetOf(track);
    final currentLyricIndex = _resolveCurrentLyricIndex(
      lyrics,
      playback.currentTime,
    );
    final duration = playback.duration > Duration.zero
        ? playback.duration
        : (library.durationOf(track) ?? Duration.zero);
    final durationMs = duration.inMilliseconds.toDouble();
    final positionMs = playback.currentTime.inMilliseconds.toDouble();
    final safeDuration = durationMs > 0 ? durationMs : 1.0;
    final safePosition = positionMs.clamp(0.0, safeDuration);

    return Stack(
      fit: StackFit.expand,
      children: [
        ImageFiltered(
          imageFilter: ImageFilter.blur(
            sigmaX: library.lowEffects ? 8 : 18,
            sigmaY: library.lowEffects ? 8 : 18,
          ),
          child: _CoverImage(
            coverPath: track.coverPath,
            coverBytes: coverBytes,
            fit: BoxFit.cover,
          ),
        ),
        DecoratedBox(
          decoration: BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
              colors: [
                Colors.black.withValues(alpha: 0.58),
                const Color(0xFF0B1324).withValues(alpha: 0.72),
                const Color(0xFF0D1629).withValues(alpha: 0.80),
              ],
            ),
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(26, 56, 26, 18),
          child: Row(
            children: [
              Flexible(
                flex: 2,
                child: LayoutBuilder(
                  builder: (context, constraints) {
                    final panelWidth = math.max(
                      320.0,
                      constraints.maxWidth - 12,
                    );
                    final coverSide = (panelWidth * 0.72).clamp(240.0, 420.0);
                    final coverScale = playback.isPlaying ? 1.0 : 0.9;

                    return Align(
                      alignment: Alignment.topCenter,
                      child: SizedBox(
                        width: panelWidth,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Center(
                              child: IconButton(
                                tooltip: t.back,
                                onPressed: () =>
                                    Navigator.of(context).maybePop(),
                                icon: const Icon(
                                  Icons.keyboard_arrow_down_rounded,
                                ),
                              ),
                            ),
                            const SizedBox(height: 8),
                            Center(
                              child: AnimatedScale(
                                scale: coverScale,
                                duration: const Duration(milliseconds: 420),
                                curve: Curves.easeInOutCubic,
                                child: AnimatedContainer(
                                  duration: const Duration(milliseconds: 420),
                                  curve: Curves.easeInOutCubic,
                                  width: coverSide,
                                  height: coverSide,
                                  child: GestureDetector(
                                    onDoubleTap: () async {
                                      await showDialog<void>(
                                        context: context,
                                        barrierColor: Colors.black.withValues(
                                          alpha: 0.36,
                                        ),
                                        builder: (_) => _CoverSearchDialog(
                                          track: track,
                                          t: t,
                                          lowEffects: library.lowEffects,
                                        ),
                                      );
                                    },
                                    child: ClipRRect(
                                      borderRadius: BorderRadius.circular(14),
                                      child: _CoverImage(
                                        coverPath: track.coverPath,
                                        coverBytes: coverBytes,
                                        fit: BoxFit.cover,
                                      ),
                                    ),
                                  ),
                                ),
                              ),
                            ),
                            const SizedBox(height: 14),
                            Center(
                              child: ConstrainedBox(
                                constraints: BoxConstraints(
                                  maxWidth: panelWidth * 0.86,
                                ),
                                child: Text(
                                  track.title,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  textAlign: TextAlign.center,
                                  style: const TextStyle(
                                    fontSize: 24,
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                              ),
                            ),
                            const SizedBox(height: 5),
                            Center(
                              child: ConstrainedBox(
                                constraints: BoxConstraints(
                                  maxWidth: panelWidth * 0.86,
                                ),
                                child: Text(
                                  track.artist,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  textAlign: TextAlign.center,
                                  style: TextStyle(
                                    fontSize: 15,
                                    color: Colors.white.withValues(alpha: 0.78),
                                  ),
                                ),
                              ),
                            ),
                            const SizedBox(height: 14),
                            Row(
                              mainAxisAlignment: MainAxisAlignment.center,
                              children: [
                                IconButton(
                                  onPressed: playback.hasTrack
                                      ? playbackCtrl.previous
                                      : null,
                                  iconSize: 28,
                                  icon: const Icon(Icons.skip_previous_rounded),
                                ),
                                const SizedBox(width: 8),
                                _PlaybackToggleButton(
                                  onPressed: playback.hasTrack
                                      ? playbackCtrl.togglePlayPause
                                      : null,
                                  isPlaying: playback.isPlaying,
                                ),
                                const SizedBox(width: 8),
                                IconButton(
                                  onPressed: playback.hasTrack
                                      ? playbackCtrl.next
                                      : null,
                                  iconSize: 28,
                                  icon: const Icon(Icons.skip_next_rounded),
                                ),
                                const SizedBox(width: 10),
                                _PlaybackModeButton(
                                  t: t,
                                  mode: playback.playbackMode,
                                  onPressed: playbackCtrl.cycleMode,
                                ),
                                const SizedBox(width: 4),
                                _ExpandableVolumeControl(
                                  tooltip: t.volume,
                                  volume: playback.volume,
                                  onChanged: playbackCtrl.setVolume,
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
                                      inactiveTrackColor: Colors.white
                                          .withValues(alpha: 0.24),
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
                                          ? (value) => playbackCtrl.seekTo(
                                              Duration(
                                                milliseconds: value.round(),
                                              ),
                                            )
                                          : null,
                                    ),
                                  ),
                                ),
                                SizedBox(
                                  width: 52,
                                  child: Text(_formatDuration(duration)),
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    );
                  },
                ),
              ),
              const SizedBox(width: 25),
              const SizedBox(width: 0),
              Flexible(
                flex: 3,
                child: _SlotLyricsPanel(
                  trackTitle: track.title,
                  lyrics: lyrics,
                  currentIndex: currentLyricIndex,
                  currentPosition: playback.currentTime,
                  noLyricsText: lyricsLoading
                      ? t.loadingLyrics
                      : t.noLyricsFound,
                  onRenderDiagnostic: (message) {
                    playbackCtrl.appendDeveloperLog(message);
                  },
                ),
              ),
            ],
          ),
        ),
        Positioned(
          right: 24,
          bottom: 24,
          child: _LyricsQuickActions(
            track: track,
            selectedSource: effectiveLyricsSource,
            isLoading: lyricsLoading,
            initialOffsetSeconds: lyricsOffsetSeconds,
            t: t,
            lowEffects: library.lowEffects,
          ),
        ),
      ],
    );
  }

  int _resolveCurrentLyricIndex(List<LyricLine> lyrics, Duration position) {
    if (lyrics.isEmpty) return -1;
    for (var i = lyrics.length - 1; i >= 0; i--) {
      if (position >= lyrics[i].time) return i;
    }
    return 0;
  }

  String _formatDuration(Duration? duration) {
    if (duration == null || duration <= Duration.zero) return '--:--';
    final minutes = duration.inMinutes.remainder(60).toString().padLeft(2, '0');
    final seconds = duration.inSeconds.remainder(60).toString().padLeft(2, '0');
    final hours = duration.inHours;
    if (hours > 0) {
      return '${hours.toString().padLeft(2, '0')}:$minutes:$seconds';
    }
    return '$minutes:$seconds';
  }
}

class _SlotLyricsPanel extends StatefulWidget {
  const _SlotLyricsPanel({
    required this.trackTitle,
    required this.lyrics,
    required this.currentIndex,
    required this.currentPosition,
    required this.noLyricsText,
    required this.onRenderDiagnostic,
  });

  final String trackTitle;
  final List<LyricLine> lyrics;
  final int currentIndex;
  final Duration currentPosition;
  final String noLyricsText;
  final void Function(String message) onRenderDiagnostic;

  @override
  State<_SlotLyricsPanel> createState() => _SlotLyricsPanelState();
}

class _SlotLyricsPanelState extends State<_SlotLyricsPanel> {
  static const double _itemExtent = 102;
  late final ScrollController _controller;
  String? _lastRenderDiagnosticKey;

  @override
  void initState() {
    super.initState();
    _controller = ScrollController();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _jumpToCurrent();
      _emitRenderDiagnostic();
    });
  }

  @override
  void didUpdateWidget(covariant _SlotLyricsPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    final next = _safeIndex(widget.currentIndex);
    final prev = _safeIndex(oldWidget.currentIndex);

    if (next != prev) {
      _animateToCurrent(prev, next);
      _emitRenderDiagnostic();
      return;
    }

    if (widget.lyrics.length != oldWidget.lyrics.length) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        _jumpToCurrent();
        _emitRenderDiagnostic();
      });
      return;
    }

    if (widget.currentPosition != oldWidget.currentPosition &&
        widget.currentIndex >= 0) {
      _emitRenderDiagnostic();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  int _safeIndex(int index) {
    if (widget.lyrics.isEmpty) return 0;
    if (index < 0) return 0;
    if (index >= widget.lyrics.length) return widget.lyrics.length - 1;
    return index;
  }

  double _targetOffset(int index) {
    return _safeIndex(index) * _itemExtent;
  }

  double _rawLineProgress(int index, Duration position) {
    if (widget.lyrics.isEmpty) return 0;
    if (index < 0 || index >= widget.lyrics.length) return 0;

    final current = widget.lyrics[index];
    final nextTime = index + 1 < widget.lyrics.length
        ? widget.lyrics[index + 1].time
        : current.time + const Duration(seconds: 3);
    final spanMs = (nextTime - current.time).inMilliseconds;
    if (spanMs <= 0) return index < widget.currentIndex ? 1 : 0;

    final elapsedMs = (position - current.time).inMilliseconds.toDouble();
    return (elapsedMs / spanMs).clamp(0.0, 1.0);
  }

  double _lineProgress(int index) {
    final raw = _rawLineProgress(index, widget.currentPosition);
    return Curves.easeInOut.transform(raw);
  }

  void _emitRenderDiagnostic() {
    final lyrics = widget.lyrics;
    final safeCurrent = lyrics.isEmpty ? -1 : _safeIndex(widget.currentIndex);
    final activeLine = safeCurrent >= 0 ? lyrics[safeCurrent] : null;
    final activeSegments = activeLine?.segments.length ?? 0;
    final karaokeLines = lyrics.where((line) => line.hasTimedSegments).length;
    final totalSegments = lyrics.fold<int>(
      0,
      (total, line) => total + line.segments.length,
    );
    final mode = activeSegments > 0 ? 'timed-karaoke' : 'fallback-average';
    final key =
        '$mode|$safeCurrent|$activeSegments|$karaokeLines|$totalSegments';
    if (_lastRenderDiagnosticKey == key) return;
    _lastRenderDiagnosticKey = key;
    widget.onRenderDiagnostic(
      'lyrics.render -> track="${widget.trackTitle}", mode=$mode, '
      'currentIndex=$safeCurrent, activeSegments=$activeSegments, '
      'karaokeLines=$karaokeLines, totalSegments=$totalSegments',
    );
  }

  void _jumpToCurrent() {
    if (!mounted || !_controller.hasClients) return;
    final maxScroll = _controller.position.maxScrollExtent;
    final target = _targetOffset(widget.currentIndex).clamp(0.0, maxScroll);
    _controller.jumpTo(target);
  }

  void _animateToCurrent(int previousIndex, int nextIndex) {
    if (!mounted) return;

    if (!_controller.hasClients) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        _jumpToCurrent();
      });
      return;
    }

    final maxScroll = _controller.position.maxScrollExtent;
    final current = _controller.offset.clamp(0.0, maxScroll);
    final target = _targetOffset(nextIndex).clamp(0.0, maxScroll);
    final deltaPixels = (target - current).abs();
    if (deltaPixels < 0.5) return;

    if (deltaPixels >= _itemExtent * 4) {
      _controller.jumpTo(target);
      return;
    }

    final deltaLines = (deltaPixels / _itemExtent).clamp(1.0, 4.0);
    final durationMs = (180 + ((deltaLines - 1) * 70)).round().clamp(180, 390);

    unawaited(
      _controller.animateTo(
        target,
        duration: Duration(milliseconds: durationMs),
        curve: Curves.easeOutCubic,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    if (widget.lyrics.isEmpty) {
      return Center(
        child: Text(
          widget.noLyricsText,
          style: TextStyle(
            color: Colors.white.withValues(alpha: 0.70),
            fontSize: 16,
          ),
        ),
      );
    }

    return LayoutBuilder(
      builder: (context, constraints) {
        final safeCurrent = _safeIndex(widget.currentIndex);
        final topPadding = math.max(
          0.0,
          (constraints.maxHeight / 2) - (_itemExtent / 2),
        );

        return ScrollConfiguration(
          behavior: ScrollConfiguration.of(context).copyWith(scrollbars: false),
          child: MiddleClickAutoScrollView(
            controller: _controller,
            builder: (context, controller) => ListView.builder(
              controller: controller,
              itemExtent: _itemExtent,
              physics: const NeverScrollableScrollPhysics(),
              padding: EdgeInsets.symmetric(vertical: topPadding),
              itemCount: widget.lyrics.length,
              itemBuilder: (_, index) {
                final active = index == safeCurrent;
                final distance = (index - safeCurrent).abs();
                return Center(
                  child: _SlotLyricText(
                    key: ValueKey(
                      'slot-line-$index-${widget.lyrics[index].time.inMilliseconds}',
                    ),
                    text: widget.lyrics[index].text,
                    segments: widget.lyrics[index].segments,
                    active: active,
                    distance: distance,
                    progress: active ? _lineProgress(index) : 0,
                    currentPosition: widget.currentPosition,
                  ),
                );
              },
            ),
          ),
        );
      },
    );
  }
}

class _SlotLyricText extends StatelessWidget {
  const _SlotLyricText({
    super.key,
    required this.text,
    required this.segments,
    required this.active,
    required this.distance,
    required this.progress,
    required this.currentPosition,
  });

  final String text;
  final List<LyricSegment> segments;
  final bool active;
  final int distance;
  final double progress;
  final Duration currentPosition;

  @override
  Widget build(BuildContext context) {
    final inactiveBlur = distance <= 1 ? 2.8 : 5.2;
    final inactiveSize = distance <= 1 ? 24.0 : 20.0;
    final inactiveOpacity = distance <= 1 ? 0.66 : 0.44;

    final inactiveWidget = TweenAnimationBuilder<double>(
      tween: Tween<double>(begin: 0, end: inactiveBlur),
      duration: const Duration(milliseconds: 360),
      curve: Curves.easeInOutCubic,
      builder: (context, sigma, child) {
        return ImageFiltered(
          imageFilter: ImageFilter.blur(sigmaX: sigma, sigmaY: sigma),
          child: child,
        );
      },
      child: Text(
        text,
        maxLines: 3,
        overflow: TextOverflow.ellipsis,
        softWrap: true,
        textAlign: TextAlign.center,
        style: TextStyle(
          fontSize: inactiveSize,
          fontWeight: FontWeight.w500,
          color: Colors.white.withValues(alpha: inactiveOpacity),
          height: 1.24,
        ),
      ),
    );

    final baseStyle = TextStyle(
      fontSize: 30,
      fontWeight: FontWeight.w700,
      color: Colors.white.withValues(alpha: 0.34),
      height: 1.24,
    );
    final highlightStyle = TextStyle(
      fontSize: 30,
      fontWeight: FontWeight.w700,
      color: Colors.white.withValues(alpha: 0.98),
      height: 1.24,
    );
    final activeKaraokeWidget = segments.isNotEmpty
        ? _KaraokeLyricText(
            text: text,
            progress: progress,
            currentPosition: currentPosition,
            segments: segments,
            style: baseStyle,
            highlightStyle: highlightStyle,
          )
        : TweenAnimationBuilder<double>(
            tween: Tween<double>(begin: 0, end: progress),
            duration: const Duration(milliseconds: 140),
            curve: Curves.linearToEaseOut,
            builder: (context, animatedProgress, _) {
              return _KaraokeLyricText(
                text: text,
                progress: animatedProgress,
                currentPosition: currentPosition,
                segments: const <LyricSegment>[],
                style: baseStyle,
                highlightStyle: highlightStyle,
              );
            },
          );

    return AnimatedSwitcher(
      duration: const Duration(milliseconds: 380),
      switchInCurve: Curves.easeInOutCubic,
      switchOutCurve: Curves.easeInOutCubic,
      transitionBuilder: (child, animation) {
        return FadeTransition(
          opacity: animation,
          child: ScaleTransition(
            scale: Tween<double>(begin: 0.98, end: 1).animate(animation),
            child: child,
          ),
        );
      },
      child: active
          ? KeyedSubtree(
              key: ValueKey('active-$text'),
              child: activeKaraokeWidget,
            )
          : KeyedSubtree(
              key: ValueKey('inactive-$text-$distance'),
              child: inactiveWidget,
            ),
    );
  }
}

class _KaraokeLyricText extends StatelessWidget {
  const _KaraokeLyricText({
    required this.text,
    required this.progress,
    required this.currentPosition,
    required this.segments,
    required this.style,
    required this.highlightStyle,
  });

  final String text;
  final double progress;
  final Duration currentPosition;
  final List<LyricSegment> segments;
  final TextStyle style;
  final TextStyle highlightStyle;

  @override
  Widget build(BuildContext context) {
    if (segments.isNotEmpty) {
      return _buildTimedSegmentsText();
    }

    return _buildFallbackCharacterText();
  }

  Widget _buildFallbackCharacterText() {
    final segments = text.runes
        .map(String.fromCharCode)
        .toList(growable: false);
    final paintableIndexes = <int>[];
    for (var i = 0; i < segments.length; i++) {
      if (segments[i].trim().isNotEmpty) {
        paintableIndexes.add(i);
      }
    }

    final exactProgress = (paintableIndexes.length * progress).clamp(
      0.0,
      paintableIndexes.length.toDouble(),
    );
    final highlightedCount = exactProgress.floor();
    final partialHighlight = exactProgress - highlightedCount;
    final highlightedIndexes = paintableIndexes.take(highlightedCount).toSet();
    final partialIndex = highlightedCount < paintableIndexes.length
        ? paintableIndexes[highlightedCount]
        : null;
    final baseColor = style.color ?? Colors.white.withValues(alpha: 0.34);
    final highlightColor =
        highlightStyle.color ?? Colors.white.withValues(alpha: 0.98);
    final partialColor = Color.lerp(
      baseColor,
      highlightColor,
      Curves.easeOut.transform(partialHighlight),
    );

    return RichText(
      maxLines: 3,
      overflow: TextOverflow.ellipsis,
      softWrap: true,
      textAlign: TextAlign.center,
      text: TextSpan(
        children: [
          for (var i = 0; i < segments.length; i++)
            TextSpan(
              text: segments[i],
              style: highlightedIndexes.contains(i)
                  ? highlightStyle
                  : (partialIndex == i
                        ? highlightStyle.copyWith(color: partialColor)
                        : style),
            ),
        ],
      ),
    );
  }

  Widget _buildTimedSegmentsText() {
    final baseColor = style.color ?? Colors.white.withValues(alpha: 0.34);
    final highlightColor =
        highlightStyle.color ?? Colors.white.withValues(alpha: 0.98);

    return RichText(
      maxLines: 3,
      overflow: TextOverflow.ellipsis,
      softWrap: true,
      textAlign: TextAlign.center,
      text: TextSpan(
        children: [
          for (final segment in segments)
            TextSpan(
              text: segment.text,
              style: _resolveSegmentStyle(
                segment,
                baseColor: baseColor,
                highlightColor: highlightColor,
              ),
            ),
        ],
      ),
    );
  }

  TextStyle _resolveSegmentStyle(
    LyricSegment segment, {
    required Color baseColor,
    required Color highlightColor,
  }) {
    if (currentPosition >= segment.end) {
      return highlightStyle;
    }
    if (currentPosition <= segment.start) {
      return style;
    }

    final spanMs = (segment.end - segment.start).inMilliseconds;
    if (spanMs <= 0) {
      return highlightStyle;
    }

    final elapsedMs = (currentPosition - segment.start).inMilliseconds
        .toDouble();
    final rawProgress = (elapsedMs / spanMs).clamp(0.0, 1.0);
    final partialColor = Color.lerp(
      baseColor,
      highlightColor,
      Curves.easeOut.transform(rawProgress),
    );
    return highlightStyle.copyWith(color: partialColor);
  }
}

const String _localLyricsSvg = '''
<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
  <path d="M4 7.2C4 5.99 4.99 5 6.2 5H10.2L12.1 6.9H17.8C19.01 6.9 20 7.89 20 9.1V16.8C20 18.01 19.01 19 17.8 19H6.2C4.99 19 4 18.01 4 16.8V7.2Z" fill="currentColor"/>
  <path d="M14.8 10.1V14.35C14.53 14.17 14.19 14.06 13.82 14.06C12.9 14.06 12.15 14.71 12.15 15.5C12.15 16.29 12.9 16.94 13.82 16.94C14.74 16.94 15.49 16.29 15.49 15.5V11.08L17.3 10.68V13.62C17.03 13.44 16.69 13.33 16.32 13.33C15.4 13.33 14.65 13.98 14.65 14.77C14.65 15.56 15.4 16.21 16.32 16.21C17.24 16.21 17.99 15.56 17.99 14.77V8.96L14.8 10.1Z" fill="#0D1629"/>
</svg>
''';

const String _onlineLyricsSvg = '''
<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
  <path d="M7.5 18.25H17.2C18.75 18.25 20 17 20 15.45C20 14.06 18.99 12.9 17.66 12.68C17.48 9.95 15.21 7.8 12.43 7.8C10.12 7.8 8.14 9.25 7.35 11.31C5.49 11.4 4 12.95 4 14.83C4 16.76 5.57 18.25 7.5 18.25Z" fill="currentColor"/>
  <path d="M11.18 12.45V15.45C10.99 15.31 10.74 15.22 10.48 15.22C9.82 15.22 9.29 15.68 9.29 16.25C9.29 16.82 9.82 17.28 10.48 17.28C11.14 17.28 11.67 16.82 11.67 16.25V13.14L13.85 12.67V14.93C13.66 14.79 13.41 14.7 13.15 14.7C12.49 14.7 11.96 15.16 11.96 15.73C11.96 16.29 12.49 16.76 13.15 16.76C13.8 16.76 14.34 16.29 14.34 15.73V11.1L11.18 12.45Z" fill="#0D1629"/>
</svg>
''';

const String _searchLyricsSvg = '''
<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
  <circle cx="10.5" cy="10.5" r="5.5" fill="none" stroke="currentColor" stroke-width="2"/>
  <path d="M15 15L20 20" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
  <path d="M10.4 8.2V10.95L12.45 12.2" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
''';

class _LyricsQuickActions extends ConsumerStatefulWidget {
  const _LyricsQuickActions({
    required this.track,
    required this.selectedSource,
    required this.isLoading,
    required this.initialOffsetSeconds,
    required this.t,
    required this.lowEffects,
  });

  final Track track;
  final LyricsSourceType selectedSource;
  final bool isLoading;
  final double initialOffsetSeconds;
  final AppStrings t;
  final bool lowEffects;

  @override
  ConsumerState<_LyricsQuickActions> createState() =>
      _LyricsQuickActionsState();
}

class _LyricsQuickActionsState extends ConsumerState<_LyricsQuickActions> {
  static const double _buttonSize = 46;
  static const double _buttonGap = 10;
  static const double _panelWidth = 192;
  static const double _panelHeight = 282;
  late final TextEditingController _offsetController;
  late final FocusNode _offsetFocusNode;
  Timer? _offsetApplyDebounce;
  bool _expanded = false;
  bool _hovering = false;
  bool _showOffsetEditor = false;
  bool _offsetHasInvalidInput = false;
  bool _syncingOffsetText = false;

  @override
  void initState() {
    super.initState();
    _offsetController = TextEditingController();
    _offsetFocusNode = FocusNode();
    _syncOffsetEditorFromWidget();
    _offsetController.addListener(_validateOffsetInput);
    _offsetFocusNode.addListener(() {
      if (!_offsetFocusNode.hasFocus) {
        unawaited(_applyOffsetIfValid());
      } else if (mounted) {
        setState(() {});
      }
    });
  }

  @override
  void didUpdateWidget(covariant _LyricsQuickActions oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.track.path != widget.track.path ||
        oldWidget.initialOffsetSeconds != widget.initialOffsetSeconds) {
      _syncOffsetEditorFromWidget();
    }
  }

  @override
  void dispose() {
    _offsetApplyDebounce?.cancel();
    _offsetController.removeListener(_validateOffsetInput);
    _offsetController.dispose();
    _offsetFocusNode.dispose();
    super.dispose();
  }

  void _syncOffsetEditorFromWidget() {
    final offset = widget.initialOffsetSeconds;
    _syncingOffsetText = true;
    _offsetController.text = offset == 0 ? '' : offset.toStringAsFixed(1);
    _offsetController.selection = TextSelection.collapsed(
      offset: _offsetController.text.length,
    );
    _syncingOffsetText = false;
    _offsetHasInvalidInput = false;
  }

  void _validateOffsetInput() {
    if (_syncingOffsetText) return;
    final input = _offsetController.text.trim();
    final invalid =
        input.isNotEmpty && !RegExp(r'^[+-]?\d*\.?\d*$').hasMatch(input);
    if (invalid != _offsetHasInvalidInput && mounted) {
      setState(() {
        _offsetHasInvalidInput = invalid;
      });
    }
    if (invalid) {
      _offsetApplyDebounce?.cancel();
      return;
    }
    _scheduleOffsetApply();
  }

  void _scheduleOffsetApply() {
    _offsetApplyDebounce?.cancel();
    _offsetApplyDebounce = Timer(const Duration(milliseconds: 260), () {
      unawaited(_applyOffsetIfValid());
    });
  }

  Future<void> _toggleLyricsSource() async {
    await ref.read(libraryProvider.notifier).toggleLyricsSource(widget.track);
  }

  Future<void> _openSearch() async {
    await showDialog<void>(
      context: context,
      builder: (_) =>
          _OnlineLyricsSearchDialog(track: widget.track, t: widget.t),
    );
  }

  Future<void> _applyOffsetIfValid() async {
    final raw = _offsetController.text.trim();
    if (raw.isEmpty) {
      if (widget.initialOffsetSeconds != 0) {
        await ref
            .read(libraryProvider.notifier)
            .setLyricsOffsetSeconds(widget.track, 0);
      }
      if (mounted) {
        setState(() {
          _offsetHasInvalidInput = false;
        });
      }
      return;
    }

    if (!RegExp(r'^[+-]?\d*\.?\d*$').hasMatch(raw)) {
      if (mounted) {
        setState(() {
          _offsetHasInvalidInput = true;
        });
      }
      return;
    }

    final parsed = double.tryParse(raw);
    if (parsed == null) {
      if (mounted) {
        setState(() {
          _offsetHasInvalidInput = true;
        });
      }
      return;
    }

    final rounded = (parsed * 10).round() / 10.0;
    await ref
        .read(libraryProvider.notifier)
        .setLyricsOffsetSeconds(widget.track, rounded);
    if (!mounted) return;
    _syncingOffsetText = true;
    setState(() {
      _offsetHasInvalidInput = false;
      _offsetController.text = rounded == 0 ? '' : rounded.toStringAsFixed(1);
      _offsetController.selection = TextSelection.collapsed(
        offset: _offsetController.text.length,
      );
    });
    _syncingOffsetText = false;
  }

  @override
  Widget build(BuildContext context) {
    final shouldReveal = _hovering || _expanded || _offsetFocusNode.hasFocus;
    const editorWidth = 64.0;
    final editorBottom = _buttonSize + _buttonGap + ((_buttonSize - 42) / 2);
    final editorRight = _buttonSize + 12;

    return MouseRegion(
      onEnter: (_) {
        setState(() {
          _hovering = true;
        });
      },
      onExit: (_) {
        setState(() {
          _hovering = false;
        });
      },
      child: SizedBox(
        width: _panelWidth,
        height: _panelHeight,
        child: Align(
          alignment: Alignment.bottomRight,
          child: AbsorbPointer(
            absorbing: !shouldReveal,
            child: AnimatedOpacity(
              duration: const Duration(milliseconds: 260),
              curve: Curves.easeOutCubic,
              opacity: shouldReveal ? 1 : 0,
              child: AnimatedSlide(
                duration: const Duration(milliseconds: 260),
                curve: Curves.easeOutCubic,
                offset: shouldReveal ? Offset.zero : const Offset(0, 0.08),
                child: Stack(
                  alignment: Alignment.bottomRight,
                  clipBehavior: Clip.none,
                  children: [
                    AnimatedPositioned(
                      duration: const Duration(milliseconds: 220),
                      curve: Curves.easeOutCubic,
                      right: _showOffsetEditor ? editorRight : editorRight - 8,
                      bottom: editorBottom,
                      child: AnimatedSwitcher(
                        duration: const Duration(milliseconds: 220),
                        switchInCurve: Curves.easeOutCubic,
                        switchOutCurve: Curves.easeInCubic,
                        transitionBuilder: (child, animation) {
                          return FadeTransition(
                            opacity: animation,
                            child: SizeTransition(
                              sizeFactor: animation,
                              axis: Axis.horizontal,
                              axisAlignment: 1,
                              child: child,
                            ),
                          );
                        },
                        child: _showOffsetEditor
                            ? SizedBox(
                                key: const ValueKey('lyrics-offset-editor'),
                                width: editorWidth,
                                child: _buildOffsetEditor(),
                              )
                            : const SizedBox.shrink(
                                key: ValueKey('lyrics-offset-editor-hidden'),
                              ),
                      ),
                    ),
                    Positioned(
                      right: 0,
                      bottom: 0,
                      child: SizedBox(
                        width: _buttonSize,
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            AnimatedSwitcher(
                              duration: const Duration(milliseconds: 220),
                              switchInCurve: Curves.easeOutCubic,
                              switchOutCurve: Curves.easeInCubic,
                              transitionBuilder: (child, animation) {
                                return FadeTransition(
                                  opacity: animation,
                                  child: SizeTransition(
                                    sizeFactor: animation,
                                    axis: Axis.vertical,
                                    axisAlignment: 1,
                                    child: child,
                                  ),
                                );
                              },
                              child: _expanded
                                  ? Column(
                                      key: const ValueKey(
                                        'lyrics-tools-expanded',
                                      ),
                                      mainAxisSize: MainAxisSize.min,
                                      children: [
                                        _buildActionButton(
                                          tooltip: widget.t.toggleLyricsSource,
                                          child: SvgPicture.string(
                                            widget.selectedSource ==
                                                    LyricsSourceType.local
                                                ? _localLyricsSvg
                                                : _onlineLyricsSvg,
                                            width: 18,
                                            height: 18,
                                            colorFilter: const ColorFilter.mode(
                                              Colors.white,
                                              BlendMode.srcIn,
                                            ),
                                          ),
                                          onTap: _toggleLyricsSource,
                                          loading: widget.isLoading,
                                        ),
                                        const SizedBox(height: _buttonGap),
                                        _buildActionButton(
                                          tooltip: widget.t.onlineLyricsSearch,
                                          child: SvgPicture.string(
                                            _searchLyricsSvg,
                                            width: 18,
                                            height: 18,
                                            colorFilter: const ColorFilter.mode(
                                              Colors.white,
                                              BlendMode.srcIn,
                                            ),
                                          ),
                                          onTap: _openSearch,
                                        ),
                                        const SizedBox(height: _buttonGap),
                                        _buildActionButton(
                                          tooltip: widget.t.lyricsOffset,
                                          child: const Icon(
                                            Icons.tune_rounded,
                                            size: 18,
                                            color: Colors.white,
                                          ),
                                          onTap: () async {
                                            setState(() {
                                              _showOffsetEditor =
                                                  !_showOffsetEditor;
                                            });
                                            if (_showOffsetEditor) {
                                              await Future<void>.delayed(
                                                const Duration(
                                                  milliseconds: 40,
                                                ),
                                              );
                                              if (mounted) {
                                                _offsetFocusNode.requestFocus();
                                              }
                                            }
                                          },
                                        ),
                                        const SizedBox(height: _buttonGap),
                                      ],
                                    )
                                  : const SizedBox.shrink(
                                      key: ValueKey('lyrics-tools-collapsed'),
                                    ),
                            ),
                            _buildActionButton(
                              tooltip: widget.t.lyricsTools,
                              child: const Icon(
                                Icons.lyrics_outlined,
                                size: 19,
                                color: Colors.white,
                              ),
                              onTap: () {
                                setState(() {
                                  _expanded = !_expanded;
                                  if (!_expanded) {
                                    _showOffsetEditor = false;
                                    _offsetFocusNode.unfocus();
                                  }
                                });
                              },
                            ),
                          ],
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildOffsetEditor() {
    final borderColor = _offsetHasInvalidInput
        ? const Color(0xFFFF6E6E)
        : Colors.white.withValues(alpha: 0.16);
    return ClipRRect(
      borderRadius: BorderRadius.circular(16),
      child: BackdropFilter(
        filter: ImageFilter.blur(
          sigmaX: widget.lowEffects ? 8 : 14,
          sigmaY: widget.lowEffects ? 8 : 14,
        ),
        child: Container(
          width: 64,
          height: 42,
          padding: const EdgeInsets.symmetric(horizontal: 6),
          decoration: BoxDecoration(
            color: const Color(0xFF0B1220).withValues(alpha: 0.18),
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: borderColor),
          ),
          child: Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _offsetController,
                  focusNode: _offsetFocusNode,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                    signed: true,
                  ),
                  inputFormatters: [LengthLimitingTextInputFormatter(7)],
                  onSubmitted: (_) => _applyOffsetIfValid(),
                  onTapOutside: (_) {
                    _offsetFocusNode.unfocus();
                    unawaited(_applyOffsetIfValid());
                  },
                  decoration: InputDecoration(
                    isDense: true,
                    border: InputBorder.none,
                    errorText: _offsetHasInvalidInput ? '' : null,
                    errorStyle: const TextStyle(height: 0, fontSize: 0),
                    contentPadding: EdgeInsets.zero,
                  ),
                  textAlign: TextAlign.center,
                  textAlignVertical: TextAlignVertical.center,
                  style: TextStyle(
                    fontSize: 13,
                    color: Colors.white.withValues(alpha: 0.96),
                  ),
                ),
              ),
              const SizedBox(width: 2),
              Text(
                's',
                style: TextStyle(
                  color: Colors.white.withValues(alpha: 0.72),
                  fontSize: 16,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildActionButton({
    required String tooltip,
    required Widget child,
    required FutureOr<void> Function() onTap,
    bool loading = false,
  }) {
    return Tooltip(
      message: tooltip,
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: () => onTap(),
          borderRadius: BorderRadius.circular(999),
          child: ClipOval(
            child: BackdropFilter(
              filter: ImageFilter.blur(
                sigmaX: widget.lowEffects ? 8 : 14,
                sigmaY: widget.lowEffects ? 8 : 14,
              ),
              child: Ink(
                width: _buttonSize,
                height: _buttonSize,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: const Color(0xFF0B1220).withValues(alpha: 0.16),
                  border: Border.all(
                    color: Colors.white.withValues(alpha: 0.10),
                  ),
                ),
                child: Stack(
                  alignment: Alignment.center,
                  children: [
                    child,
                    if (loading)
                      Positioned(
                        right: 6,
                        bottom: 6,
                        child: SizedBox(
                          width: 10,
                          height: 10,
                          child: CircularProgressIndicator(
                            strokeWidth: 1.5,
                            color: Colors.white.withValues(alpha: 0.92),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _CoverSearchDialog extends ConsumerStatefulWidget {
  const _CoverSearchDialog({
    required this.track,
    required this.t,
    required this.lowEffects,
  });

  final Track track;
  final AppStrings t;
  final bool lowEffects;

  @override
  ConsumerState<_CoverSearchDialog> createState() => _CoverSearchDialogState();
}

class _CoverSearchDialogState extends ConsumerState<_CoverSearchDialog> {
  late final TextEditingController _queryController;
  bool _loading = false;
  bool _applying = false;
  bool _hasSearched = false;
  String? _error;
  List<OnlineCoverSearchResult> _results = const [];

  @override
  void initState() {
    super.initState();
    _queryController = TextEditingController(text: widget.track.title);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      unawaited(_search());
    });
  }

  @override
  void dispose() {
    _queryController.dispose();
    super.dispose();
  }

  Future<void> _search() async {
    final query = _queryController.text.trim();
    if (query.isEmpty) return;

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final results = await ref
          .read(libraryProvider.notifier)
          .searchOnlineCovers(widget.track, query);
      if (!mounted) return;
      setState(() {
        _hasSearched = true;
        _results = results;
        _loading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _hasSearched = true;
        _loading = false;
        _error = '$error';
      });
    }
  }

  Future<void> _selectCover(OnlineCoverSearchResult result) async {
    setState(() {
      _applying = true;
      _error = null;
    });

    final success = await ref
        .read(libraryProvider.notifier)
        .applyCustomCoverSelection(widget.track, result);

    if (!mounted) return;
    final nextState = ref.read(libraryProvider);
    setState(() {
      _applying = false;
      _error = nextState.error;
    });
    if (success) {
      Navigator.of(context).pop();
    }
  }

  @override
  Widget build(BuildContext context) {
    final t = widget.t;
    final blur = widget.lowEffects ? 10.0 : 18.0;

    return Dialog(
      backgroundColor: Colors.transparent,
      insetPadding: const EdgeInsets.symmetric(horizontal: 36, vertical: 32),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(26),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: blur, sigmaY: blur),
          child: Container(
            width: 920,
            height: 660,
            decoration: BoxDecoration(
              color: const Color(0xFF0B1220).withValues(alpha: 0.22),
              borderRadius: BorderRadius.circular(26),
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
              padding: const EdgeInsets.fromLTRB(22, 18, 22, 18),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Text(
                        t.replaceCover,
                        style: const TextStyle(
                          fontSize: 22,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const Spacer(),
                      IconButton(
                        tooltip: t.back,
                        onPressed: _applying
                            ? null
                            : () => Navigator.of(context).pop(),
                        icon: const Icon(Icons.close_rounded),
                      ),
                    ],
                  ),
                  const SizedBox(height: 4),
                  const SizedBox(height: 10),
                  Row(
                    children: [
                      Expanded(
                        child: TextField(
                          controller: _queryController,
                          decoration: InputDecoration(
                            hintText: t.coverSearchHint,
                            prefixIcon: const Icon(Icons.search_rounded),
                            border: OutlineInputBorder(
                              borderRadius: BorderRadius.circular(14),
                            ),
                          ),
                          onSubmitted: (_) => _search(),
                        ),
                      ),
                      const SizedBox(width: 12),
                      FilledButton(
                        onPressed: _loading || _applying ? null : _search,
                        child: Text(t.searchAction),
                      ),
                    ],
                  ),
                  const SizedBox(height: 18),
                  Expanded(child: _buildBody(t)),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildBody(AppStrings t) {
    if (_loading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (_error != null) {
      return Center(
        child: Text(
          _error!,
          style: TextStyle(color: Colors.white.withValues(alpha: 0.74)),
          textAlign: TextAlign.center,
        ),
      );
    }

    if (!_hasSearched) {
      return Center(
        child: Text(
          t.coverSearchHint,
          style: TextStyle(color: Colors.white.withValues(alpha: 0.68)),
        ),
      );
    }

    if (_results.isEmpty) {
      return Center(
        child: Text(
          t.noOnlineCoverResults,
          style: TextStyle(color: Colors.white.withValues(alpha: 0.68)),
        ),
      );
    }

    return GridView.builder(
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
        crossAxisCount: 3,
        crossAxisSpacing: 14,
        mainAxisSpacing: 14,
        childAspectRatio: 0.9,
      ),
      itemCount: _results.length,
      itemBuilder: (_, index) {
        final result = _results[index];
        return _CoverSearchCard(
          result: result,
          busy: _applying,
          onTap: _applying ? null : () => _selectCover(result),
        );
      },
    );
  }
}

class _CoverSearchCard extends StatefulWidget {
  const _CoverSearchCard({
    required this.result,
    required this.onTap,
    this.busy = false,
  });

  final OnlineCoverSearchResult result;
  final VoidCallback? onTap;
  final bool busy;

  @override
  State<_CoverSearchCard> createState() => _CoverSearchCardState();
}

class _CoverSearchCardState extends State<_CoverSearchCard> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final result = widget.result;
    return MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      cursor: widget.onTap == null
          ? SystemMouseCursors.basic
          : SystemMouseCursors.click,
      child: GestureDetector(
        onTap: widget.onTap,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 180),
          curve: Curves.easeOutCubic,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(20),
            color: Colors.white.withValues(alpha: _hovered ? 0.08 : 0.04),
            border: Border.all(
              color: Colors.white.withValues(alpha: _hovered ? 0.22 : 0.12),
            ),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Expanded(
                child: ClipRRect(
                  borderRadius: const BorderRadius.vertical(
                    top: Radius.circular(19),
                  ),
                  child: Stack(
                    fit: StackFit.expand,
                    children: [
                      Image.network(
                        result.thumbnailUrl,
                        fit: BoxFit.cover,
                        errorBuilder: (_, _, _) => Container(
                          color: Colors.white.withValues(alpha: 0.04),
                          child: const Icon(
                            Icons.broken_image_rounded,
                            color: Colors.white54,
                            size: 42,
                          ),
                        ),
                        loadingBuilder: (context, child, progress) {
                          if (progress == null) return child;
                          return Container(
                            color: Colors.white.withValues(alpha: 0.04),
                            alignment: Alignment.center,
                            child: const SizedBox(
                              width: 24,
                              height: 24,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            ),
                          );
                        },
                      ),
                    ],
                  ),
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(12, 10, 12, 12),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      result.title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 14.5,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      result.artist,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.64),
                        fontSize: 12.5,
                      ),
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

class _OnlineLyricsSearchDialog extends ConsumerStatefulWidget {
  const _OnlineLyricsSearchDialog({required this.track, required this.t});

  final Track track;
  final AppStrings t;

  @override
  ConsumerState<_OnlineLyricsSearchDialog> createState() =>
      _OnlineLyricsSearchDialogState();
}

class _OnlineLyricsSearchDialogState
    extends ConsumerState<_OnlineLyricsSearchDialog> {
  late final TextEditingController _queryController;
  bool _loading = false;
  bool _hasSearched = false;
  String? _error;
  List<OnlineLyricsSearchResult> _results = const [];

  @override
  void initState() {
    super.initState();
    _queryController = TextEditingController(text: widget.track.title);
  }

  @override
  void dispose() {
    _queryController.dispose();
    super.dispose();
  }

  Future<void> _search() async {
    final query = _queryController.text.trim();
    if (query.isEmpty) return;

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final results = await ref
          .read(libraryProvider.notifier)
          .searchOnlineLyrics(widget.track, query);
      final qqCount = results
          .where((item) => item.provider == 'qqmusic')
          .length;
      final lrclibCount = results
          .where((item) => item.provider == 'lrclib')
          .length;
      final timedCount = results.where((item) => item.hasTimedSegments).length;
      ref
          .read(playbackProvider.notifier)
          .appendDeveloperLog(
            'lyrics.search -> query="$query", total=${results.length}, '
            'qq=$qqCount, lrclib=$lrclibCount, timed=$timedCount',
          );
      if (!mounted) return;
      setState(() {
        _hasSearched = true;
        _results = results;
        _loading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _hasSearched = true;
        _loading = false;
        _error = '$error';
      });
    }
  }

  Future<void> _selectResult(OnlineLyricsSearchResult result) async {
    await ref
        .read(libraryProvider.notifier)
        .applyManualOnlineLyricsSelection(widget.track, result);
    if (!mounted) return;
    Navigator.of(context).pop();
  }

  @override
  Widget build(BuildContext context) {
    final t = widget.t;
    final library = ref.watch(libraryProvider);
    final blur = library.lowEffects ? 10.0 : 18.0;
    final currentDocument = library.lyricsDocumentOf(widget.track);
    final effectiveSource = library.effectiveLyricsSourceOf(widget.track);
    final currentSourceLabel = effectiveSource == LyricsSourceType.local
        ? t.localLyricsSource
        : t.onlineLyricsSource;
    final currentStatusLabel = currentDocument == null
        ? t.currentLyricsUnavailable
        : (currentDocument.isSynced
              ? t.syncedLyricsLabel
              : t.unsyncedLyricsLabel);
    final karaokeLabel = currentDocument?.hasTimedSegments == true
        ? t.karaokeSupported
        : t.karaokeUnsupported;

    return Dialog(
      backgroundColor: Colors.transparent,
      insetPadding: const EdgeInsets.symmetric(horizontal: 42, vertical: 36),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(26),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: blur, sigmaY: blur),
          child: Container(
            width: 720,
            height: 560,
            decoration: BoxDecoration(
              color: const Color(0xFF0B1220).withValues(alpha: 0.22),
              borderRadius: BorderRadius.circular(26),
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
              padding: const EdgeInsets.fromLTRB(20, 18, 20, 18),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Text(
                        t.onlineLyricsSearch,
                        style: const TextStyle(
                          fontSize: 20,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const Spacer(),
                      IconButton(
                        onPressed: () => Navigator.of(context).pop(),
                        icon: const Icon(Icons.close_rounded),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  Row(
                    children: [
                      Expanded(
                        child: TextField(
                          controller: _queryController,
                          decoration: InputDecoration(
                            hintText: t.onlineLyricsSearchHint,
                            border: OutlineInputBorder(
                              borderRadius: BorderRadius.circular(12),
                            ),
                            prefixIcon: const Icon(Icons.search_rounded),
                          ),
                          onSubmitted: (_) => _search(),
                        ),
                      ),
                      const SizedBox(width: 10),
                      FilledButton(
                        onPressed: _loading ? null : _search,
                        child: Text(t.searchAction),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  Container(
                    width: double.infinity,
                    padding: const EdgeInsets.symmetric(
                      horizontal: 14,
                      vertical: 12,
                    ),
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(14),
                      color: Colors.white.withValues(alpha: 0.05),
                      border: Border.all(
                        color: Colors.white.withValues(alpha: 0.12),
                      ),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          t.currentLyricsInfo,
                          style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w700,
                            color: Colors.white.withValues(alpha: 0.90),
                          ),
                        ),
                        const SizedBox(height: 10),
                        Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: [
                            _LyricsStatusChip(
                              label: '${t.lyricsSource}: $currentSourceLabel',
                              emphasized: true,
                            ),
                            _LyricsStatusChip(label: currentStatusLabel),
                            _LyricsStatusChip(
                              label: karaokeLabel,
                              highlighted:
                                  currentDocument?.hasTimedSegments == true,
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 14),
                  if (_loading)
                    const Expanded(
                      child: Center(child: CircularProgressIndicator()),
                    )
                  else if (_error != null)
                    Expanded(
                      child: Center(
                        child: Text(
                          _error!,
                          style: TextStyle(
                            color: Colors.white.withValues(alpha: 0.72),
                          ),
                        ),
                      ),
                    )
                  else if (!_hasSearched)
                    Expanded(
                      child: Center(
                        child: Text(
                          t.onlineLyricsSearchHint,
                          style: TextStyle(
                            color: Colors.white.withValues(alpha: 0.68),
                          ),
                        ),
                      ),
                    )
                  else if (_results.isEmpty)
                    Expanded(
                      child: Center(
                        child: Text(
                          t.noOnlineLyricsResults,
                          style: TextStyle(
                            color: Colors.white.withValues(alpha: 0.68),
                          ),
                        ),
                      ),
                    )
                  else
                    Expanded(
                      child: ListView.separated(
                        itemCount: _results.length,
                        separatorBuilder: (_, _) => Divider(
                          color: Colors.white.withValues(alpha: 0.08),
                          height: 1,
                        ),
                        itemBuilder: (_, index) {
                          final result = _results[index];
                          return ListTile(
                            onTap: () => _selectResult(result),
                            contentPadding: const EdgeInsets.symmetric(
                              horizontal: 8,
                              vertical: 4,
                            ),
                            title: Text(
                              '${result.title} - ${result.artist}',
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                            ),
                            subtitle: Text(
                              _formatLyricsSize(result.byteSize),
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(
                                color: Colors.white.withValues(alpha: 0.62),
                              ),
                            ),
                            trailing: _LyricsStatusChip(
                              label: result.badgeLabel,
                              highlighted: result.badgeHighlighted,
                              emphasized: result.badgeEmphasized,
                            ),
                          );
                        },
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

  String _formatLyricsSize(int size) {
    if (size >= 1024 * 1024) {
      return '${(size / (1024 * 1024)).toStringAsFixed(1)} MB';
    }
    if (size >= 1024) {
      return '${(size / 1024).toStringAsFixed(1)} KB';
    }
    return '$size B';
  }
}

class _LyricsStatusChip extends StatelessWidget {
  const _LyricsStatusChip({
    required this.label,
    this.emphasized = false,
    this.highlighted = false,
  });

  final String label;
  final bool emphasized;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    final backgroundColor = highlighted
        ? const Color(0xFF42D7FF).withValues(alpha: 0.16)
        : Colors.white.withValues(alpha: emphasized ? 0.10 : 0.06);
    final borderColor = highlighted
        ? const Color(0xFF79E5FF).withValues(alpha: 0.34)
        : Colors.white.withValues(alpha: emphasized ? 0.18 : 0.10);
    final textColor = highlighted
        ? const Color(0xFFD7F7FF)
        : Colors.white.withValues(alpha: emphasized ? 0.88 : 0.76);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(999),
        color: backgroundColor,
        border: Border.all(color: borderColor),
      ),
      child: Text(
        label,
        style: TextStyle(
          fontSize: 12.5,
          fontWeight: FontWeight.w600,
          color: textColor,
        ),
      ),
    );
  }
}

class _CoverImage extends StatelessWidget {
  const _CoverImage({
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
          colors: [Color(0xFF1C2A46), Color(0xFF23365A)],
        ),
      ),
      child: const Icon(Icons.music_note_rounded, color: Colors.white70),
    );
  }
}

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
      child: IconButton(
        onPressed: onPressed,
        icon: AnimatedSwitcher(
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
            key: ValueKey<String>('fullplay-playback-mode-${mode.name}'),
            width: 18,
            height: 18,
            semanticsLabel: tooltip,
            colorFilter: const ColorFilter.mode(Colors.white, BlendMode.srcIn),
          ),
        ),
      ),
    );
  }
}

class _ExpandableVolumeControl extends StatefulWidget {
  const _ExpandableVolumeControl({
    required this.tooltip,
    required this.volume,
    required this.onChanged,
  });

  final String tooltip;
  final double volume;
  final ValueChanged<double> onChanged;

  @override
  State<_ExpandableVolumeControl> createState() =>
      _ExpandableVolumeControlState();
}

class _ExpandableVolumeControlState extends State<_ExpandableVolumeControl> {
  bool _expanded = false;

  void _toggleExpanded() {
    setState(() {
      _expanded = !_expanded;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        IconButton(
          tooltip: widget.tooltip,
          onPressed: _toggleExpanded,
          icon: Icon(
            Icons.volume_up_rounded,
            color: Colors.white.withValues(alpha: 0.9),
            size: 20,
          ),
        ),
        ClipRect(
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 220),
            curve: Curves.easeOutCubic,
            width: _expanded ? 170 : 0,
            child: Row(
              children: [
                const SizedBox(width: 4),
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
                      value: widget.volume,
                      min: 0,
                      max: 1,
                      onChanged: widget.onChanged,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
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
      iconSize: 30,
      icon: SvgPicture.asset(
        iconPath,
        width: 30,
        height: 30,
        colorFilter: ColorFilter.mode(
          Colors.white.withValues(alpha: onPressed == null ? 0.42 : 0.94),
          BlendMode.srcIn,
        ),
      ),
    );
  }
}
