import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:window_manager/window_manager.dart';

import '../models/lyric_line.dart';
import '../models/top_bar_idle_mode.dart';
import '../providers.dart';
import '../state/app_settings_state.dart';

class WindowTopBar extends ConsumerStatefulWidget {
  const WindowTopBar({
    super.key,
    this.showBrand = false,
    this.showLyricBox = true,
  });

  final bool showBrand;
  final bool showLyricBox;

  @override
  ConsumerState<WindowTopBar> createState() => _WindowTopBarState();
}

class _WindowTopBarState extends ConsumerState<WindowTopBar> with WindowListener {
  bool _isMaximized = false;
  Timer? _quoteTimer;

  @override
  void initState() {
    super.initState();
    windowManager.addListener(this);
    _syncWindowState();
  }

  Future<void> _syncWindowState() async {
    final maximized = await windowManager.isMaximized();
    if (!mounted) return;
    setState(() {
      _isMaximized = maximized;
    });
  }

  @override
  void dispose() {
    _quoteTimer?.cancel();
    windowManager.removeListener(this);
    super.dispose();
  }

  @override
  void onWindowMaximize() => _syncWindowState();

  @override
  void onWindowUnmaximize() => _syncWindowState();

  @override
  Widget build(BuildContext context) {
    final playback = ref.watch(playbackProvider);
    final library = ref.watch(libraryProvider);
    final settings = ref.watch(appSettingsProvider);
    final track = playback.currentTrack;
    final showCurrentLyric = track != null && playback.isPlaying;

    String topBarText = '';
    if (widget.showLyricBox) {
      if (track != null) {
        unawaited(ref.read(libraryProvider.notifier).ensureLyricsLoaded(track));
      }
      if (settings.topBarIdleMode == TopBarIdleMode.quote &&
          settings.topBarQuoteText.trim().isEmpty) {
        unawaited(
          ref.read(appSettingsProvider.notifier).ensureTopBarQuote(
                forceRefresh: false,
              ),
        );
      }

      topBarText = !showCurrentLyric
          ? _resolveIdleText(settings)
          : _resolveCurrentLyric(
                library.lyricsOf(track),
                playback.currentTime,
              ) ??
              '';
    }

    final shouldRotateQuote = widget.showLyricBox &&
        !showCurrentLyric &&
        settings.topBarIdleMode == TopBarIdleMode.quote;
    _syncQuoteTimer(shouldRotateQuote);

    return Container(
      height: 44,
      padding: const EdgeInsets.only(left: 14),
      color: Colors.transparent,
      child: Row(
        children: [
          if (widget.showBrand) ...[
            Text(
              'PrismWave',
              style: TextStyle(
                fontWeight: FontWeight.w600,
                color: Colors.white.withValues(alpha: 0.94),
              ),
            ),
            const SizedBox(width: 12),
          ] else
            const SizedBox(width: 10),
          Expanded(
            child: DragToMoveArea(
              child: widget.showLyricBox
                  ? LayoutBuilder(
                      builder: (context, constraints) {
                        const previousCenteredWidth = 520.0;
                        final startInset = math.max(
                          0.0,
                          ((constraints.maxWidth - previousCenteredWidth) / 2) - 18,
                        );
                        final boxWidth = math.max(
                          220.0,
                          constraints.maxWidth - startInset - 4,
                        );

                        return Padding(
                          padding: EdgeInsets.only(left: startInset, right: 4),
                          child: Align(
                            alignment: Alignment.centerLeft,
                            child: SizedBox(
                              width: boxWidth,
                              child: _TopBarLyricBox(text: topBarText),
                            ),
                          ),
                        );
                      },
                    )
                  : const SizedBox.expand(),
            ),
          ),
          _WindowButton(
            icon: Icons.remove_rounded,
            onTap: windowManager.minimize,
          ),
          _WindowButton(
            icon: _isMaximized
                ? Icons.filter_none_rounded
                : Icons.crop_square_rounded,
            onTap: () async {
              if (_isMaximized) {
                await windowManager.unmaximize();
              } else {
                await windowManager.maximize();
              }
            },
          ),
          _WindowButton(
            icon: Icons.close_rounded,
            danger: true,
            onTap: windowManager.close,
          ),
        ],
      ),
    );
  }

  String _resolveIdleText(AppSettingsState settings) {
    return switch (settings.topBarIdleMode) {
      TopBarIdleMode.empty => '',
      TopBarIdleMode.custom => settings.topBarIdleText.trim(),
      TopBarIdleMode.quote => settings.topBarQuoteText.trim(),
    };
  }

  String? _resolveCurrentLyric(List<LyricLine> lyrics, Duration position) {
    if (lyrics.isEmpty) return null;
    for (var i = lyrics.length - 1; i >= 0; i--) {
      if (position >= lyrics[i].time) {
        final text = lyrics[i].text.trim();
        return text.isEmpty ? null : text;
      }
    }
    return null;
  }

  void _syncQuoteTimer(bool enabled) {
    if (!enabled) {
      _quoteTimer?.cancel();
      _quoteTimer = null;
      return;
    }

    if (_quoteTimer != null) return;

    _quoteTimer = Timer.periodic(const Duration(seconds: 10), (_) {
      unawaited(
        ref.read(appSettingsProvider.notifier).ensureTopBarQuote(
              forceRefresh: true,
            ),
      );
    });
  }
}

class _TopBarLyricBox extends StatelessWidget {
  const _TopBarLyricBox({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: Container(
        height: 30,
        padding: const EdgeInsets.symmetric(horizontal: 14),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(15),
          color: Colors.white.withValues(alpha: 0.06),
          border: Border.all(color: Colors.white.withValues(alpha: 0.10)),
        ),
        child: ClipRect(
          child: AnimatedSwitcher(
            duration: const Duration(milliseconds: 420),
            switchInCurve: Curves.easeOutCubic,
            switchOutCurve: Curves.easeInCubic,
            layoutBuilder: (currentChild, previousChildren) {
              return Stack(
                alignment: Alignment.centerLeft,
                children: [
                  ...previousChildren,
                  // ignore: use_null_aware_elements
                  if (currentChild case final child?) child,
                ],
              );
            },
            transitionBuilder: (child, animation) {
              final rotate = Tween<double>(
                begin: math.pi / 2.8,
                end: 0,
              ).animate(animation);
              final slide = Tween<Offset>(
                begin: const Offset(0, 0.25),
                end: Offset.zero,
              ).animate(animation);
              return AnimatedBuilder(
                animation: animation,
                child: SlideTransition(position: slide, child: child),
                builder: (context, animatedChild) {
                  final value = animation.value;
                  return Opacity(
                    opacity: value.clamp(0.0, 1.0),
                    child: Transform(
                      alignment: Alignment.topCenter,
                      transform: Matrix4.identity()
                        ..setEntry(3, 2, 0.0014)
                        ..rotateX((1 - value) * rotate.value),
                      child: animatedChild,
                    ),
                  );
                },
              );
            },
            child: Align(
              key: ValueKey(text),
              alignment: Alignment.centerLeft,
              child: Text(
                text,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  color: Colors.white.withValues(alpha: text.isEmpty ? 0 : 0.88),
                  fontSize: 13,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _WindowButton extends StatefulWidget {
  const _WindowButton({
    required this.icon,
    required this.onTap,
    this.danger = false,
  });

  final IconData icon;
  final Future<void> Function() onTap;
  final bool danger;

  @override
  State<_WindowButton> createState() => _WindowButtonState();
}

class _WindowButtonState extends State<_WindowButton> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final normalBg = Colors.transparent;
    final hoverBg = widget.danger
        ? const Color(0xFFEE3A49)
        : Colors.white.withValues(alpha: 0.12);

    return MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        onTap: widget.onTap,
        behavior: HitTestBehavior.opaque,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 160),
          curve: Curves.easeOutCubic,
          width: 46,
          height: 44,
          color: _hovered ? hoverBg : normalBg,
          child: Icon(
            widget.icon,
            size: 18,
            color: Colors.white.withValues(alpha: 0.88),
          ),
        ),
      ),
    );
  }
}
