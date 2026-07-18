using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class AppearanceStyleStructureTests
{
    [Fact]
    public void Mica_UsesLightThemeAndOwnTranslucentPalette()
    {
        var source = ReadMainWindow();

        Assert.Contains("style == AppearanceStyleIds.Mica", source, StringComparison.Ordinal);
        Assert.Contains("? ElementTheme.Light", source, StringComparison.Ordinal);
        Assert.Contains("AppearanceStyleIds.Mica => new AppearancePalette", source, StringComparison.Ordinal);
        Assert.Contains("Surface: Color(0xB8, 0xF3, 0xF3, 0xF3)", source, StringComparison.Ordinal);
        Assert.Contains("TextPrimary: Color(0xFF, 0x1B, 0x1B, 0x1F)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidMicaAndAcrylic_ApplyDifferentSurfacePalettes()
    {
        var source = ReadMainWindow();

        Assert.Contains("AppearanceStyleIds.Acrylic => new AppearancePalette", source, StringComparison.Ordinal);
        Assert.Contains("Surface: Color(0xB5, 0x2D, 0x2E, 0x33)", source, StringComparison.Ordinal);
        Assert.Contains("Surface: Color(0xFF, 0x30, 0x31, 0x34)", source, StringComparison.Ordinal);
        Assert.Contains("SetBrushColor(\"PrismGlassBrush\", palette.Glass)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ImmersiveMode_KeepsWhiteSystemButtonsOverArtwork()
    {
        var source = ReadMainWindow();

        Assert.Contains("_isImmersiveTitleBar", source, StringComparison.Ordinal);
        Assert.Contains("ApplyTitleBarColors();", source, StringComparison.Ordinal);
        Assert.Contains("? Microsoft.UI.Colors.White", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DarkTrendingArtwork_KeepsLightTextInMicaMode()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Home", "TrendingBanner.xaml"));

        Assert.Contains("Foreground=\"#FFF6F6F7\"", source, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"#FFB9BEC8\"", source, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"White\"", source, StringComparison.Ordinal);
    }

    private static string ReadMainWindow() => File.ReadAllText(FindRepositoryFile(
        "src", "PrismWave.WinUI", "MainWindow.xaml.cs"));

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository source file.");
    }
}
