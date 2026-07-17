using Xunit;
using System.Xml.Linq;

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

    [Fact]
    public void TitleBar_HasNoBottomDivider()
    {
        var document = XDocument.Parse(Read("Views", "Hits", "HitsStatusPage.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var titleBar = document
            .Descendants()
            .Single(node => (string?)node.Attribute(x + "Name") == "HitsTitleBar");

        Assert.Null(titleBar.Attribute("BorderBrush"));
        Assert.Null(titleBar.Attribute("BorderThickness"));
    }

    [Fact]
    public void PageUnload_EndsHitsSessionAsFallback()
    {
        var page = Read("Views", "Hits", "HitsStatusPage.xaml.cs");
        var unload = SliceMethod(page, "private void HitsStatusPage_Unloaded", "private void ScheduleTimer_Tick");

        Assert.Contains("EndHitsSessionCommand.Execute(null)", unload, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_EndsHitsBeforePlayerBarReturnsAndBeforeRouteIsCleared()
    {
        var shell = Read("Views", "Shell", "ShellPage.xaml.cs");
        var hide = SliceMethod(shell, "private void HideFullPlayOverlay", "private void FullPlayExitBatch_Completed");
        var reset = SliceMethod(shell, "private void ResetFullPlayOverlay", "private static void SetFullPlayImmersiveTitleBar");

        Assert.Contains("EndHitsSessionIfNeeded();", hide, StringComparison.Ordinal);
        Assert.True(
            hide.IndexOf("EndHitsSessionIfNeeded();", StringComparison.Ordinal) <
            hide.IndexOf("ShellBottomPlayerBar.IsHitTestVisible = true", StringComparison.Ordinal));
        Assert.Contains("EndHitsSessionIfNeeded();", reset, StringComparison.Ordinal);
        Assert.True(
            reset.IndexOf("EndHitsSessionIfNeeded();", StringComparison.Ordinal) <
            reset.IndexOf("_immersiveRoute = null", StringComparison.Ordinal));
    }

    [Fact]
    public void Shell_HasRevisionGuardedFallbackWhenCompositionCompletionIsLost()
    {
        var shell = Read("Views", "Shell", "ShellPage.xaml.cs");

        Assert.Contains("ScheduleFullPlayExitFallback", shell, StringComparison.Ordinal);
        Assert.Contains("_fullPlayExitRevision", shell, StringComparison.Ordinal);
        Assert.Contains("revision != _fullPlayExitRevision", shell, StringComparison.Ordinal);
        Assert.Contains("ResetFullPlayOverlay();", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellEscape_ReturnsFromImmersiveOverlayWhenQueueIsClosed()
    {
        var shell = Read("Views", "Shell", "ShellPage.xaml.cs");
        var handler = SliceMethod(
            shell,
            "private void QueueEscapeKeyboardAccelerator_Invoked",
            "private void ProcessNavigationRequest");

        Assert.Contains("else if (_isFullPlayVisible)", handler, StringComparison.Ordinal);
        Assert.Contains("GoBackCommand.Execute(null)", handler, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapeShortcut_RemainsFunctionalWithoutShowingAKeyboardHintPopup()
    {
        var document = XDocument.Parse(Read("Views", "Hits", "HitsStatusPage.xaml"));
        var page = document.Root!;
        var escape = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "KeyboardAccelerator" &&
            element.Attribute("Key")?.Value == "Escape");
        var backButton = Assert.Single(document.Descendants(), element =>
            element.Attribute("Click")?.Value == "BackButton_Click");

        Assert.Equal("Hidden", page.Attribute("KeyboardAcceleratorPlacementMode")?.Value);
        Assert.Equal("BackKeyboardAccelerator_Invoked", escape.Attribute("Invoked")?.Value);
        Assert.NotNull(backButton);
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.InRange(start, 0, source.Length - 1);
        Assert.InRange(end, start + 1, source.Length - 1);
        return source[start..end];
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
