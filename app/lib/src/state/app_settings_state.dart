import '../models/app_language.dart';
import '../models/release_update_status.dart';
import '../models/top_bar_idle_mode.dart';
import '../services/release_update_service.dart';

class AppSettingsState {
  const AppSettingsState({
    this.language = AppLanguage.zhCn,
    this.topBarIdleMode = TopBarIdleMode.empty,
    this.topBarIdleText = '',
    this.topBarQuoteText = '',
    this.currentVersion = kCurrentReleaseVersion,
    this.releaseUpdateStatus = ReleaseUpdateStatus.idle,
    this.latestReleaseVersion = '',
    this.latestReleaseUrl = '',
    this.latestInstallerUrl = '',
    this.releaseUpdateError = '',
    this.experimentalFeaturesEnabled = false,
    this.onlineModeEnabled = true,
  });

  final AppLanguage language;
  final TopBarIdleMode topBarIdleMode;
  final String topBarIdleText;
  final String topBarQuoteText;
  final String currentVersion;
  final ReleaseUpdateStatus releaseUpdateStatus;
  final String latestReleaseVersion;
  final String latestReleaseUrl;
  final String latestInstallerUrl;
  final String releaseUpdateError;
  final bool experimentalFeaturesEnabled;
  final bool onlineModeEnabled;

  AppSettingsState copyWith({
    AppLanguage? language,
    TopBarIdleMode? topBarIdleMode,
    String? topBarIdleText,
    String? topBarQuoteText,
    String? currentVersion,
    ReleaseUpdateStatus? releaseUpdateStatus,
    String? latestReleaseVersion,
    String? latestReleaseUrl,
    String? latestInstallerUrl,
    String? releaseUpdateError,
    bool clearReleaseUpdateError = false,
    bool? experimentalFeaturesEnabled,
    bool? onlineModeEnabled,
  }) {
    return AppSettingsState(
      language: language ?? this.language,
      topBarIdleMode: topBarIdleMode ?? this.topBarIdleMode,
      topBarIdleText: topBarIdleText ?? this.topBarIdleText,
      topBarQuoteText: topBarQuoteText ?? this.topBarQuoteText,
      currentVersion: currentVersion ?? this.currentVersion,
      releaseUpdateStatus: releaseUpdateStatus ?? this.releaseUpdateStatus,
      latestReleaseVersion: latestReleaseVersion ?? this.latestReleaseVersion,
      latestReleaseUrl: latestReleaseUrl ?? this.latestReleaseUrl,
      latestInstallerUrl: latestInstallerUrl ?? this.latestInstallerUrl,
      releaseUpdateError: clearReleaseUpdateError
          ? ''
          : (releaseUpdateError ?? this.releaseUpdateError),
      experimentalFeaturesEnabled:
          experimentalFeaturesEnabled ?? this.experimentalFeaturesEnabled,
      onlineModeEnabled: onlineModeEnabled ?? this.onlineModeEnabled,
    );
  }
}
