using System.Text.Json;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Persistence;

public sealed class FlutterPreferencesMigrationService
{
    private const string FlutterPrefix = "flutter.";

    public FlutterPreferencesMigrationResult Load()
    {
        var source = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "com.prismwave",
            "prismwave_demo",
            "shared_preferences.json");

        if (!File.Exists(source))
        {
            return new FlutterPreferencesMigrationResult(
                source,
                false,
                0,
                DateTimeOffset.Now,
                new Dictionary<string, object?>());
        }

        try
        {
            using var stream = File.OpenRead(source);
            using var document = JsonDocument.Parse(stream);
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var key = property.Name.StartsWith(FlutterPrefix, StringComparison.Ordinal)
                    ? property.Name[FlutterPrefix.Length..]
                    : property.Name;

                if (ShouldMigrate(key))
                {
                    values[key] = ReadElement(property.Value);
                }
            }

            return new FlutterPreferencesMigrationResult(
                source,
                true,
                values.Count,
                DateTimeOffset.Now,
                values);
        }
        catch
        {
            return new FlutterPreferencesMigrationResult(
                source,
                true,
                0,
                DateTimeOffset.Now,
                new Dictionary<string, object?>());
        }
    }

    private static bool ShouldMigrate(string key)
    {
        return key.StartsWith("library.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("ui.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("online.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("audio.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("debug.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("lyrics.", StringComparison.OrdinalIgnoreCase);
    }

    private static object? ReadElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDouble(out var number) => number,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString())
                .ToList(),
            _ => element.ToString()
        };
    }
}
