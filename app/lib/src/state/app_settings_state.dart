import '../models/app_language.dart';

class AppSettingsState {
  const AppSettingsState({this.language = AppLanguage.zhCn});

  final AppLanguage language;

  AppSettingsState copyWith({AppLanguage? language}) {
    return AppSettingsState(language: language ?? this.language);
  }
}
