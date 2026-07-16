using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class EditorialFeatureXamlTests
{
    [Fact]
    public void EditorialFeature_UsesUnframedResponsiveMagazineLayout()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Home", "EditorialFeature.xaml"));
        var document = XDocument.Parse(source);
        var root = document.Root!;
        var layoutRoot = FindByName(document, "LayoutRoot");
        var mediaColumn = FindByName(document, "MediaColumn");
        var artwork = FindByAutomationId(document, "EditorialArtwork");
        var artworkHost = FindByAutomationId(document, "EditorialArtworkHost");
        var title = FindByAutomationId(document, "EditorialTitle");
        var trackCount = FindByAutomationId(document, "EditorialTrackCount");
        var featuredTitle = FindByAutomationId(document, "EditorialFeaturedTitle");
        var featuredArtist = FindByAutomationId(document, "EditorialFeaturedArtist");
        var playButton = FindByAutomationId(document, "EditorialPlayButton");
        var visualStateGroups = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "VisualStateManager.VisualStateGroups");

        Assert.Null(root.Attribute("Width"));
        Assert.Equal("EditorialFeature_SizeChanged", root.Attribute("SizeChanged")?.Value);
        Assert.Equal("Grid", layoutRoot.Name.LocalName);
        Assert.Null(layoutRoot.Attribute("Background"));
        Assert.Equal("LayoutRoot", visualStateGroups.Parent?.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value);
        Assert.Equal("300", mediaColumn.Attribute("Width")?.Value);
        Assert.Equal("220", artworkHost.Attribute("Height")?.Value);
        Assert.Equal("{Binding FeaturedCoverUrl, ElementName=EditorialRoot, Converter={StaticResource CoverImageSourceConverter}}",
            artwork.Attribute("Source")?.Value);
        Assert.Equal("EditorialArtwork_ImageFailed", artwork.Attribute("ImageFailed")?.Value);

        Assert.Contains(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "EditorialEyebrow" &&
            element.Attribute("Text")?.Value == "精选");
        AssertBoundText(title, "FeatureTitle");
        AssertBoundText(trackCount, "TrackCountLabel");
        AssertBoundText(featuredTitle, "FeaturedTrackTitle");
        AssertBoundText(featuredArtist, "FeaturedTrackArtist");
        Assert.Equal("{Binding PlayCommand, ElementName=EditorialRoot}", playButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding FeaturedTrack, ElementName=EditorialRoot}", playButton.Attribute("CommandParameter")?.Value);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "EditorialDescription");

        Assert.Contains(document.Descendants(), element => IsSetter(element, "CopyColumn.Width", "0"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "FeatureCopy.(Grid.Row)", "1"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "FeatureCopy.(Grid.Column)", "0"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "MediaColumn.Width", "300"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "FeatureCopy.(Grid.Row)", "0"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "FeatureCopy.(Grid.Column)", "1"));

        Assert.Single(document.Descendants(), element => element.Name.LocalName == "Border");
        Assert.DoesNotContain("BorderThickness=\"1\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ItemsControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<GridView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Canvas", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"-", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GradientBrush", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorialFeature_ProjectsFirstExistingTrackAndResponsiveState()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Home", "EditorialFeature.xaml.cs"));

        Assert.Contains("Section.Tracks.FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("TrackCountLabel", source, StringComparison.Ordinal);
        Assert.Contains("FeatureTitle = \"Play Now\"", source, StringComparison.Ordinal);
        Assert.Contains("TrackCountLabel = \"TOP20\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FeatureDescription", source, StringComparison.Ordinal);
        Assert.Contains("FeaturedTrackTitle", source, StringComparison.Ordinal);
        Assert.Contains("FeaturedTrackArtist", source, StringComparison.Ordinal);
        Assert.Contains("FeaturedCoverUrl", source, StringComparison.Ordinal);
        Assert.Contains("EditorialArtwork_ImageFailed", source, StringComparison.Ordinal);
        Assert.Contains("DistinctBy(track => track.CoverUrl, StringComparer.Ordinal)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyFeaturedCandidate(_featureCandidateIndex)", source, StringComparison.Ordinal);
        Assert.Contains("e.NewSize.Width >= 760", source, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GoToState", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePage_MountsEditorialBetweenRankingAndExplorer()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Home", "HomePage.xaml"));
        var rankingIndex = source.IndexOf("<homeControls:TrendingSongList", StringComparison.Ordinal);
        var editorialIndex = source.IndexOf("<homeControls:EditorialFeature", StringComparison.Ordinal);
        var explorerIndex = source.IndexOf("<homeControls:GenreExplorer", StringComparison.Ordinal);

        Assert.True(rankingIndex >= 0);
        Assert.True(editorialIndex > rankingIndex);
        Assert.True(explorerIndex > editorialIndex);
        Assert.Contains("Section=\"{Binding EditorialSection}\"", source, StringComparison.Ordinal);
        Assert.Contains("PlayCommand=\"{Binding PlayHomeTrackCommand}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeViewModel_ProjectsStreamableNowWithoutChangingCanonicalSections()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "ViewModels", "Home", "HomeViewModel.cs"));

        Assert.Contains("EditorialSection", source, StringComparison.Ordinal);
        Assert.Contains("section.Id.Equals(\"streamable-now\"", source, StringComparison.Ordinal);
        Assert.Contains("EditorialSection = editorial", source, StringComparison.Ordinal);
        Assert.Contains("Sections.Add(section)", source, StringComparison.Ordinal);
    }

    private static void AssertBoundText(XElement element, string propertyName)
    {
        Assert.Equal($"{{Binding {propertyName}, ElementName=EditorialRoot}}", element.Attribute("Text")?.Value);
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
