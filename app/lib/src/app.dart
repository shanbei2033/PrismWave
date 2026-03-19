import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'i18n/app_strings.dart';
import 'providers.dart';
import 'ui/main_page.dart';

class PrismWaveApp extends ConsumerWidget {
  const PrismWaveApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final language = ref.watch(appSettingsProvider).language;
    final t = AppStrings(language);

    const accent = Color(0xFF39C0FF);
    const accent2 = Color(0xFF4BE1C3);

    final base = ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      colorScheme: const ColorScheme.dark(
        primary: accent,
        secondary: accent2,
        surface: Color(0xFF0F172A),
      ),
      textTheme: const TextTheme(
        headlineSmall: TextStyle(fontWeight: FontWeight.w600),
        bodyMedium: TextStyle(height: 1.35),
      ),
    );

    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: t.appTitle,
      theme: base,
      home: const PrismWaveHomePage(),
    );
  }
}
