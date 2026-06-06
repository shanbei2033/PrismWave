import 'dart:ui';

import 'package:flutter/material.dart';

import 'prismwave_theme.dart';

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
    final alpha = lowEffects ? 0.12 : 0.08;
    final border = lowEffects
        ? Colors.white.withValues(alpha: 0.11)
        : Colors.white.withValues(alpha: 0.16);

    return ClipRRect(
      borderRadius: BorderRadius.circular(radius),
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: blur, sigmaY: blur),
        child: Container(
          padding: padding,
          decoration: BoxDecoration(
            gradient: PrismWaveTheme.glassGradient(alpha: alpha),
            borderRadius: BorderRadius.circular(radius),
            border: Border.all(color: border),
            boxShadow: lowEffects
                ? null
                : PrismWaveTheme.panelShadow(alpha: 0.06),
          ),
          child: child,
        ),
      ),
    );
  }
}
