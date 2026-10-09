using System.Globalization;

namespace PrismWave_WinUI.Infrastructure;

public readonly record struct WindowLaunchSize(int Width, int Height)
{
    public static WindowLaunchSize Default => new(1600, 900);

    public static string OneShotOverridePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrismWave",
        "WinUI",
        "window-size.test-override");

    public static WindowLaunchSize Resolve(string? value)
    {
        return TryResolve(value, out var size) ? size : Default;
    }

    public static WindowLaunchSize ResolveLaunch(string? arguments, string? oneShotOverridePath = null)
    {
        if (TryResolve(arguments, out var argumentSize))
        {
            return argumentSize;
        }

        var path = oneShotOverridePath ?? OneShotOverridePath;
        try
        {
            return File.Exists(path) ? Resolve(File.ReadAllText(path)) : Default;
        }
        catch
        {
            return Default;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private static bool TryResolve(string? value, out WindowLaunchSize size)
    {
        size = Default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(new[] { 'x', 'X' }, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height) ||
            width is < 980 or > 3840 ||
            height is < 620 or > 2160)
        {
            return false;
        }

        size = new WindowLaunchSize(width, height);
        return true;
    }
}
