import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/app_language.dart';
import '../state/app_settings_state.dart';

class AppSettingsController extends StateNotifier<AppSettingsState> {
  AppSettingsController() : super(const AppSettingsState()) {
    Future<void>.microtask(_loadLanguage);
  }

  Future<void> _loadLanguage() async {
    final prefs = await SharedPreferences.getInstance();
    final restored = AppLanguage.fromId(prefs.getString(kPrefAppLanguage));
    if (restored == state.language) return;
    state = state.copyWith(language: restored);
  }

  Future<void> setLanguage(AppLanguage language) async {
    if (language == state.language) return;
    state = state.copyWith(language: language);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(kPrefAppLanguage, language.id);
  }
}
