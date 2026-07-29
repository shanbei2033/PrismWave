using System.Text.Json;
using PrismWave_WinUI.Infrastructure.Persistence;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private long _saveRevision;

    public SettingsService(FlutterPreferencesMigrationService migrationService)
        : this(
            migrationService,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrismWave",
                "WinUI",
                "settings.json"))
    {
    }

    internal SettingsService(FlutterPreferencesMigrationService migrationService, string settingsPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);

        var loaded = LoadExisting() ?? CreateFromMigration(migrationService.Load());
        Current = loaded with
        {
            AudioOutputMode = AudioOutputPolicy.NormalizeModeId(loaded.AudioOutputMode),
            AppearanceStyle = AppearanceStyleIds.Normalize(loaded.AppearanceStyle),
            OnlineCacheMaximumBytes = loaded.OnlineCacheMaximumBytes > 0
                ? loaded.OnlineCacheMaximumBytes
                : OnlineAudioCacheDefault.MaximumBytes,
            OnlineCacheDirectory = loaded.OnlineCacheDirectory?.Trim() ?? string.Empty
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        WriteSnapshotAtomically(Current);
    }

    public SettingsSnapshot Current { get; private set; }
    public event EventHandler? SettingsChanged;

    public Task SaveAsync(SettingsSnapshot snapshot)
    {
        long revision;
        lock (_stateSync)
        {
            Current = snapshot;
            revision = ++_saveRevision;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return SaveSnapshotAsync(snapshot, revision);
    }

    private async Task SaveSnapshotAsync(SettingsSnapshot snapshot, long revision)
    {
        await _saveGate.WaitAsync().ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            lock (_stateSync)
            {
                if (revision != _saveRevision)
                {
                    return;
                }
            }

            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(snapshot, JsonOptions)).ConfigureAwait(false);

            lock (_stateSync)
            {
                if (revision != _saveRevision)
                {
                    return;
                }
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDelete(temporaryPath);
            }

            _saveGate.Release();
        }
    }

    private void WriteSnapshotAtomically(SettingsSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        var temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private SettingsSnapshot? LoadExisting()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SettingsSnapshot>(File.ReadAllText(_settingsPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    internal static SettingsSnapshot CreateFromMigration(FlutterPreferencesMigrationResult migration)
    {
        var values = migration.Values;
        var folders = GetStringList(values, "library.folders").ToList();
        var legacyRoot = GetString(values, "library.rootPath", string.Empty);
        if (folders.Count == 0 && !string.IsNullOrWhiteSpace(legacyRoot))
        {
            folders.Add(legacyRoot);
        }

        return new SettingsSnapshot(
            GetString(values, "ui.language", "zh-CN"),
            false, // Experimental features always OFF on first launch
            GetBool(values, "online.modeEnabled", false),
            GetString(values, "audio.outputMode", AudioOutputPolicy.WasapiSharedId),
            GetString(values, "audio.outputDevice", "auto"),
            GetBool(values, "audio.fadeEnabled", true),
            GetInt(values, "audio.fadeDurationMs", 220),
            folders,
            GetStringList(values, "library.favorites"),
            GetStringList(values, "library.favoriteOrder"),
            GetStringList(values, "library.trackOrder"),
            GetStringList(values, "library.hiddenTracks"),
            GetStringList(values, "online.searchHistory"),
            migration,
            GetStringMap(values, "lyrics.preferredSources"),
            GetDoubleMap(values, "lyrics.offsets"),
            GetStringMap(values, "library.customCoverPaths"),
            GetOnlineQuality(values));
    }

    private static OnlineQualityPreference GetOnlineQuality(IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue("online.quality", out var value) || value is null)
        {
            return OnlineQualityPreference.Lossless;
        }

        var text = value is JsonElement { ValueKind: JsonValueKind.String } element
            ? element.GetString()
            : value.ToString();
        return Enum.TryParse<OnlineQualityPreference>(text, ignoreCase: true, out var quality)
            ? quality
            : OnlineQualityPreference.Lossless;
    }

    private static string GetString(IReadOnlyDictionary<string, object?> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : fallback;
    }

    private static bool GetBool(IReadOnlyDictionary<string, object?> values, string key, bool fallback)
    {
        return values.TryGetValue(key, out var value) && value is bool result ? result : fallback;
    }

    private static int GetInt(IReadOnlyDictionary<string, object?> values, string key, int fallback)
    {
        return values.TryGetValue(key, out var value) && value is int result ? result : fallback;
    }

    private static IReadOnlyList<string> GetStringList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return Array.Empty<string>();
        }

        return value switch
        {
            IReadOnlyList<string> list => list,
            IEnumerable<string> sequence => sequence.ToList(),
            _ => Array.Empty<string>()
        };
    }

    private static IReadOnlyDictionary<string, string> GetStringMap(
        IReadOnlyDictionary<string, object?> values,
        string key)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return result;
        }

        if (value is IReadOnlyDictionary<string, string> typed)
        {
            foreach (var entry in typed)
            {
                AddStringEntry(result, entry.Key, entry.Value);
            }

            return result;
        }

        if (value is JsonElement element)
        {
            AddStringEntries(result, element);
            return result;
        }

        if (value is string raw && !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                AddStringEntries(result, document.RootElement);
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, double> GetDoubleMap(
        IReadOnlyDictionary<string, object?> values,
        string key)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return result;
        }

        if (value is IReadOnlyDictionary<string, double> typed)
        {
            foreach (var entry in typed.Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value != 0))
            {
                result[entry.Key] = entry.Value;
            }

            return result;
        }

        if (value is JsonElement element)
        {
            AddDoubleEntries(result, element);
            return result;
        }

        if (value is string raw && !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                AddDoubleEntries(result, document.RootElement);
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    private static void AddStringEntries(IDictionary<string, string> target, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();
            AddStringEntry(target, property.Name, value);
        }
    }

    private static void AddStringEntry(IDictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static void AddDoubleEntries(IDictionary<string, double> target, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var parsed = property.Value.ValueKind switch
            {
                JsonValueKind.Number when property.Value.TryGetDouble(out var number) => number,
                JsonValueKind.String when double.TryParse(property.Value.GetString(), out var number) => number,
                _ => 0
            };
            if (!string.IsNullOrWhiteSpace(property.Name) && parsed != 0)
            {
                target[property.Name] = parsed;
            }
        }
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
