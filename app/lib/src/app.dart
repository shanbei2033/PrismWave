import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'i18n/app_strings.dart';
import 'providers.dart';
import 'ui/main_page.dart';
import 'ui/prismwave_theme.dart';

class PrismWaveApp extends ConsumerWidget {
  const PrismWaveApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final language = ref.watch(appSettingsProvider).language;
    final t = AppStrings(language);
    final fontFamily = PrismWaveTheme.fontFamilyFor(language);
    final fontFallback = PrismWaveTheme.fontFallbackFor(language);

    final base = ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      fontFamily: fontFamily,
      fontFamilyFallback: fontFallback,
      colorScheme: const ColorScheme.dark(
        primary: PrismWaveTheme.accent,
        secondary: PrismWaveTheme.accentSoft,
        surface: PrismWaveTheme.surface,
      ),
      textTheme: const TextTheme(
        headlineSmall: TextStyle(fontWeight: FontWeight.w600),
        bodyMedium: TextStyle(height: 1.35),
      ).apply(fontFamily: fontFamily, fontFamilyFallback: fontFallback),
      scaffoldBackgroundColor: Colors.transparent,
    );

    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: t.appTitle,
      theme: base,
      home: const PrismWaveHomePage(),
    );
  }
}
