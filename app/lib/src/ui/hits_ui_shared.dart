import 'package:flutter/material.dart';

const EdgeInsets kHitsViewportPadding = EdgeInsets.fromLTRB(16, 14, 16, 14);
const double kHitsPanelTopInset = 32;
const EdgeInsets kHitsGlassPanelPadding = EdgeInsets.fromLTRB(24, 20, 24, 24);
const double kHitsHeaderBarExtent = 48;
const double kHitsHeaderGlowWidth = 168;
const double kHitsHeaderGlowHeight = 34;

const TextStyle kHitsHeaderTitleStyle = TextStyle(
  fontSize: 26,
  fontWeight: FontWeight.w800,
  letterSpacing: 7,
);

class HitsHeaderBar extends StatelessWidget {
  const HitsHeaderBar({
    super.key,
    required this.title,
    this.onBack,
    this.interactive = true,
    this.titleOpacity = 1,
    this.glowStrength = 0,
  });

  final String title;
  final VoidCallback? onBack;
  final bool interactive;
  final double titleOpacity;
  final double glowStrength;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        SizedBox(
          width: kHitsHeaderBarExtent,
          height: kHitsHeaderBarExtent,
          child: interactive
              ? IconButton(
                  tooltip: title,
                  onPressed: onBack ?? () => Navigator.of(context).maybePop(),
                  icon: const Icon(Icons.keyboard_arrow_down_rounded),
                )
              : const Center(
                  child: Icon(
                    Icons.keyboard_arrow_down_rounded,
                    color: Color(0xA6FFFFFF),
                  ),
                ),
        ),
        Expanded(
          child: Center(
            child: Stack(
              alignment: Alignment.center,
              children: [
                if (glowStrength > 0)
                  Container(
                    width: kHitsHeaderGlowWidth,
                    height: kHitsHeaderGlowHeight,
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(999),
                      color: Colors.white.withValues(alpha: 0.03),
                      border: Border.all(
                        color: Colors.white.withValues(
                          alpha: 0.06 + (glowStrength * 0.08),
                        ),
                      ),
                      boxShadow: [
                        BoxShadow(
                          color: Colors.white.withValues(
                            alpha: glowStrength * 0.08,
                          ),
                          blurRadius: 16 + (glowStrength * 10),
                        ),
                      ],
                    ),
                  ),
                Opacity(
                  opacity: titleOpacity.clamp(0.0, 1.0),
                  child: Text(
                    title,
                    textAlign: TextAlign.center,
                    style: kHitsHeaderTitleStyle,
                  ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(width: kHitsHeaderBarExtent),
      ],
    );
  }
}
