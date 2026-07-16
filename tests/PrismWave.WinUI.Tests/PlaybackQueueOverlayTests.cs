using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class PlaybackQueueOverlayTests
{
    [Fact]
    public void QueueOverlay_IsTopLevelAndDoesNotResizeNavigationContent()
    {
        var document = LoadShell();
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var overlay = FindNamed(document, xamlName, "QueueOverlay");
        var navigation = FindNamed(document, xamlName, "AppNavigationView");
        var player = FindNamed(document, xamlName, "ShellBottomPlayerBar");
        var fullPlay = FindNamed(document, xamlName, "FullPlayOverlay");

        Assert.Same(navigation.Parent, overlay.Parent);
        Assert.Same(player.Parent, overlay.Parent);
        Assert.Equal("0", overlay.Attribute("Grid.Row")?.Value);
        Assert.Equal("Collapsed", overlay.Attribute("Visibility")?.Value);
        Assert.Equal("False", overlay.Attribute("IsHitTestVisible")?.Value);
        Assert.DoesNotContain(overlay, navigation.DescendantsAndSelf());
        Assert.Contains(player, overlay.ElementsBeforeSelf());
        Assert.Contains(navigation, overlay.ElementsBeforeSelf());
        Assert.Contains(fullPlay, overlay.ElementsAfterSelf());
        Assert.DoesNotContain("IsQueuePaneOpen", overlay.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void QueueOverlay_HasBackdropAndResponsiveRightPane()
    {
        var document = LoadShell();
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var overlay = FindNamed(document, xamlName, "QueueOverlay");
        var backdrop = FindNamed(document, xamlName, "QueueBackdrop");
        var pane = FindNamed(document, xamlName, "PlaybackQueuePane");

        Assert.Contains(backdrop, overlay.Descendants());
        Assert.Contains(pane, overlay.Descendants());
        Assert.Equal("QueueBackdrop_PointerPressed", backdrop.Attribute("PointerPressed")?.Value);
        Assert.Equal("344", pane.Attribute("Width")?.Value);
        Assert.Equal("Right", pane.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("QueueOverlay_SizeChanged", overlay.Attribute("SizeChanged")?.Value);
    }

    [Fact]
    public void QueueOverlay_UsesRevisionSafeCompositionMotionAndDismissPaths()
    {
        var code = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));
        var shellViewModel = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "ViewModels", "Shell", "ShellViewModel.cs"));

        Assert.Contains("QueueOpenTransitionDurationMilliseconds = 240", code, StringComparison.Ordinal);
        Assert.Contains("QueueCloseTransitionDurationMilliseconds = 210", code, StringComparison.Ordinal);
        Assert.Contains("QueuePaneWidthRatio = 0.85", code, StringComparison.Ordinal);
        Assert.Contains("ElementCompositionPreview.SetIsTranslationEnabled(PlaybackQueuePane, true)", code, StringComparison.Ordinal);
        Assert.Contains("visual.StartAnimation(\"Translation.X\"", code, StringComparison.Ordinal);
        Assert.Contains("visual.StartAnimation(\"Opacity\"", code, StringComparison.Ordinal);
        Assert.Contains("_queueAnimationRevision", code, StringComparison.Ordinal);
        Assert.Contains("ResolveAnimationsEnabled()", code, StringComparison.Ordinal);
        Assert.Contains("QueueEscapeKeyboardAccelerator_Invoked", code, StringComparison.Ordinal);
        Assert.Contains("KeyboardAcceleratorInvokedEventArgs", code, StringComparison.Ordinal);
        Assert.Contains("PlaybackQueuePane.FocusCloseButton()", code, StringComparison.Ordinal);
        Assert.Contains("private void CloseQueuePane()", shellViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueOverlay_ResetEnablesTranslationBeforeStoppingTranslationAnimation()
    {
        var code = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));
        var start = code.IndexOf("private void ResetQueueOverlay()", StringComparison.Ordinal);
        var end = code.IndexOf("private void DetachQueueAnimationBatch()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = code[start..end];

        var enable = method.IndexOf(
            "ElementCompositionPreview.SetIsTranslationEnabled(PlaybackQueuePane, true)",
            StringComparison.Ordinal);
        var stop = method.IndexOf("visual.StopAnimation(\"Translation.X\")", StringComparison.Ordinal);
        Assert.True(enable >= 0, "Reset must enable XAML Translation before using Translation.X.");
        Assert.True(enable < stop, "Translation must be enabled before StopAnimation targets Translation.X.");
    }

    [Fact]
    public void QueueOverlay_UsesPageLevelEscapeKeyboardAccelerator()
    {
        var document = LoadShell();
        var accelerator = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "KeyboardAccelerator" &&
            element.Attribute("Key")?.Value == "Escape");
        var code = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

        Assert.Equal("QueueEscapeKeyboardAccelerator_Invoked", accelerator.Attribute("Invoked")?.Value);
        Assert.Contains("private void QueueEscapeKeyboardAccelerator_Invoked(", code, StringComparison.Ordinal);
        Assert.Contains("App.Services.Shell.IsQueuePaneOpen", code, StringComparison.Ordinal);
        Assert.Contains("App.Services.Shell.CloseQueuePaneCommand.Execute(null);", code, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true;", code, StringComparison.Ordinal);
    }

    private static XDocument LoadShell() => XDocument.Load(FindRepositoryFile(
        "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml"));

    private static XElement FindNamed(XDocument document, XName xamlName, string name) =>
        Assert.Single(document.Descendants(), element => element.Attribute(xamlName)?.Value == name);

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
