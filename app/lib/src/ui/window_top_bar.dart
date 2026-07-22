import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:window_manager/window_manager.dart';

import '../models/lyric_line.dart';
import '../models/top_bar_idle_mode.dart';
import '../providers.dart';
import '../state/app_settings_state.dart';
import 'prismwave_theme.dart';

class WindowTopBar extends ConsumerStatefulWidget {
  const WindowTopBar({
    super.key,
    this.showBrand = false,
    this.showLyricBox = true,
    this.showCommandBar = false,
    this.searchPlaceholder = '',
    this.onSearchSubmitted,
    this.onBack,
  });

  final bool showBrand;
  final bool showLyricBox;
  final bool showCommandBar;
  final String searchPlaceholder;
  final ValueChanged<String>? onSearchSubmitted;
  final VoidCallback? onBack;

  @override
  ConsumerState<WindowTopBar> createState() => _WindowTopBarState();
}

class _WindowTopBarState extends ConsumerState<WindowTopBar>
    with WindowListener {
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
    final topBarFeatureEnabled =
        settings.topBarIdleMode != TopBarIdleMode.empty;

    String topBarText = '';
    if (widget.showLyricBox) {
      if (topBarFeatureEnabled && track != null) {
        unawaited(
          ref
              .read(libraryProvider.notifier)
              .ensureLyricsLoaded(track, durationHint: playback.duration),
        );
      }
      if (topBarFeatureEnabled &&
          settings.topBarIdleMode == TopBarIdleMode.quote &&
          settings.topBarQuoteText.trim().isEmpty) {
        unawaited(
          ref
              .read(appSettingsProvider.notifier)
              .ensureTopBarQuote(forceRefresh: false),
        );
      }

      if (topBarFeatureEnabled) {
        topBarText = !showCurrentLyric
            ? _resolveIdleText(settings)
            : _resolveCurrentLyric(
                    library.lyricsOf(track),
                    playback.currentTime,
                  ) ??
                  '';
      }
    }

    final shouldRotateQuote =
        widget.showLyricBox &&
        topBarFeatureEnabled &&
        !showCurrentLyric &&
        settings.topBarIdleMode == TopBarIdleMode.quote;
    final shouldShowBox =
        widget.showLyricBox &&
        topBarFeatureEnabled &&
        topBarText.trim().isNotEmpty;
    _syncQuoteTimer(shouldRotateQuote);

    return Container(
      height: widget.showCommandBar ? 58 : 44,
      padding: EdgeInsets.only(left: widget.showCommandBar ? 286 : 14),
      color: Colors.transparent,
      child: Row(
        children: [
          if (widget.showCommandBar)
            _TopCommandCluster(
              placeholder: widget.searchPlaceholder,
              onSubmitted: widget.onSearchSubmitted,
              onBack: widget.onBack,
            )
          else if (widget.showBrand) ...[
            Text(
              'PrismWave',
              style: const TextStyle(
                color: PrismWaveTheme.textPrimary,
                fontWeight: FontWeight.w700,
                letterSpacing: 0,
              ),
            ),
            const SizedBox(width: 12),
          ] else
            const SizedBox(width: 10),
          Expanded(
            child: DragToMoveArea(
              child: AnimatedSwitcher(
                duration: const Duration(milliseconds: 260),
                switchInCurve: Curves.easeOutCubic,
                switchOutCurve: Curves.easeInCubic,
                transitionBuilder: (child, animation) {
                  return FadeTransition(
                    opacity: animation,
                    child: SizeTransition(
                      sizeFactor: animation,
                      axis: Axis.horizontal,
                      axisAlignment: -1,
                      child: child,
                    ),
                  );
                },
                child: shouldShowBox
                    ? LayoutBuilder(
                        key: const ValueKey('topbar-box-visible'),
                        builder: (context, constraints) {
                          // Align the lyric/quote box with the main content area.
                          // Main page content starts after:
                          // window padding 16 + sidebar 260 + gutter 14,
                          // while the drag area itself already starts after
                          // top bar left padding 14 + placeholder 10.
                          final contentStartInset = widget.showCommandBar
                              ? 26.0
                              : 266.0;
                          const trailingGap = 8.0;
                          final boxWidth = math.max(
                            0.0,
                            constraints.maxWidth -
                                contentStartInset -
                                trailingGap,
                          );

                          return Padding(
                            padding: EdgeInsets.only(
                              left: contentStartInset,
                              right: trailingGap,
                            ),
                            child: Align(
                              alignment: Alignment.centerLeft,
                              child: SizedBox(
                                width: boxWidth,
                                child: _TopBarLyricBox(
                                  text: topBarText,
                                  useQuoteTypography: shouldRotateQuote,
                                ),
                              ),
                            ),
                          );
                        },
                      )
                    : const SizedBox(
                        key: ValueKey('topbar-box-hidden'),
                        height: double.infinity,
                      ),
              ),
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
        ref
            .read(appSettingsProvider.notifier)
            .ensureTopBarQuote(forceRefresh: true),
      );
    });
  }
}

class _TopCommandCluster extends StatefulWidget {
  const _TopCommandCluster({
    required this.placeholder,
    required this.onSubmitted,
    required this.onBack,
  });

  final String placeholder;
  final ValueChanged<String>? onSubmitted;
  final VoidCallback? onBack;

  @override
  State<_TopCommandCluster> createState() => _TopCommandClusterState();
}

class _TopCommandClusterState extends State<_TopCommandCluster> {
  late final TextEditingController _controller;

  @override
  void initState() {
    super.initState();
    _controller = TextEditingController();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
        width: 500,
        child: Row(
          children: [
            _TopRoundButton(
              icon: Icons.chevron_left_rounded,
              tooltip: 'Back',
              onTap: widget.onBack,
            ),
            const SizedBox(width: 8),
            _TopRoundButton(
              icon: Icons.chevron_right_rounded,
              tooltip: 'Forward',
              onTap: null,
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Container(
                height: 40,
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(14),
                  color: const Color(0xFF111722).withValues(alpha: 0.74),
                  border: Border.all(
                    color: Colors.white.withValues(alpha: 0.08),
                  ),
                  boxShadow: [
                    BoxShadow(
                      color: Colors.black.withValues(alpha: 0.16),
                      blurRadius: 18,
                      offset: const Offset(0, 10),
                    ),
                  ],
                ),
                child: TextField(
                  controller: _controller,
                  onSubmitted: (value) {
                    widget.onSubmitted?.call(value);
                    _controller.clear();
                  },
                  style: const TextStyle(fontSize: 13.5),
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    border: InputBorder.none,
                    hintText: widget.placeholder,
                    hintStyle: TextStyle(
                      color: PrismWaveTheme.textMuted.withValues(alpha: 0.78),
                      fontSize: 13,
                    ),
                    prefixIcon: Icon(
                      Icons.search_rounded,
                      size: 19,
                      color: PrismWaveTheme.textSecondary.withValues(
                        alpha: 0.82,
                      ),
                    ),
                    contentPadding: const EdgeInsets.symmetric(vertical: 10),
                  ),
                ),
              ),
            ),
          ],
        ),
    );
  }
}

class _TopRoundButton extends StatefulWidget {
  const _TopRoundButton({
    required this.icon,
    required this.tooltip,
    required this.onTap,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback? onTap;

  @override
  State<_TopRoundButton> createState() => _TopRoundButtonState();
}

class _TopRoundButtonState extends State<_TopRoundButton> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final enabled = widget.onTap != null;
    return Tooltip(
      message: widget.tooltip,
      child: MouseRegion(
        onEnter: (_) => setState(() => _hovered = true),
        onExit: (_) => setState(() => _hovered = false),
        cursor: enabled ? SystemMouseCursors.click : MouseCursor.defer,
        child: GestureDetector(
          onTap: widget.onTap,
          child: AnimatedContainer(
            duration: PrismWaveTheme.fastMotion,
            width: 34,
            height: 34,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: _hovered && enabled
                  ? Colors.white.withValues(alpha: 0.09)
                  : Colors.transparent,
            ),
            child: Icon(
              widget.icon,
              size: 24,
              color: enabled
                  ? PrismWaveTheme.textSecondary
                  : PrismWaveTheme.textMuted.withValues(alpha: 0.45),
            ),
          ),
        ),
      ),
    );
  }
}

class _TopBarLyricBox extends StatelessWidget {
  const _TopBarLyricBox({required this.text, required this.useQuoteTypography});

  final String text;
  final bool useQuoteTypography;

  @override
  Widget build(BuildContext context) {
    final baseStyle = TextStyle(
      color: PrismWaveTheme.textSecondary.withValues(
        alpha: text.isEmpty ? 0 : 0.92,
      ),
      fontSize: 13,
      fontWeight: useQuoteTypography ? FontWeight.w400 : FontWeight.w500,
      height: 1.08,
      leadingDistribution: TextLeadingDistribution.even,
    );

    return IgnorePointer(
      child: Container(
        height: 30,
        padding: const EdgeInsets.symmetric(horizontal: 14),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(PrismWaveTheme.controlRadius),
          gradient: PrismWaveTheme.glassGradient(alpha: 0.18),
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
              if (useQuoteTypography) {
                final slide = Tween<Offset>(
                  begin: const Offset(0, 0.08),
                  end: Offset.zero,
                ).animate(animation);
                return FadeTransition(
                  opacity: animation,
                  child: SlideTransition(position: slide, child: child),
                );
              }

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
                strutStyle: StrutStyle.fromTextStyle(
                  baseStyle,
                  forceStrutHeight: true,
                ),
                style: baseStyle,
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
    final hoverBg = widget.danger
        ? PrismWaveTheme.accent
        : Colors.white.withValues(alpha: 0.09);

    return MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        onTap: widget.onTap,
        behavior: HitTestBehavior.opaque,
        child: SizedBox(
          width: 46,
          height: 44,
          child: Center(
            child: AnimatedContainer(
              duration: const Duration(milliseconds: 160),
              curve: Curves.easeOutCubic,
              width: 36,
              height: 30,
              decoration: BoxDecoration(
                color: _hovered ? hoverBg : Colors.transparent,
                borderRadius: BorderRadius.circular(
                  PrismWaveTheme.controlRadius,
                ),
                border: Border.all(
                  color: _hovered
                      ? Colors.white.withValues(alpha: 0.10)
                      : Colors.transparent,
                ),
              ),
              child: Icon(
                widget.icon,
                size: 18,
                color: PrismWaveTheme.textSecondary,
              ),
            ),
          ),
        ),
      ),
    );
  }
}
