using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class TrendingSongListXamlTests
{
    [Fact]
    public void GlobalTrending_UsesResponsiveNativeRankingListContract()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Home", "TrendingSongList.xaml"));
        var document = XDocument.Parse(source);
        var root = document.Root!;
        var layoutRoot = FindByName(document, "LayoutRoot");
        var leftList = FindByAutomationId(document, "GlobalTrendingLeftList");
        var rightList = FindByAutomationId(document, "GlobalTrendingRightList");
        var cover = FindByAutomationId(document, "GlobalTrendingCover");
        var coverHost = cover.Ancestors().First(element => element.Name.LocalName == "Border");
        var title = FindByAutomationId(document, "GlobalTrendingTitle");
        var artist = FindByAutomationId(document, "GlobalTrendingArtist");
        var duration = FindByAutomationId(document, "GlobalTrendingDuration");
        var moreButton = FindByAutomationId(document, "GlobalTrendingMoreButton");
        var visualStateGroups = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "VisualStateManager.VisualStateGroups");

        Assert.Null(root.Attribute("Width"));
        Assert.Equal("TrendingSongList_SizeChanged", root.Attribute("SizeChanged")?.Value);
        Assert.Equal("LayoutRoot", layoutRoot.Attributes().Single(attribute =>
            attribute.Name.LocalName == "Name").Value);
        Assert.Equal("LayoutRoot", visualStateGroups.Parent?.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value);
        Assert.Equal("全球热门", document.Descendants().Single(element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "GlobalTrendingHeading")
            .Attribute("Text")?.Value);

        AssertListContract(leftList);
        AssertListContract(rightList);
        Assert.Equal("48", coverHost.Attribute("Width")?.Value);
        Assert.Equal("48", coverHost.Attribute("Height")?.Value);
        AssertEllipsized(title);
        AssertEllipsized(artist);
        Assert.Equal("{Binding Duration}", duration.Attribute("Text")?.Value);
        Assert.Equal("32", moreButton.Attribute("Width")?.Value);
        Assert.Equal("32", moreButton.Attribute("Height")?.Value);

        Assert.Contains(document.Descendants(), element => IsSetter(element, "RightList.(Grid.Row)", "1"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "RightList.(Grid.Column)", "0"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "RightList.(Grid.Row)", "0"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "RightList.(Grid.Column)", "1"));
        Assert.DoesNotContain("<homeControls:SongCard", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<GridView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Canvas", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"-", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalScrollMode=\"Enabled\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePage_MountsRankingListBeforeEditorialAndExplorer()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Home", "HomePage.xaml"));
        var rankingIndex = source.IndexOf("<homeControls:TrendingSongList", StringComparison.Ordinal);
        var editorialIndex = source.IndexOf("<homeControls:EditorialFeature", StringComparison.Ordinal);
        var explorerIndex = source.IndexOf("<homeControls:GenreExplorer", StringComparison.Ordinal);

        Assert.True(rankingIndex >= 0);
        Assert.True(editorialIndex > rankingIndex);
        Assert.True(explorerIndex > editorialIndex);
        Assert.Contains("ItemsSource=\"{Binding GlobalTrendingTracks}\"", source, StringComparison.Ordinal);
        Assert.Contains("PlayCommand=\"{Binding PlayHomeTrackCommand}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding Sections}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeViewModel_ProjectsGlobalHotWithoutChangingCanonicalSections()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "ViewModels", "Home", "HomeViewModel.cs"));

        Assert.Contains("GlobalTrendingTracks", source, StringComparison.Ordinal);
        Assert.Contains("section.Id.Equals(\"global-hot\"", source, StringComparison.Ordinal);
        Assert.Contains("GlobalTrendingTracks.Clear()", source, StringComparison.Ordinal);
        Assert.Contains("Sections.Add(section)", source, StringComparison.Ordinal);
    }

    private static void AssertListContract(XElement list)
    {
        Assert.Equal("None", list.Attribute("SelectionMode")?.Value);
        Assert.Equal("True", list.Attribute("IsItemClickEnabled")?.Value);
        Assert.Equal("Disabled", list.Attribute("ScrollViewer.VerticalScrollMode")?.Value);
        Assert.Equal("Disabled", list.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", list.Attribute("ScrollViewer.HorizontalScrollMode")?.Value);
        Assert.Equal("Disabled", list.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value);
    }

    private static void AssertEllipsized(XElement element)
    {
        Assert.Equal("CharacterEllipsis", element.Attribute("TextTrimming")?.Value);
        Assert.Equal("NoWrap", element.Attribute("TextWrapping")?.Value);
    }

    private static bool IsSetter(XElement element, string target, string value)
    {
        return element.Name.LocalName == "Setter" &&
               element.Attribute("Target")?.Value == target &&
               element.Attribute("Value")?.Value == value;
    }

    private static XElement FindByAutomationId(XDocument document, string automationId)
    {
        return Assert.Single(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == automationId);
    }

    private static XElement FindByName(XDocument document, string name)
    {
        return Assert.Single(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == name));
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
