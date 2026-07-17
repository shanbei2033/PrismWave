using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class HitsImmersivePageStructureTests
{
    [Fact]
    public void Page_IsAResponsiveMinimalistImmersiveRadioSurface()
    {
        var xaml = Read("Views", "Hits", "HitsStatusPage.xaml");

        Assert.Contains("x:Name=\"HitsTitleBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HitsDragRegion\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CoverPlayPauseButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AdaptiveTrigger MinWindowWidth=\"900\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StableCoverImage", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ListView", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Slider", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LinearGradientBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RadialGradientBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_CentersCoverAndPlacesAllTrackMetadataBelowIt()
    {
        var xaml = Read("Views", "Hits", "HitsStatusPage.xaml");

        Assert.Contains("x:Name=\"CenteredContentStack\"", xaml, StringComparison.Ordinal);
        Assert.True(
            xaml.IndexOf("x:Name=\"CoverColumn\"", StringComparison.Ordinal) <
            xaml.IndexOf("x:Name=\"TrackColumn\"", StringComparison.Ordinal));
        Assert.DoesNotContain("Target=\"CoverColumn.(Grid.Column)\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Target=\"TrackColumn.(Grid.Column)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextAlignment=\"Center\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CoverButton_HasNoPointerHoverOrSelectionChrome()
    {
        var xaml = Read("Views", "Hits", "HitsStatusPage.xaml");
        var source = Read("Views", "Hits", "HitsStatusPage.xaml.cs");

        Assert.Contains("x:Key=\"HitsCoverButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate TargetType=\"Button\">", xaml, StringComparison.Ordinal);
        Assert.Contains("UseSystemFocusVisuals\" Value=\"False\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerEntered=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerExited=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CoverHoverOverlay", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateHoverOverlay", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_UsesCompositionForBackdropTrackAndCoverStateTransitions()
    {
        var source = Read("Views", "Hits", "HitsStatusPage.xaml.cs");

        Assert.Contains("GaussianBlurEffect", source, StringComparison.Ordinal);
        Assert.Contains("LoadBackdrop", source, StringComparison.Ordinal);
        Assert.Contains("AnimateTrackChange", source, StringComparison.Ordinal);
        Assert.Contains("AnimateCoverState", source, StringComparison.Ordinal);
        Assert.Contains("MotionPolicy.ShouldAnimateInteraction", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(270)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_TreatsHitsAsNestedImmersiveOverlayAndRestoresTitleBar()
    {
        var shellViewModel = Read("ViewModels", "Shell", "ShellViewModel.cs");
        var shellPage = Read("Views", "Shell", "ShellPage.xaml.cs");
        var mainWindow = ReadRoot("MainWindow.xaml.cs");

        Assert.Contains("\"Hits\"", shellViewModel, StringComparison.Ordinal);
        Assert.Contains("IsImmersiveRoute", shellPage, StringComparison.Ordinal);
        Assert.Contains("new HitsStatusPage()", shellPage, StringComparison.Ordinal);
        Assert.Contains("UIElement? dragRegion", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SetTitleBar(dragRegion", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SetTitleBar(AppTitleBar)", mainWindow, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments)
    {
        var path = Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PrismWave.WinUI" }
                .Concat(segments)
                .ToArray());
        return File.ReadAllText(Path.GetFullPath(path));
    }

    private static string ReadRoot(string fileName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "PrismWave.WinUI", fileName);
        return File.ReadAllText(Path.GetFullPath(path));
    }
}
