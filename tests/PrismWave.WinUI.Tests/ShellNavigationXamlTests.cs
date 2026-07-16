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
        Assert.Single(document.Descendants(), element =>
            element.Attribute(xamlName)?.Value == "TransitionFocusTarget" &&
            !host.DescendantsAndSelf().Contains(element));
    }

    [Fact]
    public void Shell_CoverNavigationFrames_DrawOpaquePageBackground()
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
        Assert.All(frames, frame =>
            Assert.Equal("{StaticResource PrismBackgroundBrush}", frame.Attribute("Background")?.Value));
    }

    [Fact]
    public void Shell_HostsFullPlayInAnImmersiveOverlayOutsideNormalNavigation()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml");
        var document = XDocument.Load(xamlPath);
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var overlay = Assert.Single(document.Descendants(), element =>
            element.Attribute(xamlName)?.Value == "FullPlayOverlay");
        var navigation = Assert.Single(document.Descendants(), element =>
            element.Attribute(xamlName)?.Value == "AppNavigationView");
        var playerBar = Assert.Single(document.Descendants(), element =>
            element.Attribute(xamlName)?.Value == "ShellBottomPlayerBar");
        var fullPlayFrame = Assert.Single(overlay.Descendants(), element =>
            element.Name.LocalName == "Frame" &&
            element.Attribute(xamlName)?.Value == "FullPlayFrame");

        Assert.Same(navigation.Parent, overlay.Parent);
        Assert.Same(playerBar.Parent, overlay.Parent);
        Assert.Equal("2", overlay.Attribute("Grid.RowSpan")?.Value);
        Assert.Equal("0", overlay.Attribute("Margin")?.Value);
        Assert.Equal("Collapsed", overlay.Attribute("Visibility")?.Value);
        Assert.Equal("0", overlay.Attribute("Opacity")?.Value);
        Assert.Equal("Transparent", overlay.Attribute("Background")?.Value);
        Assert.Equal("Transparent", fullPlayFrame.Attribute("Background")?.Value);
        Assert.True(overlay.ElementsAfterSelf().Any() is false, "FullPlay overlay must be the topmost shell child.");
    }

    [Fact]
    public void Shell_HandlesFullPlayOutsideTheCoverNavigationCoordinator()
    {
        var codeBehind = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

        Assert.Contains("request.Route == \"FullPlay\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ShowFullPlayOverlay", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HideFullPlayOverlay", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FullPlayFrame.Content = new FullPlayPage()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FullPlayTransitionDurationMilliseconds = 280", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ElementCompositionPreview.SetIsTranslationEnabled(FullPlayOverlay, true)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("visual.StartAnimation(\"Translation.Y\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("visual.Properties.InsertVector3(\"Translation\", Vector3.Zero)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("visual.Offset = new Vector3(0, startOffset", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("visual.Offset = new Vector3(0, endOffset", codeBehind, StringComparison.Ordinal);
        Assert.Contains("slideAnimation.InsertKeyFrame(0f, startOffset", codeBehind, StringComparison.Ordinal);
        Assert.Contains("slideAnimation.InsertKeyFrame(1f, 0f", codeBehind, StringComparison.Ordinal);
        Assert.Contains("slideAnimation.InsertKeyFrame(1f, endOffset", codeBehind, StringComparison.Ordinal);
        Assert.Contains("visual.StartAnimation(\"Opacity\"", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_TitleBarSupportsFullPlayImmersiveMode()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        var codeBehind = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var titleBarBackground = Assert.Single(document.Descendants(), element =>
            element.Attribute(xamlName)?.Value == "TitleBarBackground");
        var titleBar = Assert.Single(document.Descendants(), element =>
            element.Attribute(xamlName)?.Value == "AppTitleBar");

        Assert.Equal(
            "{StaticResource PrismBackgroundBrush}",
            titleBarBackground.Attribute("Background")?.Value);
        Assert.Equal("False", titleBarBackground.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("Transparent", titleBar.Attribute("Background")?.Value);
        Assert.Contains("internal void SetImmersiveTitleBar(bool isImmersive)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TitleBarBackground.Visibility = isImmersive", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Visibility.Collapsed", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Visibility.Visible", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ButtonForegroundColor = Microsoft.UI.Colors.White", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_FullPlayRestoresTitleBarThroughEveryExitPath()
    {
        var codeBehind = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

        Assert.Contains("SetFullPlayImmersiveTitleBar(true)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SetFullPlayImmersiveTitleBar(false)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("App.Window is MainWindow mainWindow", codeBehind, StringComparison.Ordinal);
        Assert.True(
            codeBehind.IndexOf("SetFullPlayImmersiveTitleBar(true)", StringComparison.Ordinal) <
            codeBehind.IndexOf("FullPlayOverlay.Visibility = Visibility.Visible", StringComparison.Ordinal),
            "Immersive mode must be active before the FullPlay overlay becomes visible.");
        Assert.True(
            codeBehind.IndexOf("SetFullPlayImmersiveTitleBar(false)", StringComparison.Ordinal) >
            codeBehind.IndexOf("private void ResetFullPlayOverlay()", StringComparison.Ordinal),
            "The shared reset path must restore the normal title bar.");
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
