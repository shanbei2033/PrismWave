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
    public void SolidAndMica_ApplyDifferentSurfacePalettes()
    {
        var source = ReadMainWindow();

        Assert.Contains("Surface: Color(0xFF, 0x30, 0x31, 0x34)", source, StringComparison.Ordinal);
        Assert.Contains("Surface: Color(0xB8, 0xF3, 0xF3, 0xF3)", source, StringComparison.Ordinal);
        Assert.Contains("SetBrushColor(\"PrismGlassBrush\", palette.Glass)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppearanceStyleIds.Acrylic", source, StringComparison.Ordinal);
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
    public void TrendingBanner_UsesDynamicThemeBrushes()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Home", "TrendingBanner.xaml"));
    
        // Text colors should use dynamic resource brushes, not hardcoded colors
        Assert.Contains("Foreground=\"{StaticResource PrismTextPrimaryBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource PrismTextSecondaryBrush}\"", source, StringComparison.Ordinal);
        // Overlay should use dynamic PrismGlassBrush, not hardcoded acrylic
        Assert.Contains("PrismGlassBrush", source, StringComparison.Ordinal);
        // No hardcoded dark colors that would break light mode
        Assert.DoesNotContain("#FF303135", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#FFF6F6F7", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#FFB9BEC8", source, StringComparison.OrdinalIgnoreCase);
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
