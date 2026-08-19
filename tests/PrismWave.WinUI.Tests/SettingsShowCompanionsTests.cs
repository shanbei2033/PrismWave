using System.Text.RegularExpressions;
using PrismWave_WinUI.Infrastructure.Persistence;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class SettingsShowCompanionsTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"PrismWaveSettingsTests-{Guid.NewGuid():N}");

    public SettingsShowCompanionsTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task SaveAsync_PersistsShowLyricsCompanionsAcrossReloads()
    {
        var path = Path.Combine(_tempDirectory, "settings.json");
        var migration = new FlutterPreferencesMigrationService();
        var service = new SettingsService(migration, path);

        await service.SaveAsync(service.Current with { ShowLyricsCompanions = false });

        var reloaded = new SettingsService(migration, path);
        Assert.False(reloaded.Current.ShowLyricsCompanions);
    }

    [Fact]
    public async Task LegacySettingsWithoutField_DefaultsToShowCompanions()
    {
        var path = Path.Combine(_tempDirectory, "legacy.json");
        var migration = new FlutterPreferencesMigrationService();
        var service = new SettingsService(migration, path);
        Assert.True(service.Current.ShowLyricsCompanions);

        await service.SaveAsync(service.Current with { ShowLyricsCompanions = false });
        var json = await File.ReadAllTextAsync(path);
        var legacy = Regex.Replace(json, @",\s*""showLyricsCompanions""\s*:\s*false", string.Empty);
        Assert.NotEqual(json, legacy);
        await File.WriteAllTextAsync(path, legacy);

        var reloaded = new SettingsService(migration, path);
        Assert.True(reloaded.Current.ShowLyricsCompanions);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
