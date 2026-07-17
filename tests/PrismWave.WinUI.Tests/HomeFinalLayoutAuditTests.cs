using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class HomeFinalLayoutAuditTests
{
    [Fact]
    public void Shell_SeparatesNavigationContentFromBottomPlayer()
    {
        var source = ReadSource("src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml");
        var document = XDocument.Parse(source);
        var rootGrid = document.Root!.Elements().Single(element => element.Name.LocalName == "Grid");
        var rows = rootGrid.Elements()
            .Single(element => element.Name.LocalName == "Grid.RowDefinitions")
            .Elements()
            .Select(element => element.Attribute("Height")?.Value)
            .ToArray();
        var navigation = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "NavigationView");
        var player = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "BottomPlayerBar");

        Assert.Equal(new[] { "*", "Auto" }, rows);
        Assert.Equal("0", navigation.Attribute("Grid.Row")?.Value ?? "0");
        Assert.Equal("1", player.Attribute("Grid.Row")?.Value);
        Assert.Equal("Left", navigation.Attribute("PaneDisplayMode")?.Value);
        Assert.Equal("True", navigation.Attribute("IsPaneToggleButtonVisible")?.Value);
        Assert.Equal("True", navigation.Attribute("IsSettingsVisible")?.Value);
        Assert.Equal("220", navigation.Attribute("OpenPaneLength")?.Value);
        Assert.Equal("48", navigation.Attribute("CompactPaneLength")?.Value);
    }

    [Fact]
    public void Home_SeparatesHeaderFromVerticalScrollContent()
    {
        var source = ReadSource("src", "PrismWave.WinUI", "Views", "Home", "HomePage.xaml");
        var document = XDocument.Parse(source);
        var rootGrid = document.Root!.Elements().Single(element => element.Name.LocalName == "Grid");
        var rows = rootGrid.Elements()
            .Single(element => element.Name.LocalName == "Grid.RowDefinitions")
            .Elements()
            .Select(element => element.Attribute("Height")?.Value)
            .ToArray();
        var header = FindByAutomationId(document, "HomePageHeader");
        var refresh = FindByAutomationId(document, "HomeRefreshButton");
        var scroll = Assert.Single(rootGrid.Elements(), element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal(new[] { "Auto", "*" }, rows);
        Assert.Equal("0", header.Attribute("Grid.Row")?.Value ?? "0");
        Assert.Equal("0,18,24,12", header.Attribute("Margin")?.Value);
        Assert.Equal("40", refresh.Attribute("Width")?.Value);
        Assert.Equal("40", refresh.Attribute("Height")?.Value);
        Assert.Equal("1", scroll.Attribute("Grid.Row")?.Value);
        Assert.Equal("Auto", scroll.Attribute("VerticalScrollBarVisibility")?.Value);
    }

    [Fact]
    public void Home_KeepsApprovedModuleOrder()
    {
        var source = ReadSource("src", "PrismWave.WinUI", "Views", "Home", "HomePage.xaml");
        var document = XDocument.Parse(source);
        var moduleOrder = document.Descendants()
            .Where(element => element.Name.NamespaceName.Contains("Controls.Home", StringComparison.Ordinal))
            .Select(element => element.Name.LocalName)
            .ToArray();

        Assert.Equal(
            new[] { "TrendingBanner", "TrendingSongList", "EditorialFeature", "GenreExplorer" },
            moduleOrder);
    }

    [Fact]
    public void FinalHomeSurface_HasNoForbiddenOverlayOrRemovedModules()
    {
        var relativeFiles = new[]
        {
            new[] { "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml" },
            new[] { "src", "PrismWave.WinUI", "Views", "Home", "HomePage.xaml" },
            new[] { "src", "PrismWave.WinUI", "Controls", "Home", "TrendingBanner.xaml" },
            new[] { "src", "PrismWave.WinUI", "Controls", "Home", "TrendingSongList.xaml" },
            new[] { "src", "PrismWave.WinUI", "Controls", "Home", "EditorialFeature.xaml" },
            new[] { "src", "PrismWave.WinUI", "Controls", "Home", "GenreExplorer.xaml" },
            new[] { "src", "PrismWave.WinUI", "Controls", "Playback", "BottomPlayerBar.xaml" },
        };

        var sources = relativeFiles.Select(ReadSource).ToArray();
        var source = string.Join(Environment.NewLine, sources);

        Assert.DoesNotContain("<Canvas", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Canvas.ZIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Panel.ZIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("最近播放", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Recent Play", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("私人雷达", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Radar", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex("Width=\\\"[1-9][0-9]{3,}\\\"", RegexOptions.CultureInvariant), source);
        Assert.All(
            sources.SelectMany(item => XDocument.Parse(item).Descendants())
                .SelectMany(element => element.Attributes())
                .Where(attribute => attribute.Name.LocalName == "Margin"),
            margin => Assert.DoesNotMatch(
                new Regex("(^|,)\\s*-", RegexOptions.CultureInvariant),
                margin.Value));
    }

    private static XElement FindByAutomationId(XDocument document, string automationId)
    {
        return Assert.Single(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == automationId);
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
