using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class TrendingHeroXamlTests
{
    [Fact]
    public void TrendingHero_UsesImmersiveResponsiveXamlContract()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Home", "TrendingBanner.xaml"));
        var document = XDocument.Parse(source);
        var hero = FindByAutomationId(document, "TrendingHero");
        var backdrop = FindByAutomationId(document, "HeroBackdrop");
        var collage = FindByAutomationId(document, "HeroCoverCollage");
        var visualStateGroups = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "VisualStateManager.VisualStateGroups");

        Assert.Equal("220", hero.Attribute("MinHeight")?.Value);
        Assert.Equal("0", hero.Attribute("BorderThickness")?.Value);
        Assert.Equal("12", hero.Attribute("CornerRadius")?.Value);
        Assert.Equal("220", backdrop.Attribute("Height")?.Value);
        Assert.Null(collage.Attribute("Visibility"));
        Assert.Equal("LayoutRoot", visualStateGroups.Parent?.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value);
        Assert.Contains(document.Descendants(), element => element.Attribute("Text")?.Value == "TOP 100");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "HeroCoverCollage");
        Assert.Equal(4, document.Descendants().Count(element =>
            element.Name.LocalName == "Image" &&
            element.Attribute("AutomationProperties.AutomationId")?.Value?.StartsWith(
                "HeroCover", StringComparison.Ordinal) == true));
        Assert.Equal("BannerRoot_SizeChanged", document.Root?.Attribute("SizeChanged")?.Value);
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter" &&
            element.Attribute("Target")?.Value == "CoverColumn.Width" &&
            element.Attribute("Value")?.Value == "0");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter" &&
            element.Attribute("Target")?.Value == "CoverColumn.Width" &&
            element.Attribute("Value")?.Value == "320");
        Assert.DoesNotContain("<ItemsRepeater", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<UniformGridLayout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<AdaptiveTrigger", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Canvas", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Translation=\"0,-", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"-", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrendingHero_ProjectsAndObservesFourExistingTrackCovers()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Home", "TrendingBanner.xaml.cs"));

        Assert.Contains("CoverOneUrl", source, StringComparison.Ordinal);
        Assert.Contains("CoverTwoUrl", source, StringComparison.Ordinal);
        Assert.Contains("CoverThreeUrl", source, StringComparison.Ordinal);
        Assert.Contains("CoverFourUrl", source, StringComparison.Ordinal);
        Assert.Contains("WeakCollectionChangedListener<TrendingBanner>", source, StringComparison.Ordinal);
        Assert.Contains("_tracksListener.Subscribe(args.NewValue)", source, StringComparison.Ordinal);
        Assert.Contains("_tracksListener.Unsubscribe()", source, StringComparison.Ordinal);
        Assert.Contains("OfType<HomeTrackModel>()", source, StringComparison.Ordinal);
        Assert.Contains("e.NewSize.Width >= 720", source, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GoToState", source, StringComparison.Ordinal);
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
