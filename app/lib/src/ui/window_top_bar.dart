import 'package:flutter/material.dart';
import 'package:window_manager/window_manager.dart';

class WindowTopBar extends StatefulWidget {
  const WindowTopBar({
    super.key,
    this.showBrand = true,
  });

  final bool showBrand;

  @override
  State<WindowTopBar> createState() => _WindowTopBarState();
}

class _WindowTopBarState extends State<WindowTopBar> with WindowListener {
  bool _isMaximized = false;

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
    windowManager.removeListener(this);
    super.dispose();
  }

  @override
  void onWindowMaximize() => _syncWindowState();

  @override
  void onWindowUnmaximize() => _syncWindowState();

  @override
  Widget build(BuildContext context) {
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
          ] else
            const SizedBox(width: 4),
          const Expanded(child: DragToMoveArea(child: SizedBox.expand())),
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
