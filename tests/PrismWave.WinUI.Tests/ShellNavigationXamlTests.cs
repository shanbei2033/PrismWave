using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class ShellNavigationXamlTests
{
    [Fact]
    public void Shell_UsesNativeNavigationViewContract()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml"));

        Assert.Contains("<NavigationView", xaml, StringComparison.Ordinal);
        Assert.Contains("PaneDisplayMode=\"Left\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsPaneToggleButtonVisible=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsSettingsVisible=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenPaneLength=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CompactPaneLength=\"48\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemInvoked=\"AppNavigationView_ItemInvoked\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsInvoked=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<navigation:Sidebar", xaml, StringComparison.Ordinal);

        foreach (var route in new[] { "Home", "Search", "Library", "Albums", "Artists", "Favorites", "Hits" })
        {
            Assert.Contains($"Tag=\"{route}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Shell_LocalizesNativeSettingsItem()
    {
        var codeBehind = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

        Assert.Contains("settingsItem.Content = \"设置\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(settingsItem, \"设置\")", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_UsesRadioSvgForHitsOnly()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml");
        var document = XDocument.Load(xamlPath);
        var hitsItem = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "NavigationViewItem" &&
            element.Attribute("Tag")?.Value == "Hits");
        var imageIcon = Assert.Single(hitsItem.Descendants(), element =>
            element.Name.LocalName == "ImageIcon");
        var svgSource = Assert.Single(imageIcon.Descendants(), element =>
            element.Name.LocalName == "SvgImageSource");

        Assert.Equal("20", imageIcon.Attribute("Width")?.Value);
        Assert.Equal("20", imageIcon.Attribute("Height")?.Value);
        Assert.Equal("ms-appx:///Assets/Icons/radio.svg", svgSource.Attribute("UriSource")?.Value);

        var project = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "PrismWave.WinUI.csproj"));
        Assert.Contains("Assets\\Icons\\radio.svg", project, StringComparison.Ordinal);
        var svg = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Assets", "Icons", "radio.svg"));
        Assert.Contains("#F2F2F2", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#000", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shell_UsesAlbumSvgForAlbumsNavigationItem()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml");
        var document = XDocument.Load(xamlPath);
        var albumsItem = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "NavigationViewItem" &&
            element.Attribute("Tag")?.Value == "Albums");
        var imageIcon = Assert.Single(albumsItem.Descendants(), element =>
            element.Name.LocalName == "ImageIcon");
        var svgSource = Assert.Single(imageIcon.Descendants(), element =>
            element.Name.LocalName == "SvgImageSource");

        Assert.Equal("20", imageIcon.Attribute("Width")?.Value);
        Assert.Equal("20", imageIcon.Attribute("Height")?.Value);
        Assert.Equal("ms-appx:///Assets/Icons/album.svg", svgSource.Attribute("UriSource")?.Value);

        var project = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "PrismWave.WinUI.csproj"));
        Assert.Contains(@"Assets\Icons\album.svg", project, StringComparison.Ordinal);
        var svg = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Assets", "Icons", "album.svg"));
        Assert.Contains("viewBox=\"0 0 24 24\"", svg, StringComparison.Ordinal);
        Assert.Contains("circle", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shell_UsesClippedDualFrameCoverNavigationHost()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml"));
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var host = Assert.Single(document.Descendants(), element =>
            element.Attribute(xamlName)?.Value == "PageTransitionHost");
        var frames = host.Descendants()
            .Where(element => element.Name.LocalName == "Frame")
            .ToArray();

        Assert.Equal(2, frames.Length);
        Assert.Contains(frames, frame => frame.Attribute(xamlName)?.Value == "PrimaryContentFrame");
        Assert.Contains(frames, frame => frame.Attribute(xamlName)?.Value == "SecondaryContentFrame");
        Assert.Single(host.Descendants(), element =>
            element.Name.LocalName == "RectangleGeometry" &&
            element.Attribute(xamlName)?.Value == "PageTransitionClip");
        Assert.DoesNotContain(host.Descendants(), element => element.Name.LocalName == "QueuePane");
    }

    [Fact]
    public void Shell_UsesFixedDirectionFullWidthCoverTransition()
    {
        var codeBehind = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

        Assert.Contains("TimeSpan.FromMilliseconds(280)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PageTransitionHost.ActualWidth", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CreateScalarKeyFrameAnimation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("StartAnimation(\"Offset.X\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CompleteActiveTransition(superseded: true)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("transitionRevision != _navigationTransitionRevision", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SuppressNavigationTransitionInfo", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("DoubleAnimation", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimationsEnabled", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ScaleTransform", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("RotateTransform", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_PreparesAndGuardsCoverTransitionFrames()
    {
        var code = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

        var offscreenOffset = code.IndexOf(
            "incomingVisual.Offset = new Vector3((float)PageTransitionHost.ActualWidth, 0, 0);",
            StringComparison.Ordinal);
        var incomingVisible = code.IndexOf(
            "_incomingContentFrame.Visibility = Visibility.Visible;",
            StringComparison.Ordinal);
        var transitionCompletion = code.IndexOf(
            "CompleteActiveTransition(superseded: true);",
            StringComparison.Ordinal);
        var currentTargetCheck = code.LastIndexOf(
            "_currentContentFrame.CurrentSourcePageType == target",
            StringComparison.Ordinal);

        Assert.True(offscreenOffset >= 0 && offscreenOffset < incomingVisible);
        Assert.Contains("Canvas.SetZIndex(_currentContentFrame, 0)", code, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetZIndex(_incomingContentFrame, 1)", code, StringComparison.Ordinal);
        Assert.Contains("_currentContentFrame.IsHitTestVisible = false;", code, StringComparison.Ordinal);
        Assert.Contains("_incomingContentFrame.IsHitTestVisible = false;", code, StringComparison.Ordinal);
        Assert.True(transitionCompletion >= 0 && currentTargetCheck > transitionCompletion);
    }

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
