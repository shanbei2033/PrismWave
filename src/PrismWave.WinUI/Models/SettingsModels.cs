namespace PrismWave_WinUI.Models;

public sealed record SettingsSnapshot(
    string Language,
    bool ExperimentalFeaturesEnabled,
    bool OnlineModeEnabled,
    bool LowEffects,
    string AudioOutputMode,
    string AudioOutputDevice,
    string WindowsDsdDevice,
    bool FadeEnabled,
    int FadeDurationMs,
    IReadOnlyList<string> LibraryFolders,
    IReadOnlyList<string> FavoritePaths,
    IReadOnlyList<string> FavoriteOrderPaths,
    IReadOnlyList<string> TrackOrderPaths,
    IReadOnlyList<string> HiddenTrackPaths,
    IReadOnlyList<string> SearchHistory,
    FlutterPreferencesMigrationResult Migration,
    IReadOnlyDictionary<string, string>? PreferredLyricsSources = null,
    IReadOnlyDictionary<string, double>? LyricsOffsets = null,
    IReadOnlyDictionary<string, string>? CustomCoverPaths = null,
    OnlineQualityPreference OnlineQualityPreference = OnlineQualityPreference.Lossless);

public sealed record FlutterPreferencesMigrationResult(
    string SourceFile,
    bool SourceFound,
    int MigratedKeyCount,
    DateTimeOffset MigratedAt,
    IReadOnlyDictionary<string, object?> Values);
