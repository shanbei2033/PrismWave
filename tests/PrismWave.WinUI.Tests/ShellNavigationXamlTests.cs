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
