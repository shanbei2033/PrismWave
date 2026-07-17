using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class ExperiencePolishTests
{
    [Fact]
    public void App_DefinesNoHoverTooltipsAndHidesEveryPageKeyboardHint()
    {
        var document = XDocument.Load(Read("MainWindow.xaml"));
        var root = document.Descendants().Single(element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "WindowRoot");

        Assert.Equal("Hidden", root.Attribute("KeyboardAcceleratorPlacementMode")?.Value);
        foreach (var xaml in Directory.EnumerateFiles(
                     Path.GetDirectoryName(Read("MainWindow.xaml"))!, "*.xaml", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("ToolTipService.ToolTip=", File.ReadAllText(xaml), StringComparison.Ordinal);
        }

        foreach (var code in Directory.EnumerateFiles(
                     Path.GetDirectoryName(Read("MainWindow.xaml"))!, "*.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("ToolTipService.SetToolTip", File.ReadAllText(code), StringComparison.Ordinal);
        }

        foreach (var page in new[]
                 {
                     Read("Views", "Shell", "ShellPage.xaml"),
                     Read("Views", "Hits", "HitsStatusPage.xaml"),
                     Read("Views", "Player", "FullPlayPage.xaml")
                 })
        {
            Assert.Equal("Hidden", XDocument.Load(page).Root?.Attribute("KeyboardAcceleratorPlacementMode")?.Value);
        }
    }

    [Fact]
    public void PlaybackModes_UseOneDynamicSemanticGlyphAcrossEverySurface()
    {
        var viewModel = File.ReadAllText(Read("ViewModels", "Player", "PlaybackViewModel.cs"));
        var bar = File.ReadAllText(Read("Controls", "Playback", "BottomPlayerBar.xaml"));
        var fullPlay = File.ReadAllText(Read("Views", "Player", "FullPlayPage.xaml"));
        var queue = File.ReadAllText(Read("Controls", "Playback", "QueuePane.xaml"));

        Assert.Contains("public string ModeGlyph", viewModel, StringComparison.Ordinal);
        Assert.Contains("PlaybackMode.Single => \"\\uE8ED\"", viewModel, StringComparison.Ordinal);
        Assert.Contains("PlaybackMode.Shuffle => \"\\uE8B1\"", viewModel, StringComparison.Ordinal);
        Assert.Contains("_ => \"\\uE8EE\"", viewModel, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"{Binding ModeGlyph}\"", bar, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"{Binding ModeGlyph}\"", fullPlay, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"{Binding ModeGlyph}\"", queue, StringComparison.Ordinal);
    }

    [Fact]
    public void Queue_IsAFloatingRoundedDrawerWithScrimAndExistingSlideAnimation()
    {
        var shell = XDocument.Load(Read("Views", "Shell", "ShellPage.xaml"));
        var shellCode = File.ReadAllText(Read("Views", "Shell", "ShellPage.xaml.cs"));
        var queue = XDocument.Load(Read("Controls", "Playback", "QueuePane.xaml"));
        var backdrop = FindByName(shell, "QueueBackdrop");
        var surface = FindByName(queue, "QueueSurface");

        Assert.Equal("#52000000", backdrop.Attribute("Background")?.Value);
        Assert.Equal("0,12,12,12", surface.Attribute("Margin")?.Value);
        Assert.Equal("16", surface.Attribute("CornerRadius")?.Value);
        Assert.Equal("1", surface.Attribute("BorderThickness")?.Value);
        Assert.Contains(surface.Descendants(), element => element.Name.LocalName == "ThemeShadow");
        Assert.Contains("Translation.X", shellCode, StringComparison.Ordinal);
        Assert.Contains("QueueOpenTransitionDurationMilliseconds = 240", shellCode, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryFavoriteButton_ProvidesAnUnclippedCenteredVectorViewport()
    {
        var document = XDocument.Load(Read("Views", "Library", "LibraryPage.xaml"));
        var button = document.Descendants().Single(element =>
            element.Attribute("Click")?.Value == "Favorite_Click");
        var icon = button.Descendants().Single(element => element.Name.LocalName == "FontIcon");

        Assert.Equal("40", button.Attribute("Width")?.Value);
        Assert.Equal("40", button.Attribute("Height")?.Value);
        Assert.Equal("0", button.Attribute("Padding")?.Value);
        Assert.Equal("Center", button.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Center", button.Attribute("VerticalContentAlignment")?.Value);
        Assert.Equal("18", icon.Attribute("FontSize")?.Value);
    }

    [Fact]
    public void BetaNavigation_HidesOnlineRoutesAndDefaultsToLibrary()
    {
        var shell = XDocument.Load(Read("Views", "Shell", "ShellPage.xaml"));
        var viewModel = File.ReadAllText(Read("ViewModels", "Shell", "ShellViewModel.cs"));
        foreach (var tag in new[] { "Home", "Search", "Hits" })
        {
            var item = shell.Descendants().Single(element => element.Attribute("Tag")?.Value == tag);
            Assert.Equal(
                "{Binding IsOnlineNavigationVisible, Converter={StaticResource BoolToVisibilityConverter}}",
                item.Attribute("Visibility")?.Value);
        }

        Assert.DoesNotContain(shell.Descendants(), element => element.Attribute("IsSelected")?.Value == "True");
        Assert.Contains("SelectedRoute = IsOnlineNavigationVisible ? \"Home\" : \"Library\"", viewModel, StringComparison.Ordinal);
        Assert.Contains("SelectedRoute is \"Home\" or \"Search\" or \"Hits\"", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsAndSidebar_UseLiveLanguageBindingsInsteadOfHardcodedEnglish()
    {
        var settings = File.ReadAllText(Read("Views", "Settings", "SettingsPage.xaml"));
        var settingsViewModel = File.ReadAllText(Read("ViewModels", "Settings", "SettingsViewModel.cs"));
        var shell = File.ReadAllText(Read("Views", "Shell", "ShellPage.xaml"));
        var shellViewModel = File.ReadAllText(Read("ViewModels", "Shell", "ShellViewModel.cs"));

        Assert.Contains("Text=\"{Binding Text.SettingsTitle}\"", settings, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding Text.BasicTab}\"", settings, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding Text.ExperimentalFeatures}\"", settings, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding Text.OutputMode}\"", settings, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding Text.ScanLogin}\"", settings, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding LibraryFolders.FolderEntries}\"", settings, StringComparison.Ordinal);
        Assert.Contains("OffContent=\"{Binding Text.Off}\"", settings, StringComparison.Ordinal);
        Assert.Contains("public SettingsText Text", settingsViewModel, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(Text))", settingsViewModel, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding HomeLabel}\"", shell, StringComparison.Ordinal);
        Assert.Contains("settingsItem.Content = App.Services.Shell.SettingsLabel", File.ReadAllText(Read("Views", "Shell", "ShellPage.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(HomeLabel))", shellViewModel, StringComparison.Ordinal);

        var folderManager = File.ReadAllText(Read("ViewModels", "Library", "LibraryFolderManagerViewModel.cs"));
        Assert.Contains("ISettingsService? settingsService = null", folderManager, StringComparison.Ordinal);
        Assert.Contains("Localize(\"Ready\"", folderManager, StringComparison.Ordinal);
    }

    private static XElement FindByName(XDocument document, string name) =>
        document.Descendants().Single(element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == name);

    private static string Read(params string[] segments)
    {
        var path = Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PrismWave.WinUI" }
                .Concat(segments)
                .ToArray());
        return Path.GetFullPath(path);
    }
}
