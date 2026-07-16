namespace PrismWave_WinUI.Infrastructure;

public static class StartupLog
{
    private static readonly object Gate = new();

    public static event EventHandler<string>? LineWritten;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrismWave",
        "WinUI",
        "startup.log");

    public static void Write(string message, Exception? exception = null)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var payload = exception is null
                    ? line + Environment.NewLine
                    : $"{line}{Environment.NewLine}{exception}{Environment.NewLine}";
                File.AppendAllText(FilePath, payload);
            }

            LineWritten?.Invoke(null, line);
        }
        catch
        {
            // Startup diagnostics must never become the reason startup fails.
        }
    }

    public static IReadOnlyList<string> ReadRecent(int limit = 1000)
    {
        try
        {
            lock (Gate)
            {
                return File.Exists(FilePath)
                    ? File.ReadLines(FilePath).Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(Math.Max(1, limit)).ToList()
                    : Array.Empty<string>();
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void Clear()
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, string.Empty);
            }
        }
        catch
        {
        }
    }
}
