using PrismWave_WinUI.Infrastructure;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class WindowLaunchSizeTests
{
    [Theory]
    [InlineData(null, 1600, 900)]
    [InlineData("", 1600, 900)]
    [InlineData("invalid", 1600, 900)]
    [InlineData("640x480", 1600, 900)]
    [InlineData("1280xx720", 1600, 900)]
    [InlineData("x1280x720", 1600, 900)]
    [InlineData("1280x720x", 1600, 900)]
    [InlineData("1280x720", 1280, 720)]
    [InlineData("1440X900", 1440, 900)]
    [InlineData("1920x1080", 1920, 1080)]
    public void Resolve_UsesSupportedOverrideOrDefault(string? value, int width, int height)
    {
        var result = WindowLaunchSize.Resolve(value);

        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
    }

    [Fact]
    public void Launch_ForwardsActivationArgumentsToMainWindow()
    {
        var appSource = ReadSource("src", "PrismWave.WinUI", "App.xaml.cs");
        var windowSource = ReadSource("src", "PrismWave.WinUI", "MainWindow.xaml.cs");

        Assert.Contains("WindowLaunchSize.ResolveLaunch(args.Arguments)", appSource, StringComparison.Ordinal);
        Assert.Contains("new MainWindow(launchSize)", appSource, StringComparison.Ordinal);
        Assert.Contains("public MainWindow(WindowLaunchSize launchSize)", windowSource, StringComparison.Ordinal);
        Assert.Contains("launchSize.Width, launchSize.Height", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEnvironmentVariable", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveLaunch_UsesAndDeletesOneShotOverride()
    {
        var overridePath = Path.Combine(Path.GetTempPath(), $"prismwave-window-{Guid.NewGuid():N}.txt");
        File.WriteAllText(overridePath, "1920x1080");

        var result = WindowLaunchSize.ResolveLaunch(string.Empty, overridePath);

        Assert.Equal(new WindowLaunchSize(1920, 1080), result);
        Assert.False(File.Exists(overridePath));
    }

    private static string ReadSource(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository source file.");
    }
}
