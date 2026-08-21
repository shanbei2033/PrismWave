using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class TrackEditorXamlTests
{
    [Fact]
    public void LibraryContextMenu_ExposesEditMetadataEntry()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Library", "LibraryPage.xaml"));
        var item = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "MenuFlyoutItem" &&
            element.Attribute("Text")?.Value == "Edit metadata...");

        Assert.Equal("EditMetadata_Click", item.Attribute("Click")?.Value);
        Assert.Equal("{Binding}", item.Attribute("Tag")?.Value);
    }

    [Fact]
    public void ShellRouting_RegistersTrackEditorAsNestedRoute()
    {
        var shellCode = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "ViewModels", "Shell", "ShellViewModel.cs"));
        Assert.Contains("\"TrackEditor\"", shellCode, StringComparison.Ordinal);

        var shellPageCode = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));
        Assert.Contains("\"TrackEditor\" => typeof(TrackEditorPage)", shellPageCode, StringComparison.Ordinal);
        Assert.Contains("\"TrackEditor\" => \"Library\"", shellPageCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackEditorPage_ContainsEditorFieldsAndLockBanner()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Library", "TrackEditorPage.xaml"));
        var ids = document.Descendants()
            .Select(element => element.Attribute("AutomationProperties.AutomationId")?.Value)
            .Where(id => id is not null)
            .ToHashSet();

        Assert.Contains("TrackEditorBackButton", ids);
        Assert.Contains("TrackEditorLockedBanner", ids);
        Assert.Contains("TrackEditorCoverPreview", ids);
        Assert.Contains("TrackEditorSearchCoverButton", ids);
        Assert.Contains("TrackEditorResetCoverButton", ids);
        Assert.Contains("TrackEditorTitleInput", ids);
        Assert.Contains("TrackEditorLyricsInput", ids);
        Assert.Contains("TrackEditorSaveButton", ids);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine([directory, .. segments])))
        {
            directory = Path.GetDirectoryName(directory);
        }

        Assert.NotNull(directory);
        return Path.Combine([directory, .. segments]);
    }
}
