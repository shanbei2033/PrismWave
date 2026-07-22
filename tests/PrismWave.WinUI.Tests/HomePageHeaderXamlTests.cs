using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class HomePageHeaderXamlTests
{
    [Fact]
    public void HomeHeader_UsesIndependentNativeLayoutContract()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Home", "HomePage.xaml"));
        var header = FindByAutomationId(document, "HomePageHeader");
        var refresh = FindByAutomationId(document, "HomeRefreshButton");
        var contentScroll = document.Descendants().Single(element =>
            element.Name.LocalName == "ScrollViewer" &&
            element.Attribute("Grid.Row")?.Value == "1");

        Assert.Equal("0", header.Attribute("Grid.Row")?.Value);
        Assert.Equal("40", header.Attribute("Height")?.Value);
        Assert.Equal("0,0,0,12", header.Attribute("Margin")?.Value);
        Assert.Equal("40", refresh.Attribute("Width")?.Value);
        Assert.Equal("40", refresh.Attribute("Height")?.Value);
        Assert.Equal("{Binding RefreshHomeCommand}", refresh.Attribute("Command")?.Value);
        Assert.Null(refresh.Attribute("Background"));
        Assert.Null(refresh.Attribute("BorderBrush"));
        Assert.Null(refresh.Attribute("BorderThickness"));
        Assert.Equal("1", contentScroll.Attribute("Grid.Row")?.Value);
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
