import 'dart:math' as math;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

class MiddleClickAutoScrollView extends StatefulWidget {
  const MiddleClickAutoScrollView({
    required this.builder,
    super.key,
    this.controller,
    this.enabled = true,
    this.axis = Axis.vertical,
    this.deadZone = 14,
    this.maxSpeed = 2200,
  });

  final Widget Function(BuildContext context, ScrollController controller)
  builder;
  final ScrollController? controller;
  final bool enabled;
  final Axis axis;
  final double deadZone;
  final double maxSpeed;

  @override
  State<MiddleClickAutoScrollView> createState() =>
      _MiddleClickAutoScrollViewState();
}

class _MiddleClickAutoScrollViewState extends State<MiddleClickAutoScrollView>
    with SingleTickerProviderStateMixin {
  ScrollController? _ownedController;
  late final Ticker _ticker;

  bool _active = false;
  Offset? _anchorLocalPosition;
  Offset? _pointerLocalPosition;
  Duration? _lastTick;

  ScrollController get _controller =>
      widget.controller ?? (_ownedController ??= ScrollController());

  @override
  void initState() {
    super.initState();
    _ticker = createTicker(_handleTick);
  }

  @override
  void didUpdateWidget(covariant MiddleClickAutoScrollView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!widget.enabled && _active) {
      _deactivate();
    }
  }

  void _handlePointerDown(PointerDownEvent event) {
    if (!widget.enabled) return;
    if (event.kind != PointerDeviceKind.mouse) return;
    if ((event.buttons & kMiddleMouseButton) == 0) return;

    if (_active) {
      _deactivate();
      return;
    }

    _active = true;
    _anchorLocalPosition = event.localPosition;
    _pointerLocalPosition = event.localPosition;
    _lastTick = null;
    _ticker.start();
    setState(() {});
  }

  void _handlePointerHover(PointerHoverEvent event) {
    if (!_active) return;
    _pointerLocalPosition = event.localPosition;
  }

  void _handlePointerExit(PointerExitEvent event) {
    if (!_active) return;
    _pointerLocalPosition = _anchorLocalPosition;
  }

  void _handleTick(Duration elapsed) {
    if (!_active) return;
    final anchor = _anchorLocalPosition;
    final pointer = _pointerLocalPosition;
    if (anchor == null || pointer == null) return;

    final dtSeconds = _lastTick == null
        ? 0.0
        : (elapsed - _lastTick!).inMicroseconds /
              Duration.microsecondsPerSecond;
    _lastTick = elapsed;
    if (dtSeconds <= 0) return;

    final controller = _controller;
    if (!controller.hasClients) return;

    final delta = switch (widget.axis) {
      Axis.vertical => pointer.dy - anchor.dy,
      Axis.horizontal => pointer.dx - anchor.dx,
    };
    final speed = _resolveSpeed(delta);
    if (speed == 0) return;

    final position = controller.position;
    final nextOffset = math.max(
      position.minScrollExtent,
      math.min(position.maxScrollExtent, position.pixels + (speed * dtSeconds)),
    );

    if ((nextOffset - position.pixels).abs() < 0.1) return;
    controller.jumpTo(nextOffset);
  }

  double _resolveSpeed(double delta) {
    final distance = delta.abs();
    if (distance <= widget.deadZone) return 0;

    final effectiveDistance = distance - widget.deadZone;
    final normalized = effectiveDistance / 180;
    final scaled = math.pow(normalized, 1.2).toDouble();
    final speed = math.min(
      widget.maxSpeed,
      180 + (scaled * 900) + (effectiveDistance * 2.5),
    );
    return delta.isNegative ? -speed : speed;
  }

  void _deactivate() {
    _active = false;
    _anchorLocalPosition = null;
    _pointerLocalPosition = null;
    _lastTick = null;
    _ticker.stop();
    if (mounted) {
      setState(() {});
    }
  }

  @override
  void dispose() {
    _ticker.dispose();
    _ownedController?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final anchor = _anchorLocalPosition;
    final delta = switch ((anchor, _pointerLocalPosition)) {
      (Offset a, Offset p) =>
        widget.axis == Axis.vertical ? p.dy - a.dy : p.dx - a.dx,
      _ => 0.0,
    };

    return MouseRegion(
      cursor: _active ? SystemMouseCursors.allScroll : MouseCursor.defer,
      onExit: _handlePointerExit,
      child: Listener(
        behavior: HitTestBehavior.translucent,
        onPointerDown: _handlePointerDown,
        onPointerHover: _handlePointerHover,
        child: Stack(
          clipBehavior: Clip.none,
          children: [
            widget.builder(context, _controller),
            if (_active && anchor != null)
              Positioned(
                left: anchor.dx - 18,
                top: anchor.dy - 18,
                child: IgnorePointer(
                  child: _AutoScrollIndicator(
                    axis: widget.axis,
                    delta: delta,
                    deadZone: widget.deadZone,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _AutoScrollIndicator extends StatelessWidget {
  const _AutoScrollIndicator({
    required this.axis,
    required this.delta,
    required this.deadZone,
  });

  final Axis axis;
  final double delta;
  final double deadZone;

  @override
  Widget build(BuildContext context) {
    final upActive = axis == Axis.vertical && delta < -deadZone;
    final downActive = axis == Axis.vertical && delta > deadZone;
    final leftActive = axis == Axis.horizontal && delta < -deadZone;
    final rightActive = axis == Axis.horizontal && delta > deadZone;

    return Container(
      width: 36,
      height: 36,
      decoration: BoxDecoration(
        color: const Color(0xFF0E1628).withValues(alpha: 0.92),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: Colors.white.withValues(alpha: 0.12)),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.22),
            blurRadius: 18,
            offset: const Offset(0, 8),
          ),
        ],
      ),
      child: Stack(
        alignment: Alignment.center,
        children: [
          if (axis == Axis.vertical) ...[
            Positioned(
              top: 2,
              child: _IndicatorArrow(
                icon: Icons.keyboard_arrow_up_rounded,
                active: upActive,
              ),
            ),
            Positioned(
              bottom: 2,
              child: _IndicatorArrow(
                icon: Icons.keyboard_arrow_down_rounded,
                active: downActive,
              ),
            ),
          ] else ...[
            Positioned(
              left: 2,
              child: _IndicatorArrow(
                icon: Icons.keyboard_arrow_left_rounded,
                active: leftActive,
              ),
            ),
            Positioned(
              right: 2,
              child: _IndicatorArrow(
                icon: Icons.keyboard_arrow_right_rounded,
                active: rightActive,
              ),
            ),
          ],
          Container(
            width: 8,
            height: 8,
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.88),
              shape: BoxShape.circle,
            ),
          ),
        ],
      ),
    );
  }
}

class _IndicatorArrow extends StatelessWidget {
  const _IndicatorArrow({required this.icon, required this.active});

  final IconData icon;
  final bool active;

  @override
  Widget build(BuildContext context) {
    return AnimatedOpacity(
      opacity: active ? 1 : 0.55,
      duration: const Duration(milliseconds: 120),
      curve: Curves.easeOutCubic,
      child: Icon(
        icon,
        size: 16,
        color: active
            ? const Color(0xFF8BE3FF)
            : Colors.white.withValues(alpha: 0.82),
      ),
    );
  }
}
