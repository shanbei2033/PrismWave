using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class SettingsMigrationTests
{
    [Fact]
    public void CreateFromMigration_DefaultsOnlineQualityToLossless()
    {
        var migration = new FlutterPreferencesMigrationResult(
            "shared_preferences.json",
            false,
            0,
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?>());

        var settings = SettingsService.CreateFromMigration(migration);

        Assert.Equal(OnlineQualityPreference.Lossless, settings.OnlineQualityPreference);
    }

    [Fact]
    public void CreateFromMigration_ReadsOnlineQualityPreference()
    {
        var migration = new FlutterPreferencesMigrationResult(
            "shared_preferences.json",
            true,
            1,
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?> { ["online.quality"] = "High" });

        var settings = SettingsService.CreateFromMigration(migration);

        Assert.Equal(OnlineQualityPreference.High, settings.OnlineQualityPreference);
    }

    [Fact]
    public void CreateFromMigration_DecodesLyricsAndCoverMaps()
    {
        const string path = @"C:\Music\Song.flac";
        var migration = new FlutterPreferencesMigrationResult(
            "shared_preferences.json",
            true,
            3,
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?>
            {
                ["lyrics.preferredSources"] = "{\"C:\\\\Music\\\\Song.flac\":\"online\"}",
                ["lyrics.offsets"] = "{\"C:\\\\Music\\\\Song.flac\":0.3}",
                ["library.customCoverPaths"] = "{\"C:\\\\Music\\\\Song.flac\":\"C:\\\\Covers\\\\Song.jpg\"}"
            });

        var settings = SettingsService.CreateFromMigration(migration);

        Assert.Equal("online", settings.PreferredLyricsSources![path]);
        Assert.Equal(0.3, settings.LyricsOffsets![path]);
        Assert.Equal(@"C:\Covers\Song.jpg", settings.CustomCoverPaths![path]);
    }
}
