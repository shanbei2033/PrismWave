import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/app_language.dart';
import '../models/top_bar_idle_mode.dart';
import '../services/quote_service.dart';
import '../state/app_settings_state.dart';

class AppSettingsController extends StateNotifier<AppSettingsState> {
  AppSettingsController() : super(const AppSettingsState()) {
    Future<void>.microtask(_loadSettings);
  }

  static const String _prefTopBarIdleMode = 'ui.topBarIdleMode';
  static const String _prefTopBarIdleText = 'ui.topBarIdleText';
  static const String _prefTopBarQuoteText = 'ui.topBarQuoteText';
  static const String _prefTopBarQuoteDate = 'ui.topBarQuoteDate';

  final QuoteService _quoteService = QuoteService();

  Future<void> _loadSettings() async {
    final prefs = await SharedPreferences.getInstance();
    final restored = AppLanguage.fromId(prefs.getString(kPrefAppLanguage));
    final idleMode = TopBarIdleMode.fromId(
      prefs.getString(_prefTopBarIdleMode),
    );
    final idleText = prefs.getString(_prefTopBarIdleText) ?? '';
    final quoteText = prefs.getString(_prefTopBarQuoteText) ?? '';
    state = state.copyWith(
      language: restored,
      topBarIdleMode: idleMode,
      topBarIdleText: idleText,
      topBarQuoteText: quoteText,
    );

    await ensureTopBarQuote(forceRefresh: false);
  }

  Future<void> setLanguage(AppLanguage language) async {
    if (language == state.language) return;
    state = state.copyWith(language: language);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(kPrefAppLanguage, language.id);
  }

  Future<void> setTopBarIdleMode(TopBarIdleMode mode) async {
    if (mode == state.topBarIdleMode) return;
    state = state.copyWith(topBarIdleMode: mode);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_prefTopBarIdleMode, mode.id);
    if (mode == TopBarIdleMode.quote) {
      await ensureTopBarQuote(forceRefresh: false);
    }
  }

  Future<void> setTopBarIdleText(String value) async {
    if (value == state.topBarIdleText) return;
    state = state.copyWith(topBarIdleText: value);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_prefTopBarIdleText, value);
  }

  Future<void> ensureTopBarQuote({required bool forceRefresh}) async {
    final prefs = await SharedPreferences.getInstance();
    final today = _todayKey();
    final cachedDate = prefs.getString(_prefTopBarQuoteDate) ?? '';
    final cachedText = prefs.getString(_prefTopBarQuoteText) ?? '';

    if (!forceRefresh && cachedDate == today && cachedText.trim().isNotEmpty) {
      if (cachedText != state.topBarQuoteText) {
        state = state.copyWith(topBarQuoteText: cachedText);
      }
      return;
    }

    final onlineQuote = await _quoteService.fetchQuote();
    if (onlineQuote == null || onlineQuote.trim().isEmpty) {
      if (cachedText.trim().isNotEmpty && cachedText != state.topBarQuoteText) {
        state = state.copyWith(topBarQuoteText: cachedText);
      }
      return;
    }

    state = state.copyWith(topBarQuoteText: onlineQuote);
    await prefs.setString(_prefTopBarQuoteText, onlineQuote);
    await prefs.setString(_prefTopBarQuoteDate, today);
  }

  String _todayKey() {
    final now = DateTime.now();
    final month = now.month.toString().padLeft(2, '0');
    final day = now.day.toString().padLeft(2, '0');
    return '${now.year}-$month-$day';
  }
}
