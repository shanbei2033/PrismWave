namespace PrismWave_WinUI.Models;

public static class AppearanceStyleIds
{
    public const string Solid = "solid";
    public const string Mica = "mica";
    public const string Acrylic = "acrylic";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Solid => Solid,
        Acrylic => Acrylic,
        _ => Mica
    };
}

public sealed record SettingsSnapshot(
    string Language,
    bool ExperimentalFeaturesEnabled,
    bool OnlineModeEnabled,
    string AudioOutputMode,
    string AudioOutputDevice,
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
    OnlineQualityPreference OnlineQualityPreference = OnlineQualityPreference.Lossless,
    string AppearanceStyle = AppearanceStyleIds.Mica,
    long OnlineCacheMaximumBytes = OnlineAudioCacheDefault.MaximumBytes,
    string OnlineCacheDirectory = "");

public static class OnlineAudioCacheDefault
{
    public const long MaximumBytes = 5L * 1024 * 1024 * 1024;
}

public sealed record FlutterPreferencesMigrationResult(
    string SourceFile,
    bool SourceFound,
    int MigratedKeyCount,
    DateTimeOffset MigratedAt,
    IReadOnlyDictionary<string, object?> Values);
