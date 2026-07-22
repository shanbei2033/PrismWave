import 'dart:ui';

import 'package:flutter/material.dart';

import 'prismwave_theme.dart';

class GlassPanel extends StatelessWidget {
  const GlassPanel({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.radius = PrismWaveTheme.panelRadius,
    this.lowEffects = false,
    this.alpha,
    this.borderAlpha,
    this.shadowAlpha,
    this.dock = false,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;
  final bool lowEffects;
  final double? alpha;
  final double? borderAlpha;
  final double? shadowAlpha;
  final bool dock;

  @override
  Widget build(BuildContext context) {
    final blur = lowEffects ? 7.0 : 18.0;
    final effectiveAlpha = alpha ?? (lowEffects ? 0.72 : 0.76);
    final effectiveBorderAlpha = borderAlpha ?? (lowEffects ? 0.10 : 0.13);
    final effectiveShadowAlpha = shadowAlpha ?? (lowEffects ? 0.0 : 0.12);

    return ClipRRect(
      borderRadius: BorderRadius.circular(radius),
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: blur, sigmaY: blur),
        child: Container(
          padding: padding,
          decoration: BoxDecoration(
            gradient: dock
                ? PrismWaveTheme.dockGradient(alpha: effectiveAlpha)
                : PrismWaveTheme.glassGradient(alpha: effectiveAlpha),
            borderRadius: BorderRadius.circular(radius),
            border: Border.all(
              color: Colors.white.withValues(alpha: effectiveBorderAlpha),
            ),
            boxShadow: effectiveShadowAlpha <= 0
                ? null
                : PrismWaveTheme.panelShadow(alpha: effectiveShadowAlpha),
          ),
          child: child,
        ),
      ),
    );
  }
}
