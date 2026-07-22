import 'package:flutter/material.dart';

import '../models/app_language.dart';

class PrismWaveTheme {
  const PrismWaveTheme._();

  static const String fontFamilyEn = 'Inter';
  static const String fontFamilyCn = 'Noto Sans SC';
  static const String fontFamilyTw = 'Noto Sans TC';
  static const String fontFamilyRoundedCn = 'Resource Han Rounded CN';
  static const String fontFamilyRoundedTw = 'Resource Han Rounded TW';
  static const List<String> fontFallbackEn = [
    fontFamilyCn,
    fontFamilyTw,
    fontFamilyRoundedCn,
    fontFamilyRoundedTw,
    'SF Pro Text',
    'Segoe UI Variable Text',
    'Segoe UI',
    'Microsoft YaHei UI',
    'Microsoft YaHei',
  ];
  static const List<String> fontFallbackCn = [
    fontFamilyTw,
    fontFamilyRoundedCn,
    fontFamilyRoundedTw,
    'PingFang SC',
    'Microsoft YaHei UI',
    'Microsoft YaHei',
    'Segoe UI Variable Text',
    'Segoe UI',
  ];
  static const List<String> fontFallbackTw = [
    fontFamilyCn,
    fontFamilyRoundedTw,
    fontFamilyRoundedCn,
    'PingFang SC',
    'Segoe UI Variable Text',
    'Segoe UI',
  ];

  static String fontFamilyFor(AppLanguage language) {
    return switch (language) {
      AppLanguage.zhTw || AppLanguage.zhCn || AppLanguage.enUs => fontFamilyEn,
    };
  }

  static List<String> fontFallbackFor(AppLanguage language) {
    return switch (language) {
      AppLanguage.zhTw => fontFallbackTw,
      AppLanguage.zhCn => fontFallbackCn,
      AppLanguage.enUs => fontFallbackEn,
    };
  }

  static const Color appBackgroundTop = Color(0xF4070A10);
  static const Color appBackgroundMid = Color(0xF3090E17);
  static const Color appBackgroundBottom = Color(0xF207080C);
  static const Color surface = Color(0xFF10151D);
  static const Color surfaceElevated = Color(0xFF151B25);
  static const Color surfaceStrong = Color(0xFF1D2532);
  static const Color surfaceInk = Color(0xFF070A0F);
  static const Color surfaceHover = Color(0xFF202838);
  static const Color surfaceSelected = Color(0xFF2E35FF);
  static const Color border = Color(0xFFFFFFFF);
  static const Color borderSoft = Color(0x26FFFFFF);
  static const Color borderMuted = Color(0x14FFFFFF);
  static const Color textPrimary = Color(0xFFF6F6F7);
  static const Color textSecondary = Color(0xFFB9BEC8);
  static const Color textMuted = Color(0xFF7F8794);
  static const Color accent = Color(0xFF625BFF);
  static const Color accentSoft = Color(0xFF8B7DFF);
  static const Color accentDeep = Color(0xFF342BDF);
  static const Color cyanAccent = Color(0xFF68D8FF);
  static const Color danger = Color(0xFFFF6B7A);
  static const Color warning = Color(0xFFFFC86B);

  static const double panelRadius = 18;
  static const double dockRadius = 20;
  static const double controlRadius = 13;
  static const double cardRadius = 14;
  static const double tileRadius = 12;
  static const double sidebarRadius = 0;
  static const double sidebarWidth = 252;
  static const double rightRailWidth = 320;
  static const double playerDockHeight = 108;
  static const double topCommandHeight = 58;
  static const double shellGutter = 26;
  static const double contentMaxWidth = 1180;
  static const double compactBreakpoint = 980;
  static const double wideHomeBreakpoint = 1240;
  static const Duration fastMotion = Duration(milliseconds: 160);
  static const Duration mediumMotion = Duration(milliseconds: 260);

  static const LinearGradient appGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF05070B), Color(0xFF09101A), Color(0xFF040508)],
    stops: [0, 0.54, 1],
  );

  static const LinearGradient accentGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [accentSoft, accent, accentDeep],
    stops: [0, 0.52, 1],
  );

  static const LinearGradient sidebarGradient = LinearGradient(
    begin: Alignment.topCenter,
    end: Alignment.bottomCenter,
    colors: [Color(0xE80A0F16), Color(0xF00B1018), Color(0xF407090D)],
  );

  static const LinearGradient pageFadeGradient = LinearGradient(
    begin: Alignment.topCenter,
    end: Alignment.bottomCenter,
    colors: [Color(0x00000000), Color(0x11000000), Color(0x55000000)],
    stops: [0, 0.68, 1],
  );

  static LinearGradient glassGradient({double alpha = 0.76}) {
    return LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [
        surfaceElevated.withValues(alpha: alpha),
        surface.withValues(alpha: alpha * 0.94),
        surfaceInk.withValues(alpha: alpha * 0.82),
      ],
    );
  }

  static LinearGradient cardGradient({double alpha = 0.62}) {
    return LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [
        Colors.white.withValues(alpha: 0.075 * alpha),
        Colors.white.withValues(alpha: 0.026 * alpha),
      ],
    );
  }

  static LinearGradient dockGradient({double alpha = 0.86}) {
    return LinearGradient(
      begin: Alignment.topCenter,
      end: Alignment.bottomCenter,
      colors: [
        const Color(0xFF151D29).withValues(alpha: alpha),
        const Color(0xFF0B1018).withValues(alpha: alpha),
      ],
    );
  }

  static LinearGradient railGradient({double alpha = 0.70}) {
    return LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [
        const Color(0xFF172030).withValues(alpha: alpha),
        const Color(0xFF0B1018).withValues(alpha: alpha * 0.96),
        const Color(0xFF070A0F).withValues(alpha: alpha * 0.90),
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

  static List<BoxShadow> cardShadow({double alpha = 0.18}) {
    return [
      BoxShadow(
        color: Colors.black.withValues(alpha: alpha),
        blurRadius: 24,
        offset: const Offset(0, 14),
      ),
    ];
  }

  static List<BoxShadow> accentShadow({double alpha = 0.32}) {
    return [
      BoxShadow(
        color: accent.withValues(alpha: alpha),
        blurRadius: 24,
        offset: const Offset(0, 10),
      ),
    ];
  }

  static TextStyle sectionTitleStyle({double fontSize = 19}) {
    return TextStyle(
      color: textPrimary,
      fontSize: fontSize,
      fontWeight: FontWeight.w800,
      height: 1.05,
      letterSpacing: 0,
    );
  }

  static TextStyle captionStyle({double fontSize = 12, double alpha = 0.72}) {
    return TextStyle(
      color: textSecondary.withValues(alpha: alpha),
      fontSize: fontSize,
      fontWeight: FontWeight.w500,
      height: 1.25,
      letterSpacing: 0,
    );
  }

  static TextStyle pageTitleStyle({double fontSize = 28}) {
    return TextStyle(
      color: textPrimary,
      fontSize: fontSize,
      fontWeight: FontWeight.w900,
      height: 1.02,
      letterSpacing: 0,
    );
  }

  static TextStyle mediaTitleStyle({double fontSize = 14}) {
    return TextStyle(
      color: textPrimary,
      fontSize: fontSize,
      fontWeight: FontWeight.w700,
      height: 1.15,
      letterSpacing: 0,
    );
  }

  static BoxDecoration glassDecoration({
    double radius = panelRadius,
    double alpha = 0.72,
    double borderAlpha = 0.10,
    bool selected = false,
    bool withShadow = true,
  }) {
    return BoxDecoration(
      gradient: selected ? accentGradient : glassGradient(alpha: alpha),
      borderRadius: BorderRadius.circular(radius),
      border: Border.all(
        color: selected
            ? Colors.white.withValues(alpha: 0.20)
            : Colors.white.withValues(alpha: borderAlpha),
      ),
      boxShadow: withShadow ? panelShadow(alpha: selected ? 0.18 : 0.10) : null,
    );
  }

  static InputDecoration searchInputDecoration({
    required String hintText,
    Widget? prefixIcon,
    Widget? suffixIcon,
  }) {
    return InputDecoration(
      hintText: hintText,
      prefixIcon: prefixIcon,
      suffixIcon: suffixIcon,
      filled: true,
      fillColor: Colors.white.withValues(alpha: 0.055),
      hintStyle: TextStyle(color: textMuted.withValues(alpha: 0.78)),
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(controlRadius),
        borderSide: BorderSide.none,
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(controlRadius),
        borderSide: BorderSide(color: Colors.white.withValues(alpha: 0.075)),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(controlRadius),
        borderSide: BorderSide(color: accentSoft.withValues(alpha: 0.60)),
      ),
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
    );
  }

  static ButtonStyle rectangularButtonStyle({
    bool selected = false,
    EdgeInsetsGeometry padding = const EdgeInsets.symmetric(
      horizontal: 16,
      vertical: 12,
    ),
  }) {
    final background = selected
        ? accent.withValues(alpha: 0.98)
        : Colors.white.withValues(alpha: 0.045);
    final foreground = selected ? textPrimary : textSecondary;
    final side = BorderSide(
      color: selected
          ? Colors.white.withValues(alpha: 0.18)
          : Colors.white.withValues(alpha: 0.08),
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
              ? accentDeep.withValues(alpha: 1)
              : Colors.white.withValues(alpha: 0.12);
        }
        if (states.contains(WidgetState.hovered)) {
          return selected
              ? accentSoft.withValues(alpha: 0.98)
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
      elevation: const WidgetStatePropertyAll(0),
    );
  }

  static ButtonStyle iconButtonStyle({bool selected = false}) {
    return rectangularButtonStyle(
      selected: selected,
      padding: EdgeInsets.zero,
    ).copyWith(
      minimumSize: const WidgetStatePropertyAll(Size(40, 40)),
      fixedSize: const WidgetStatePropertyAll(Size(40, 40)),
      shape: WidgetStatePropertyAll(
        RoundedRectangleBorder(borderRadius: BorderRadius.circular(999)),
      ),
    );
  }

  static ButtonStyle dangerButtonStyle({
    EdgeInsetsGeometry padding = const EdgeInsets.symmetric(
      horizontal: 16,
      vertical: 12,
    ),
  }) {
    return rectangularButtonStyle(selected: true, padding: padding).copyWith(
      backgroundColor: WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.disabled)) {
          return danger.withValues(alpha: 0.32);
        }
        if (states.contains(WidgetState.pressed)) {
          return danger.withValues(alpha: 0.78);
        }
        if (states.contains(WidgetState.hovered)) {
          return danger.withValues(alpha: 0.94);
        }
        return danger;
      }),
      iconColor: const WidgetStatePropertyAll(textPrimary),
      foregroundColor: const WidgetStatePropertyAll(textPrimary),
    );
  }
}
