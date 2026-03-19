import 'dart:ui';

import 'package:flutter/material.dart';

class GlassPanel extends StatelessWidget {
  const GlassPanel({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.radius = 20,
    this.lowEffects = false,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;
  final bool lowEffects;

  @override
  Widget build(BuildContext context) {
    final blur = lowEffects ? 7.0 : 18.0;
    final alpha = lowEffects ? 0.16 : 0.10;
    final border = lowEffects
        ? Colors.white.withValues(alpha: 0.14)
        : Colors.white.withValues(alpha: 0.22);

    return ClipRRect(
      borderRadius: BorderRadius.circular(radius),
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: blur, sigmaY: blur),
        child: Container(
          padding: padding,
          decoration: BoxDecoration(
            color: const Color(0xFF0B1220).withValues(alpha: alpha),
            borderRadius: BorderRadius.circular(radius),
            border: Border.all(color: border),
            boxShadow: lowEffects
                ? null
                : [
                    BoxShadow(
                      color: Colors.black.withValues(alpha: 0.10),
                      blurRadius: 20,
                      offset: const Offset(0, 8),
                    ),
                  ],
          ),
          child: child,
        ),
      ),
    );
  }
}
