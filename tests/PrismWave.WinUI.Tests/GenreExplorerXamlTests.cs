using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class GenreExplorerXamlTests
{
    [Fact]
    public void GenreExplorer_UsesLightweightWrappingNativeCommands()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Home", "GenreExplorer.xaml"));
        var document = XDocument.Parse(source);
        var root = document.Root!;
        var channels = FindByAutomationId(document, "ChannelExplorerRepeater");
        var genres = FindByAutomationId(document, "GenreExplorerRepeater");
        var entryButton = FindByAutomationId(document, "ExplorerEntryButton");
        var title = FindByAutomationId(document, "ExplorerEntryTitle");
        var icon = FindByAutomationId(document, "ExplorerEntryIcon");
        var layouts = document.Descendants().Where(element =>
            element.Name.LocalName == "UniformGridLayout").ToArray();

        Assert.Null(root.Attribute("Width"));
        Assert.Equal("频道", document.Descendants().Single(element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "ChannelExplorerHeading")
            .Attribute("Text")?.Value);
        Assert.Equal("流派探索", document.Descendants().Single(element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "GenreExplorerHeading")
            .Attribute("Text")?.Value);
        Assert.Equal("{Binding ChannelItems, ElementName=ExplorerRoot}", channels.Attribute("ItemsSource")?.Value);
        Assert.Equal("{Binding GenreItems, ElementName=ExplorerRoot}", genres.Attribute("ItemsSource")?.Value);
        Assert.Equal(2, layouts.Length);
        Assert.All(layouts, layout =>
        {
            Assert.Equal("170", layout.Attribute("MinItemWidth")?.Value);
            Assert.Equal("46", layout.Attribute("MinItemHeight")?.Value);
            Assert.Equal("8", layout.Attribute("MinRowSpacing")?.Value);
            Assert.Equal("8", layout.Attribute("MinColumnSpacing")?.Value);
            Assert.Equal("Fill", layout.Attribute("ItemsStretch")?.Value);
        });

        Assert.Equal("46", entryButton.Attribute("Height")?.Value);
        Assert.Equal("Transparent", entryButton.Attribute("Background")?.Value);
        Assert.Equal("0", entryButton.Attribute("BorderThickness")?.Value);
        Assert.Equal("8", entryButton.Attribute("CornerRadius")?.Value);
        Assert.Equal("ExplorerEntry_Click", entryButton.Attribute("Click")?.Value);
        Assert.Equal("{Binding Section}", entryButton.Attribute("Tag")?.Value);
        Assert.Equal("{Binding Title}", title.Attribute("Text")?.Value);
        Assert.Equal("{Binding IconGlyph}", icon.Attribute("Glyph")?.Value);
        Assert.Equal("{StaticResource PrismAccentSoftBrush}", icon.Attribute("Foreground")?.Value);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "ExplorerEntryCount");

        Assert.DoesNotContain("<Border", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Image", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<GridView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Canvas", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"-", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GradientBrush", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GenreExplorer_ObservesBothSectionSourcesAndRaisesSectionNavigation()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Home", "GenreExplorer.xaml.cs"));

        Assert.Contains("ChannelSectionsProperty", source, StringComparison.Ordinal);
        Assert.Contains("GenreSectionsProperty", source, StringComparison.Ordinal);
        Assert.Contains("INotifyCollectionChanged", source, StringComparison.Ordinal);
        Assert.Contains("CollectionChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("CollectionChanged -=", source, StringComparison.Ordinal);
        Assert.Contains("OpenRequested", source, StringComparison.Ordinal);
        Assert.Contains("Section = section", source, StringComparison.Ordinal);
        Assert.Contains("ExplorerEntry_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TrackCountLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FeaturedTrack", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePage_ReplacesGenericCardStripsWithExplorerAfterEditorial()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Home", "HomePage.xaml"));
        var editorialIndex = source.IndexOf("<homeControls:EditorialFeature", StringComparison.Ordinal);
        var explorerIndex = source.IndexOf("<homeControls:GenreExplorer", StringComparison.Ordinal);

        Assert.True(editorialIndex >= 0);
        Assert.True(explorerIndex > editorialIndex);
        Assert.Contains("ChannelSections=\"{Binding ChannelSections}\"", source, StringComparison.Ordinal);
        Assert.Contains("GenreSections=\"{Binding GenreSections}\"", source, StringComparison.Ordinal);
        Assert.Contains("OpenRequested=\"GenreExplorer_OpenRequested\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FollowingSections", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HomeTrackScroller", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<homeControls:SongCard", source, StringComparison.Ordinal);

        var codeBehind = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Home", "HomePage.xaml.cs"));
        Assert.Contains("SelectHomeSectionCommand.Execute(e.Section)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("NavigateCommand.Execute(\"TopPlaylist\")", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeViewModel_ProjectsChannelsAndGenresWithoutChangingCanonicalSections()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "ViewModels", "Home", "HomeViewModel.cs"));

        Assert.Contains("ChannelSections", source, StringComparison.Ordinal);
        Assert.Contains("GenreSections", source, StringComparison.Ordinal);
        Assert.Contains("section.Id.Equals(\"world-charts\"", source, StringComparison.Ordinal);
        Assert.Contains("section.Id.Equals(\"audius-trending\"", source, StringComparison.Ordinal);
        Assert.Contains("section.Id.StartsWith(\"style-\"", source, StringComparison.Ordinal);
        Assert.Contains("Sections.Add(section)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FollowingSections", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplorerNavigation_UsesSelectedSectionOnPlaylistPage()
    {
        var viewModel = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "ViewModels", "Home", "HomeViewModel.cs"));
        var page = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Home", "TopPlaylistPage.xaml"));

        Assert.Contains("SelectedPlaylist", viewModel, StringComparison.Ordinal);
        Assert.Contains("SelectHomeSection", viewModel, StringComparison.Ordinal);
        Assert.Contains("PlaySelectedPlaylist", viewModel, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedPlaylist.Title}", page, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedPlaylist.Tracks}", page, StringComparison.Ordinal);
        Assert.Contains("{Binding PlaySelectedPlaylistCommand}", page, StringComparison.Ordinal);
    }

    private static XElement FindByAutomationId(XDocument document, string automationId)
    {
        return Assert.Single(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == automationId);
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
