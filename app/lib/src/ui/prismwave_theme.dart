import 'package:flutter/material.dart';

import '../models/app_language.dart';

class PrismWaveTheme {
  const PrismWaveTheme._();

  static const String fontFamilyCn = 'Resource Han Rounded CN';
  static const String fontFamilyTw = 'Resource Han Rounded TW';
  static const List<String> fontFallbackCn = [
    fontFamilyTw,
    'Microsoft YaHei UI',
    'Microsoft YaHei',
    'Segoe UI Variable Text',
    'Segoe UI',
  ];
  static const List<String> fontFallbackTw = [
    fontFamilyCn,
    'PingFang SC',
    'Noto Sans CJK SC',
    'Segoe UI Variable Text',
    'Segoe UI',
  ];

  static String fontFamilyFor(AppLanguage language) {
    return switch (language) {
      AppLanguage.zhTw => fontFamilyTw,
      AppLanguage.zhCn || AppLanguage.enUs => fontFamilyCn,
    };
  }

  static List<String> fontFallbackFor(AppLanguage language) {
    return switch (language) {
      AppLanguage.zhTw => fontFallbackTw,
      AppLanguage.zhCn || AppLanguage.enUs => fontFallbackCn,
    };
  }

  static const Color appBackgroundTop = Color(0x14171719);
  static const Color appBackgroundMid = Color(0x12202024);
  static const Color appBackgroundBottom = Color(0x16111113);
  static const Color surface = Color(0xFF1D1D20);
  static const Color surfaceElevated = Color(0xFF26262A);
  static const Color surfaceStrong = Color(0xFF303036);
  static const Color border = Color(0xFFFFFFFF);
  static const Color textPrimary = Color(0xFFF6F6F7);
  static const Color textSecondary = Color(0xFFB7B7BE);
  static const Color textMuted = Color(0xFF82828A);
  static const Color accent = Color(0xFFFA2D48);
  static const Color accentSoft = Color(0xFFFF6478);
  static const Color accentDeep = Color(0xFFC91F37);

  static const double panelRadius = 18;
  static const double controlRadius = 12;
  static const double cardRadius = 14;

  static const LinearGradient appGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [appBackgroundTop, appBackgroundMid, appBackgroundBottom],
    stops: [0, 0.48, 1],
  );

  static LinearGradient glassGradient({double alpha = 0.76}) {
    return LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [
        surfaceElevated.withValues(alpha: alpha),
        surface.withValues(alpha: alpha * 0.92),
      ],
    );
  }

  static List<BoxShadow> panelShadow({double alpha = 0.24}) {
    return [
      BoxShadow(
        color: Colors.black.withValues(alpha: alpha),
        blurRadius: 34,
        offset: const Offset(0, 18),
      ),
    ];
  }

  static ButtonStyle rectangularButtonStyle({
    bool selected = false,
    EdgeInsetsGeometry padding = const EdgeInsets.symmetric(
      horizontal: 16,
      vertical: 12,
    ),
  }) {
    final background = selected
        ? Colors.white.withValues(alpha: 0.12)
        : Colors.white.withValues(alpha: 0.055);
    final foreground = selected ? textPrimary : textSecondary;
    final side = BorderSide(
      color: selected
          ? Colors.white.withValues(alpha: 0.20)
          : Colors.white.withValues(alpha: 0.09),
    );
    return ButtonStyle(
      minimumSize: const WidgetStatePropertyAll(Size(0, 42)),
      padding: WidgetStatePropertyAll(padding),
      backgroundColor: WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.disabled)) {
          return Colors.white.withValues(alpha: 0.035);
        }
        if (states.contains(WidgetState.pressed)) {
          return selected
              ? Colors.white.withValues(alpha: 0.18)
              : Colors.white.withValues(alpha: 0.12);
        }
        if (states.contains(WidgetState.hovered)) {
          return selected
              ? Colors.white.withValues(alpha: 0.16)
              : Colors.white.withValues(alpha: 0.085);
        }
        return background;
      }),
      foregroundColor: WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.disabled)) {
          return textMuted.withValues(alpha: 0.56);
        }
        return foreground;
      }),
      iconColor: WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.disabled)) {
          return textMuted.withValues(alpha: 0.56);
        }
        return selected ? textPrimary : foreground;
      }),
      side: WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.hovered)) {
          return BorderSide(
            color: selected
                ? Colors.white.withValues(alpha: 0.28)
                : Colors.white.withValues(alpha: 0.14),
          );
        }
        return side;
      }),
      shape: WidgetStatePropertyAll(
        RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(controlRadius),
        ),
      ),
      overlayColor: WidgetStatePropertyAll(
        Colors.white.withValues(alpha: 0.08),
      ),
    );
  }
}
