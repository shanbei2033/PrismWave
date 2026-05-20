import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../i18n/app_strings.dart';
import '../providers.dart';
import 'window_top_bar.dart';

class HitsUnavailablePage extends ConsumerWidget {
  const HitsUnavailablePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final language = ref.watch(appSettingsProvider).language;
    final t = AppStrings(language);

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Stack(
        fit: StackFit.expand,
        children: [
          const DecoratedBox(
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                colors: [
                  Color(0x24090F1D),
                  Color(0x240C1323),
                  Color(0x240E1526),
                ],
              ),
            ),
          ),
          Positioned(
            left: 18,
            top: 46,
            child: IconButton(
              onPressed: () => Navigator.of(context).maybePop(),
              icon: const Icon(Icons.keyboard_arrow_down_rounded),
              tooltip: t.back,
            ),
          ),
          Center(
            child: Text(
              t.hitsUnavailable,
              style: TextStyle(
                fontSize: 34,
                fontWeight: FontWeight.w800,
                color: Colors.white.withValues(alpha: 0.95),
                letterSpacing: 1.2,
              ),
            ),
          ),
          const Positioned(
            left: 0,
            top: 0,
            right: 0,
            child: WindowTopBar(showBrand: false, showLyricBox: false),
          ),
        ],
      ),
    );
  }
}
