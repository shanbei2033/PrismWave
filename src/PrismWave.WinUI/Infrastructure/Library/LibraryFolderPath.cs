namespace PrismWave_WinUI.Infrastructure.Library;

public static class LibraryFolderPath
{
    public static string? Normalize(string? path, bool requireExisting = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var normalized = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(normalized);
            if (!string.Equals(root, normalized, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return !requireExisting || Directory.Exists(normalized) ? normalized : null;
        }
        catch
        {
            return null;
        }
    }
}
