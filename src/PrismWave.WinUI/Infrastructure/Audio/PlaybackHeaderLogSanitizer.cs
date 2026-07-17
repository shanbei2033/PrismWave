namespace PrismWave_WinUI.Infrastructure.Audio;

internal static class PlaybackHeaderLogSanitizer
{
    private static readonly string[] SensitiveNameMarkers =
    {
        "authorization",
        "cookie",
        "api-key",
        "apikey",
        "token",
        "secret"
    };

    public static string FormatHeaderNames(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.Count == 0)
        {
            return "<none>";
        }

        return string.Join(
            ',',
            headers.Keys.Select(name => IsSensitive(name) ? "<redacted>" : name));
    }

    private static bool IsSensitive(string name)
    {
        return SensitiveNameMarkers.Any(marker =>
            name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
