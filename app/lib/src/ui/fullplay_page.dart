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
            child: WindowTopBar(showBrand: false),
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
              SizedBox(
                width: 360,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    IconButton(
                      tooltip: t.back,
                      onPressed: () => Navigator.of(context).maybePop(),
                      icon: const Icon(Icons.keyboard_arrow_down_rounded),
                    ),
                    const SizedBox(height: 8),
                    Center(
                      child: SizedBox(
                        width: 236,
                        height: 236,
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
                        constraints: const BoxConstraints(maxWidth: 300),
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
                        constraints: const BoxConstraints(maxWidth: 300),
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
                    const SizedBox(height: 4),
                    Row(
                      children: [
                        SizedBox(
                          width: 52,
                          child: Align(
                            alignment: Alignment.centerRight,
                            child: Icon(
                              Icons.volume_up_rounded,
                              size: 17,
                              color: Colors.white.withValues(alpha: 0.86),
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        SizedBox(
                          width: 220,
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
                              value: playback.volume,
                              min: 0,
                              max: 1,
                              onChanged: playbackCtrl.setVolume,
                            ),
                          ),
                        ),
                        const SizedBox(width: 52),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 22),
              Expanded(
                child: _SlotLyricsPanel(
                  lyrics: lyrics,
                  currentIndex: currentLyricIndex,
                  noLyricsText: t.noLyricsFound,
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
    required this.noLyricsText,
  });

  final List<LyricLine> lyrics;
  final int currentIndex;
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
          behavior: ScrollConfiguration.of(context).copyWith(scrollbars: false),
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
  });

  final String text;
  final bool active;
  final int distance;

  @override
  Widget build(BuildContext context) {
    final inactiveBlur = distance <= 1 ? 2.8 : 5.2;
    final inactiveSize = distance <= 1 ? 24.0 : 20.0;
    final inactiveOpacity = distance <= 1 ? 0.66 : 0.44;

    final activeWidget = TweenAnimationBuilder<double>(
      tween: Tween<double>(begin: 10, end: 0),
      duration: const Duration(milliseconds: 420),
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
          fontSize: 30,
          fontWeight: FontWeight.w700,
          color: Colors.white.withValues(alpha: 0.98),
          height: 1.24,
        ),
      ),
    );

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
          ? KeyedSubtree(key: ValueKey('active-$text'), child: activeWidget)
          : KeyedSubtree(
              key: ValueKey('inactive-$text-$distance'),
              child: inactiveWidget,
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
