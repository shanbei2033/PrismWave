using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class PlaybackQueueXamlTests
{
    [Fact]
    public void QueuePane_BindsStableRowsAndRealCovers()
    {
        var document = LoadQueuePane();
        var list = Assert.Single(document.Descendants(), element => element.Name.LocalName == "ListView");

        Assert.Equal("{Binding QueueItems}", list.Attribute("ItemsSource")?.Value);
        Assert.Equal("True", list.Attribute("CanDragItems")?.Value);
        Assert.Equal("True", list.Attribute("CanReorderItems")?.Value);
        Assert.Equal("True", list.Attribute("AllowDrop")?.Value);
        Assert.Equal("Enabled", list.Attribute("ReorderMode")?.Value);
        Assert.Equal("Queue_DragItemsStarting", list.Attribute("DragItemsStarting")?.Value);
        Assert.Equal("Queue_DragItemsCompleted", list.Attribute("DragItemsCompleted")?.Value);

        var cover = Assert.Single(list.Descendants(), element => element.Name.LocalName == "StableCoverImage");
        Assert.Equal("48", cover.Attribute("Width")?.Value);
        Assert.Equal("48", cover.Attribute("Height")?.Value);
        Assert.Equal("{Binding CoverPath}", cover.Attribute("SourcePath")?.Value);
    }

    [Fact]
    public void QueuePane_UsesLocalizedAccessibleHeaderAndModeFooter()
    {
        var document = LoadQueuePane();
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "播放队列");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "{Binding QueueCountLabel}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "{Binding ModeLabel}");

        var close = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Button" && element.Attribute(xamlName)?.Value == "QueueCloseButton");
        Assert.Equal("44", close.Attribute("Width")?.Value);
        Assert.Equal("44", close.Attribute("Height")?.Value);
        Assert.Equal("关闭播放队列", close.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("Close_Click", close.Attribute("Click")?.Value);
    }

    [Fact]
    public void QueuePane_RowsHaveStableFieldsAndCurrentIndicator()
    {
        var document = LoadQueuePane();
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var list = Assert.Single(document.Descendants(), element => element.Name.LocalName == "ListView");

        Assert.Contains(list.Descendants(), element =>
            element.Name.LocalName == "Grid" && element.Attribute("MinHeight")?.Value == "64");
        Assert.Contains(list.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "{Binding PositionLabel}");
        Assert.Contains(list.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "{Binding Title}");
        Assert.Contains(list.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "{Binding Artist}");
        Assert.Contains(list.Descendants(), element =>
            element.Attribute(xamlName)?.Value == "CurrentItemIndicator" &&
            element.Attribute("Visibility")?.Value.Contains("IsCurrent", StringComparison.Ordinal) == true);
        Assert.Contains(list.Descendants(), element =>
            element.Name.LocalName == "Button" && element.Attribute("AutomationProperties.Name")?.Value == "从播放队列移除");
    }

    [Fact]
    public void QueuePane_UsesWinUiDragStartingDelegateSignature()
    {
        var code = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Playback", "QueuePane.xaml.cs"));

        Assert.Contains(
            "private void Queue_DragItemsStarting(object sender, DragItemsStartingEventArgs args)",
            code,
            StringComparison.Ordinal);
    }

    private static XDocument LoadQueuePane() => XDocument.Load(FindRepositoryFile(
        "src", "PrismWave.WinUI", "Controls", "Playback", "QueuePane.xaml"));

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
