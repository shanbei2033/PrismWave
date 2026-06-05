import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/app_language.dart';
import '../models/release_update_status.dart';
import '../models/top_bar_idle_mode.dart';
import '../services/quote_service.dart';
import '../services/release_update_service.dart';
import '../state/app_settings_state.dart';

class AppSettingsController extends StateNotifier<AppSettingsState> {
  AppSettingsController() : super(const AppSettingsState()) {
    Future<void>.microtask(_loadSettings);
  }

  static const String _prefTopBarIdleMode = 'ui.topBarIdleMode';
  static const String _prefTopBarIdleText = 'ui.topBarIdleText';
  static const String _legacyPrefTopBarQuoteText = 'ui.topBarQuoteText';
  static const String _legacyPrefTopBarQuoteDate = 'ui.topBarQuoteDate';
  static const String _prefOnlineModeEnabled = 'online.modeEnabled';

  final QuoteService _quoteService = QuoteService();
  final ReleaseUpdateService _releaseUpdateService = ReleaseUpdateService();

  Future<void> _loadSettings() async {
    final prefs = await SharedPreferences.getInstance();
    final restored = AppLanguage.fromId(prefs.getString(kPrefAppLanguage));
    final idleMode = TopBarIdleMode.fromId(
      prefs.getString(_prefTopBarIdleMode),
    );
    final idleText = prefs.getString(_prefTopBarIdleText) ?? '';
    final quoteText = _readCachedQuoteText(prefs, restored);
    final onlineModeEnabled = prefs.getBool(_prefOnlineModeEnabled) ?? true;
    state = state.copyWith(
      language: restored,
      topBarIdleMode: idleMode,
      topBarIdleText: idleText,
      topBarQuoteText: quoteText,
      onlineModeEnabled: onlineModeEnabled,
    );

    await ensureTopBarQuote(forceRefresh: false);
  }

  Future<void> setOnlineModeEnabled(bool value) async {
    if (value == state.onlineModeEnabled) return;
    state = state.copyWith(onlineModeEnabled: value);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_prefOnlineModeEnabled, value);
  }

  Future<void> setLanguage(AppLanguage language) async {
    if (language == state.language) return;
    final prefs = await SharedPreferences.getInstance();
    final cachedQuote = _readCachedQuoteText(prefs, language);
    state = state.copyWith(
      language: language,
      topBarQuoteText: cachedQuote,
    );
    await prefs.setString(kPrefAppLanguage, language.id);
    if (state.topBarIdleMode == TopBarIdleMode.quote) {
      await ensureTopBarQuote(forceRefresh: false);
    }
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
    final language = state.language;
    final today = _todayKey();
    final cachedDate = _readCachedQuoteDate(prefs, language);
    final cachedText = _readCachedQuoteText(prefs, language);

    if (!forceRefresh && cachedDate == today && cachedText.trim().isNotEmpty) {
      if (cachedText != state.topBarQuoteText) {
        state = state.copyWith(topBarQuoteText: cachedText);
      }
      return;
    }

    final onlineQuote = await _quoteService.fetchQuote(language: language);
    if (onlineQuote == null || onlineQuote.trim().isEmpty) {
      if (cachedText.trim().isNotEmpty && cachedText != state.topBarQuoteText) {
        state = state.copyWith(topBarQuoteText: cachedText);
        return;
      }
      final fallbackQuote = _quoteService.fallbackQuote(language: language);
      state = state.copyWith(topBarQuoteText: fallbackQuote);
      await prefs.setString(_quoteTextKey(language), fallbackQuote);
      await prefs.setString(_quoteDateKey(language), today);
      return;
    }

    state = state.copyWith(topBarQuoteText: onlineQuote);
    await prefs.setString(_quoteTextKey(language), onlineQuote);
    await prefs.setString(_quoteDateKey(language), today);
  }

  Future<void> checkForUpdates() async {
    if (state.releaseUpdateStatus == ReleaseUpdateStatus.checking) return;

    state = state.copyWith(
      releaseUpdateStatus: ReleaseUpdateStatus.checking,
      clearReleaseUpdateError: true,
    );

    try {
      final release = await _releaseUpdateService.fetchLatestRelease();
      final hasUpdate = _releaseUpdateService.isRemoteNewer(
        release.version,
        state.currentVersion,
      );

      state = state.copyWith(
        releaseUpdateStatus: hasUpdate
            ? ReleaseUpdateStatus.updateAvailable
            : ReleaseUpdateStatus.upToDate,
        latestReleaseVersion: release.version,
        latestReleaseUrl: release.releasePageUrl,
        latestInstallerUrl: release.installerUrl,
        clearReleaseUpdateError: true,
      );
    } catch (error) {
      state = state.copyWith(
        releaseUpdateStatus: ReleaseUpdateStatus.failed,
        releaseUpdateError: '$error',
      );
    }
  }

  String _quoteTextKey(AppLanguage language) => 'ui.topBarQuoteText.${language.id}';

  String _quoteDateKey(AppLanguage language) => 'ui.topBarQuoteDate.${language.id}';

  String _readCachedQuoteText(SharedPreferences prefs, AppLanguage language) {
    final scoped = prefs.getString(_quoteTextKey(language)) ?? '';
    if (scoped.trim().isNotEmpty) return scoped;
    return switch (language) {
      AppLanguage.zhCn || AppLanguage.zhTw =>
        prefs.getString(_legacyPrefTopBarQuoteText) ?? '',
      AppLanguage.enUs => '',
    };
  }

  String _readCachedQuoteDate(SharedPreferences prefs, AppLanguage language) {
    final scoped = prefs.getString(_quoteDateKey(language)) ?? '';
    if (scoped.trim().isNotEmpty) return scoped;
    return switch (language) {
      AppLanguage.zhCn || AppLanguage.zhTw =>
        prefs.getString(_legacyPrefTopBarQuoteDate) ?? '',
      AppLanguage.enUs => '',
    };
  }

  String _todayKey() {
    final now = DateTime.now();
    final month = now.month.toString().padLeft(2, '0');
    final day = now.day.toString().padLeft(2, '0');
    return '${now.year}-$month-$day';
  }
}
