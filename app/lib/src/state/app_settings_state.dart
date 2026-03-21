import '../models/app_language.dart';
import '../models/top_bar_idle_mode.dart';

class AppSettingsState {
  const AppSettingsState({
    this.language = AppLanguage.zhCn,
    this.topBarIdleMode = TopBarIdleMode.empty,
    this.topBarIdleText = '',
    this.topBarQuoteText = '',
  });

  final AppLanguage language;
  final TopBarIdleMode topBarIdleMode;
  final String topBarIdleText;
  final String topBarQuoteText;

  AppSettingsState copyWith({
    AppLanguage? language,
    TopBarIdleMode? topBarIdleMode,
    String? topBarIdleText,
    String? topBarQuoteText,
  }) {
    return AppSettingsState(
      language: language ?? this.language,
      topBarIdleMode: topBarIdleMode ?? this.topBarIdleMode,
      topBarIdleText: topBarIdleText ?? this.topBarIdleText,
      topBarQuoteText: topBarQuoteText ?? this.topBarQuoteText,
    );
  }
}
