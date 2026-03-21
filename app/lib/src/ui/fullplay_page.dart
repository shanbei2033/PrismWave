import 'dart:async';
import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';
import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../i18n/app_strings.dart';
import '../models/lyric_line.dart';
import '../models/lyrics_source_type.dart';
import '../models/online_lyrics_search_result.dart';
import '../models/playback_mode.dart';
import '../models/track.dart';
import '../providers.dart';
import '../state/library_state.dart';
import '../state/playback_state.dart';
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
                    final panelWidth = math.max(320.0, constraints.maxWidth - 12);
                    final coverSide = (panelWidth * 0.72).clamp(240.0, 420.0);

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
                                onPressed: () => Navigator.of(context).maybePop(),
                                icon: const Icon(Icons.keyboard_arrow_down_rounded),
                              ),
                            ),
                            const SizedBox(height: 8),
                            Center(
                              child: SizedBox(
                                width: coverSide,
                                height: coverSide,
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
                                          ? (value) => playbackCtrl.seekTo(
                                              Duration(milliseconds: value.round()),
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
                            const SizedBox(height: 10),
                            _LyricsSourceSelector(
                              track: track,
                              selectedSource: effectiveLyricsSource,
                              isLoading: lyricsLoading,
                              t: t,
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
                  lyrics: lyrics,
                  currentIndex: currentLyricIndex,
                  currentPosition: playback.currentTime,
                  noLyricsText: lyricsLoading
                      ? t.loadingLyrics
                      : t.noLyricsFound,
                ),
              ),
            ],
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
    required this.lyrics,
    required this.currentIndex,
    required this.currentPosition,
    required this.noLyricsText,
  });

  final List<LyricLine> lyrics;
  final int currentIndex;
  final Duration currentPosition;
  final String noLyricsText;

  @override
  State<_SlotLyricsPanel> createState() => _SlotLyricsPanelState();
}

class _SlotLyricsPanelState extends State<_SlotLyricsPanel> {
  static const double _itemExtent = 102;
  late final ScrollController _controller;

  @override
  void initState() {
    super.initState();
    _controller = ScrollController();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _jumpToCurrent();
    });
  }

  @override
  void didUpdateWidget(covariant _SlotLyricsPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    final next = _safeIndex(widget.currentIndex);
    final prev = _safeIndex(oldWidget.currentIndex);

    if (next != prev) {
      _animateToCurrent(prev, next);
      return;
    }

    if (widget.lyrics.length != oldWidget.lyrics.length) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        _jumpToCurrent();
      });
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

  double _lineProgress(int index) {
    if (widget.lyrics.isEmpty) return 0;
    if (index < 0 || index >= widget.lyrics.length) return 0;

    final current = widget.lyrics[index];
    final nextTime = index + 1 < widget.lyrics.length
        ? widget.lyrics[index + 1].time
        : current.time + const Duration(seconds: 3);
    final spanMs = (nextTime - current.time).inMilliseconds;
    if (spanMs <= 0) return index < widget.currentIndex ? 1 : 0;

    final elapsedMs =
        (widget.currentPosition - current.time).inMilliseconds.toDouble();
    final raw = (elapsedMs / spanMs).clamp(0.0, 1.0);
    return Curves.easeInOut.transform(raw);
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
    final target = _targetOffset(nextIndex).clamp(0.0, maxScroll);
    final delta = (nextIndex - previousIndex).abs();
    final durationMs = (delta * 230).clamp(280, 1200);

    _controller.animateTo(
      target,
      duration: Duration(milliseconds: durationMs),
      curve: Curves.easeInOutCubic,
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
          behavior: ScrollConfiguration.of(context).copyWith(
            scrollbars: false,
          ),
          child: ListView.builder(
            controller: _controller,
            itemExtent: _itemExtent,
            physics: const NeverScrollableScrollPhysics(),
            padding: EdgeInsets.symmetric(vertical: topPadding),
            itemCount: widget.lyrics.length,
            itemBuilder: (_, index) {
              final active = index == safeCurrent;
              final distance = (index - safeCurrent).abs();
              return Center(
                child: _SlotLyricText(
                  key: ValueKey('slot-line-$index-$safeCurrent'),
                  text: widget.lyrics[index].text,
                  active: active,
                  distance: distance,
                  progress: active ? _lineProgress(index) : 0,
                ),
              );
            },
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
    required this.active,
    required this.distance,
    required this.progress,
  });

  final String text;
  final bool active;
  final int distance;
  final double progress;

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

    final activeKaraokeWidget = TweenAnimationBuilder<double>(
      tween: Tween<double>(begin: 0, end: progress),
      duration: const Duration(milliseconds: 140),
      curve: Curves.linearToEaseOut,
      builder: (context, animatedProgress, _) {
        return _KaraokeLyricText(
          text: text,
          progress: animatedProgress,
          style: TextStyle(
            fontSize: 30,
            fontWeight: FontWeight.w700,
            color: Colors.white.withValues(alpha: 0.34),
            height: 1.24,
          ),
          highlightStyle: TextStyle(
            fontSize: 30,
            fontWeight: FontWeight.w700,
            color: Colors.white.withValues(alpha: 0.98),
            height: 1.24,
          ),
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
    required this.style,
    required this.highlightStyle,
  });

  final String text;
  final double progress;
  final TextStyle style;
  final TextStyle highlightStyle;

  @override
  Widget build(BuildContext context) {
    final segments = text.runes.map(String.fromCharCode).toList(growable: false);
    final paintableIndexes = <int>[];
    for (var i = 0; i < segments.length; i++) {
      if (segments[i].trim().isNotEmpty) {
        paintableIndexes.add(i);
      }
    }

    final exactProgress =
        (paintableIndexes.length * progress).clamp(0.0, paintableIndexes.length.toDouble());
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

class _LyricsSourceSelector extends ConsumerWidget {
  const _LyricsSourceSelector({
    required this.track,
    required this.selectedSource,
    required this.isLoading,
    required this.t,
  });

  final Track track;
  final LyricsSourceType selectedSource;
  final bool isLoading;
  final AppStrings t;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(libraryProvider.notifier);
    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        _LyricsSourceIconButton(
          tooltip: t.localLyricsSource,
          svg: _localLyricsSvg,
          selected: selectedSource == LyricsSourceType.local,
          onPressed: () => controller.selectLyricsSource(
            track,
            LyricsSourceType.local,
          ),
        ),
        const SizedBox(width: 12),
        _LyricsSourceIconButton(
          tooltip: t.onlineLyricsSource,
          svg: _onlineLyricsSvg,
          selected: selectedSource == LyricsSourceType.online,
          loading: isLoading,
          onPressed: () => controller.selectLyricsSource(
            track,
            LyricsSourceType.online,
          ),
        ),
        const SizedBox(width: 12),
        _LyricsSourceIconButton(
          tooltip: t.onlineLyricsSearch,
          svg: _searchLyricsSvg,
          onPressed: () async {
            await showDialog<void>(
              context: context,
              builder: (_) => _OnlineLyricsSearchDialog(track: track, t: t),
            );
          },
        ),
      ],
    );
  }
}

class _LyricsSourceIconButton extends StatefulWidget {
  const _LyricsSourceIconButton({
    required this.tooltip,
    required this.svg,
    required this.onPressed,
    this.selected = false,
    this.loading = false,
  });

  final String tooltip;
  final String svg;
  final FutureOr<void> Function() onPressed;
  final bool selected;
  final bool loading;

  @override
  State<_LyricsSourceIconButton> createState() =>
      _LyricsSourceIconButtonState();
}

class _LyricsSourceIconButtonState extends State<_LyricsSourceIconButton> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final iconColor = widget.selected
        ? Colors.white
        : Colors.white.withValues(alpha: 0.78);

    return MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      cursor: SystemMouseCursors.click,
      child: Tooltip(
        message: widget.tooltip,
        child: GestureDetector(
          onTap: () {
            widget.onPressed();
          },
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 160),
            curve: Curves.easeOut,
            width: 42,
            height: 42,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: widget.selected
                  ? Colors.white.withValues(alpha: 0.14)
                  : (_hovered
                        ? Colors.white.withValues(alpha: 0.05)
                        : Colors.transparent),
              border: _hovered
                  ? Border.all(
                      color: Colors.white.withValues(alpha: 0.26),
                      width: 1,
                    )
                  : null,
            ),
            child: Stack(
              alignment: Alignment.center,
              children: [
                SvgPicture.string(
                  widget.svg,
                  width: 18,
                  height: 18,
                  colorFilter: ColorFilter.mode(iconColor, BlendMode.srcIn),
                ),
                if (widget.loading)
                  Positioned(
                    right: 4,
                    bottom: 4,
                    child: SizedBox(
                      width: 10,
                      height: 10,
                      child: CircularProgressIndicator(
                        strokeWidth: 1.6,
                        color: Colors.white.withValues(alpha: 0.92),
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

class _OnlineLyricsSearchDialog extends ConsumerStatefulWidget {
  const _OnlineLyricsSearchDialog({
    required this.track,
    required this.t,
  });

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

    return Dialog(
      backgroundColor: const Color(0xFF0D1629),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
      child: SizedBox(
        width: 720,
        height: 560,
        child: Padding(
          padding: const EdgeInsets.all(18),
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
                        trailing: result.isSynced
                            ? Text(
                                'LRC',
                                style: TextStyle(
                                  color: Colors.white.withValues(alpha: 0.78),
                                  fontSize: 12,
                                  fontWeight: FontWeight.w600,
                                ),
                              )
                            : Text(
                                'TXT',
                                style: TextStyle(
                                  color: Colors.white.withValues(alpha: 0.52),
                                  fontSize: 12,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                      );
                    },
                  ),
                ),
            ],
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
        errorBuilder: (_, _, _) => _placeholder(),
      );
    }

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

class _ExpandableVolumeControl extends StatefulWidget {
  const _ExpandableVolumeControl({
    required this.volume,
    required this.onChanged,
  });

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
          tooltip: 'Volume',
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
