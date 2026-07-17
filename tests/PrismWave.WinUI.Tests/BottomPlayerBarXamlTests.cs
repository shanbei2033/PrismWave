using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class BottomPlayerBarXamlTests
{
    [Fact]
    public void PlayerBar_UsesBalancedRegionsAndSeparateTransportRows()
    {
        var source = ReadSource("src", "PrismWave.WinUI", "Controls", "Playback", "BottomPlayerBar.xaml");
        var document = XDocument.Parse(source);
        var root = document.Root!;
        var playerBorder = FindByAutomationId(document, "BottomPlayerSurface");
        var layout = FindByName(document, "PlayerLayout");
        var center = FindByAutomationId(document, "PlayerCenterRegion");
        var progress = FindByAutomationId(document, "PlayerProgressRow");
        var columns = layout.Elements().Single(element =>
                element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().ToArray();
        var centerRows = center.Elements().Single(element =>
                element.Name.LocalName == "Grid.RowDefinitions")
            .Elements().ToArray();

        Assert.Equal("BottomPlayerBar_SizeChanged", root.Attribute("SizeChanged")?.Value);
        Assert.Equal("{StaticResource PrismPlayerBarHeight}", playerBorder.Attribute("Height")?.Value);
        Assert.Equal("20,11", playerBorder.Attribute("Padding")?.Value);
        Assert.Equal(3, columns.Length);
        Assert.Equal(new[] { "LeftColumn", "CenterColumn", "RightColumn" },
            columns.Select(GetName).ToArray());
        Assert.All(columns, column => Assert.Equal("*", column.Attribute("Width")?.Value));
        Assert.Equal(new[] { "Auto", "10", "Auto" },
            centerRows.Select(row => row.Attribute("Height")?.Value).ToArray());
        Assert.Equal("0", FindByAutomationId(document, "PlayerTransportControls")
            .Attribute("Grid.Row")?.Value ?? "0");
        Assert.Equal("2", progress.Attribute("Grid.Row")?.Value);

        Assert.Equal("{Binding PositionLabel}", FindByAutomationId(document, "PlayerPositionLabel")
            .Attribute("Text")?.Value);
        Assert.Equal("{Binding PositionSeconds, Mode=OneWay}", FindByAutomationId(document, "PlayerSeekSlider")
            .Attribute("Value")?.Value);
        Assert.Equal("{Binding DurationLabel}", FindByAutomationId(document, "PlayerDurationLabel")
            .Attribute("Text")?.Value);

        Assert.DoesNotContain("Canvas", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Canvas.ZIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Panel.ZIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"-", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" Clip=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerBar_UsesOneAccentButtonAndCompleteTrackControls()
    {
        var source = ReadSource("src", "PrismWave.WinUI", "Controls", "Playback", "BottomPlayerBar.xaml");
        var document = XDocument.Parse(source);
        var secondaryStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key" && attribute.Value == "PlayerSecondaryButtonStyle"));
        var mode = FindByAutomationId(document, "PlayerModeButton");
        var previous = FindByAutomationId(document, "PlayerPreviousButton");
        var play = FindByAutomationId(document, "PlayerPlayPauseButton");
        var next = FindByAutomationId(document, "PlayerNextButton");
        var queue = FindByAutomationId(document, "PlayerQueueButton");
        var favorite = FindByAutomationId(document, "PlayerFavoriteButton");
        var title = FindByAutomationId(document, "PlayerTrackTitle");
        var subtitle = FindByAutomationId(document, "PlayerTrackSubtitle");

        Assert.Contains(secondaryStyle.Elements(), element => IsSetter(element, "Background", "Transparent"));
        Assert.Contains(secondaryStyle.Elements(), element => IsSetter(element, "BorderThickness", "0"));
        AssertButtonSize(mode, 36);
        AssertButtonSize(previous, 40);
        AssertButtonSize(play, 52);
        AssertButtonSize(next, 40);
        AssertButtonSize(queue, 36);
        AssertButtonSize(favorite, 36);
        Assert.Equal("{StaticResource PrismAccentBrush}", play.Attribute("Background")?.Value);
        Assert.Equal("0", play.Attribute("BorderThickness")?.Value);
        Assert.Equal("{Binding ToggleCurrentFavoriteCommand}", favorite.Attribute("Command")?.Value);
        Assert.Equal("{Binding CurrentFavoriteGlyph}", FindByAutomationId(document, "PlayerFavoriteIcon")
            .Attribute("Glyph")?.Value);
        Assert.Equal("{Binding CanFavoriteCurrentTrack}", favorite.Attribute("IsEnabled")?.Value);
        Assert.Same(FindByAutomationId(document, "PlayerTransportControls"), favorite.Parent);
        Assert.True(
            favorite.ElementsBeforeSelf().All(element =>
                element.Attribute("AutomationProperties.AutomationId")?.Value != "PlayerModeButton"));
        Assert.Same(favorite, mode.ElementsBeforeSelf().Last());
        Assert.Null(favorite.Attribute("Grid.Column"));
        Assert.Null(favorite.Attribute("Margin"));
        AssertEllipsized(title);
        AssertEllipsized(subtitle);

        var volumeRegion = FindByAutomationId(document, "PlayerVolumeRegion");
        Assert.Equal("Right", volumeRegion.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("{Binding Volume, Mode=TwoWay}", FindByAutomationId(document, "PlayerVolumeSlider")
            .Attribute("Value")?.Value);
    }

    [Fact]
    public void PlayerBar_UsesControlWidthResponsiveStates()
    {
        var xaml = ReadSource("src", "PrismWave.WinUI", "Controls", "Playback", "BottomPlayerBar.xaml");
        var code = ReadSource("src", "PrismWave.WinUI", "Controls", "Playback", "BottomPlayerBar.xaml.cs");
        var document = XDocument.Parse(xaml);
        var playerBorder = FindByAutomationId(document, "BottomPlayerSurface");
        var stateGroups = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "VisualStateManager.VisualStateGroups");
        var stateNames = document.Descendants()
            .Where(element => element.Name.LocalName == "VisualState")
            .Select(GetName)
            .ToArray();
        var compactState = document.Descendants().Single(element =>
            element.Name.LocalName == "VisualState" && GetName(element) == "Compact");
        var mediumState = document.Descendants().Single(element =>
            element.Name.LocalName == "VisualState" && GetName(element) == "Medium");

        Assert.Equal(new[] { "Compact", "Medium", "Wide" }, stateNames);
        Assert.Same(playerBorder, stateGroups.Parent);
        Assert.Contains(mediumState.Descendants(), element => IsSetter(element, "LeftColumn.Width", "*"));
        Assert.Contains(mediumState.Descendants(), element => IsSetter(element, "CenterColumn.Width", "*"));
        Assert.Contains(mediumState.Descendants(), element => IsSetter(element, "RightColumn.Width", "*"));
        Assert.Contains(compactState.Descendants(), element => IsSetter(element, "LeftColumn.Width", "*"));
        Assert.Contains(compactState.Descendants(), element => IsSetter(element, "CenterColumn.Width", "Auto"));
        Assert.Contains(compactState.Descendants(), element => IsSetter(element, "RightColumn.Width", "*"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "FavoriteButton.Visibility", "Collapsed"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "ModeButton.Visibility", "Collapsed"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "QueueButton.Visibility", "Collapsed"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "VolumeSlider.Visibility", "Collapsed"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "LeftColumn.Width", "*"));
        Assert.Contains(document.Descendants(), element => IsSetter(element, "CenterColumn.Width", "Auto"));
        Assert.Contains("e.NewSize.Width >= 1120", code, StringComparison.Ordinal);
        Assert.Contains("e.NewSize.Width >= 760", code, StringComparison.Ordinal);
        Assert.Contains(
            "VisualStateManager.GoToState(this, state, useTransitions: false)",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerBar_OpensFullPlayFromTheWholeTrackInformationRegionOnly()
    {
        var source = ReadSource("src", "PrismWave.WinUI", "Controls", "Playback", "BottomPlayerBar.xaml");
        var document = XDocument.Parse(source);
        var trackButton = FindByAutomationId(document, "PlayerTrackRegion");
        var playButton = FindByAutomationId(document, "PlayerPlayPauseButton");
        var previousButton = FindByAutomationId(document, "PlayerPreviousButton");
        var nextButton = FindByAutomationId(document, "PlayerNextButton");

        Assert.Equal("Button", trackButton.Name.LocalName);
        Assert.Equal("{x:Bind FullPlayCommand, Mode=OneWay}", trackButton.Attribute("Command")?.Value);
        Assert.Contains(trackButton.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "PlayerTrackTitle");
        Assert.Contains(trackButton.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "PlayerTrackSubtitle");
        Assert.Contains(trackButton.Descendants(), element => GetName(element) == "CoverButton");
        Assert.DoesNotContain("FullPlay", playButton.Attribute("Command")?.Value ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("FullPlay", previousButton.Attribute("Command")?.Value ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("FullPlay", nextButton.Attribute("Command")?.Value ?? string.Empty, StringComparison.Ordinal);
    }

    private static void AssertButtonSize(XElement button, int expected)
    {
        Assert.Equal(expected.ToString(), button.Attribute("Width")?.Value);
        Assert.Equal(expected.ToString(), button.Attribute("Height")?.Value);
    }

    private static void AssertEllipsized(XElement textBlock)
    {
        Assert.Equal("CharacterEllipsis", textBlock.Attribute("TextTrimming")?.Value);
        Assert.Equal("NoWrap", textBlock.Attribute("TextWrapping")?.Value);
    }

    private static bool IsSetter(XElement element, string property, string value)
    {
        return element.Name.LocalName == "Setter" &&
               element.Attribute("Property")?.Value == property &&
               element.Attribute("Value")?.Value == value ||
               element.Name.LocalName == "Setter" &&
               element.Attribute("Target")?.Value == property &&
               element.Attribute("Value")?.Value == value;
    }

    private static XElement FindByAutomationId(XDocument document, string automationId)
    {
        return Assert.Single(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == automationId);
    }

    private static XElement FindByName(XDocument document, string name)
    {
        return Assert.Single(document.Descendants(), element => GetName(element) == name);
    }

    private static string GetName(XElement element)
    {
        return element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value
            ?? string.Empty;
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
