using System.Text.Json;
using PrismWave_WinUI.Infrastructure.Persistence;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class SettingsServiceConcurrencyTests
{
    [Fact]
    public async Task ConcurrentSaves_PersistNewestCompleteSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PrismWave.SettingsTests.{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(new FlutterPreferencesMigrationService(), settingsPath);
            var older = service.Current with { Language = "en-US", OnlineCacheMaximumBytes = 1_000 };
            var newest = older with { Language = "zh-CN", OnlineCacheMaximumBytes = 5_000 };

            await Task.WhenAll(service.SaveAsync(older), service.SaveAsync(newest));

            var persisted = JsonSerializer.Deserialize<SettingsSnapshot>(
                await File.ReadAllTextAsync(settingsPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(persisted);
            Assert.Equal("zh-CN", persisted.Language);
            Assert.Equal(5_000, persisted.OnlineCacheMaximumBytes);
            Assert.Equal(newest, service.Current);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }
}
