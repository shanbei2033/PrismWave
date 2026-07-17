using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class LocalLibraryStructureTests
{
    [Fact]
    public void SettingsPage_HasSharedAddRemoveAndRescanControls()
    {
        var document = XDocument.Load(FindFile(
            "src", "PrismWave.WinUI", "Views", "Settings", "SettingsPage.xaml"));

        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Command")?.Value == "{Binding LibraryFolders.AddFolderCommand}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Command")?.Value == "{Binding LibraryFolders.RescanCommand}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Command")?.Value == "{Binding DataContext.LibraryFolders.RemoveFolderCommand, ElementName=SettingsRoot}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("ItemsSource")?.Value == "{Binding LibraryFolders.FolderEntries}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ProgressRing" &&
            element.Attribute("IsActive")?.Value == "{Binding LibraryFolders.IsScanning}");
    }

    [Fact]
    public void LibraryPage_UsesSharedFolderCommandsWithoutCodeBehindPicker()
    {
        var xamlPath = FindFile(
            "src", "PrismWave.WinUI", "Views", "Library", "LibraryPage.xaml");
        var document = XDocument.Load(xamlPath);
        var code = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));

        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Command")?.Value == "{Binding LibraryFolders.AddFolderCommand}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Command")?.Value == "{Binding LibraryFolders.RescanCommand}");
        Assert.DoesNotContain("FolderPicker", code, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeWithWindow", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AddFolder_Click", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPage_UsesSongTableInsteadOfPermanentFolderRail()
    {
        var xamlPath = FindFile(
            "src", "PrismWave.WinUI", "Views", "Library", "LibraryPage.xaml");
        var document = XDocument.Load(xamlPath);
        var code = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "AutoSuggestBox" &&
            element.Attribute("QueryIcon")?.Value == "Find");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "TableHeader");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "TracksList" &&
            element.Attribute("ItemsSource")?.Value == "{Binding VisibleTracks}");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "MetricPill");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("ItemsSource")?.Value == "{Binding LibraryFolders.Folders}");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "ColumnDefinition" && element.Attribute("Width")?.Value == "260");
        Assert.Contains("OpenFolderManager_Click", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPage_KeepsAlbumColumnsAlignedWithoutConditionalLayoutJumps()
    {
        var document = XDocument.Load(FindFile(
            "src", "PrismWave.WinUI", "Views", "Library", "LibraryPage.xaml"));

        Assert.Contains(document.Descendants(), element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "AlbumHeader" &&
            element.Attribute("Grid.Column")?.Value == "3");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "AlbumCell" &&
            element.Attribute("Grid.Column")?.Value == "3");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "HeaderAlbumColumn" &&
            element.Attribute("Width")?.Value == "1.15*");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "AlbumColumn" &&
            element.Attribute("Width")?.Value == "1.15*");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "AdaptiveTrigger");
    }

    [Fact]
    public void FolderDialog_UsesSharedManagerAndShowsTrackCounts()
    {
        var document = XDocument.Load(FindFile(
            "src", "PrismWave.WinUI", "Views", "Dialogs", "LibraryFoldersDialog.xaml"));

        Assert.Contains(document.Descendants(), element =>
            element.Attribute("ItemsSource")?.Value == "{Binding FolderEntries}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Text")?.Value == "{Binding TrackCount}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Text")?.Value == "{Binding StatusText}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Command")?.Value == "{Binding AddFolderCommand}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Command")?.Value == "{Binding RescanCommand}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Command")?.Value == "{Binding DataContext.RemoveFolderCommand, ElementName=FolderDialogRoot}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "StackPanel" &&
            element.Attribute("Grid.Row")?.Value == "1" &&
            element.Attribute("Orientation")?.Value == "Horizontal");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Grid" &&
            element.Attribute("MinWidth")?.Value == "560");
    }

    [Fact]
    public void App_InitializesDispatcherBeforeWindowAndLibraryAfterActivation()
    {
        var code = File.ReadAllText(FindFile("src", "PrismWave.WinUI", "App.xaml.cs"));
        var dispatcher = code.IndexOf("DispatcherQueue =", StringComparison.Ordinal);
        var window = code.IndexOf("Window = new MainWindow", StringComparison.Ordinal);
        var activate = code.IndexOf("Window.Activate()", StringComparison.Ordinal);
        var initialize = code.IndexOf("LibraryService.InitializeAsync", StringComparison.Ordinal);

        Assert.True(dispatcher >= 0 && dispatcher < window);
        Assert.True(activate >= 0 && activate < initialize);
    }

    [Fact]
    public void FolderPicker_UsesOwnerWindowWildcardAndConcurrencyGuard()
    {
        var code = File.ReadAllText(FindFile(
            "src", "PrismWave.WinUI", "Services", "Implementations", "WindowsMusicFolderPicker.cs"));

        Assert.Contains("InitializeWithWindow.Initialize", code, StringComparison.Ordinal);
        Assert.Contains("FileTypeFilter.Add(\"*\")", code, StringComparison.Ordinal);
        Assert.Contains("SemaphoreSlim", code, StringComparison.Ordinal);
        Assert.Contains("MusicFolderPickResult.Canceled", code, StringComparison.Ordinal);
        Assert.Contains("MusicFolderPickResult.Failed", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalLibraryPages_ContainNoHardCodedTrackItems()
    {
        foreach (var relative in new[]
        {
            new[] { "Views", "Library", "LibraryPage.xaml" },
            new[] { "Views", "Library", "AlbumsPage.xaml" },
            new[] { "Views", "Library", "ArtistsPage.xaml" },
            new[] { "Views", "Library", "FavoritesPage.xaml" }
        })
        {
            var segments = new[] { "src", "PrismWave.WinUI" }.Concat(relative).ToArray();
            var document = XDocument.Load(FindFile(segments));
            Assert.DoesNotContain(document.Descendants(), element =>
                element.Name.LocalName is "TrackModel" or "AlbumModel" or "ArtistModel");
        }
    }

    [Fact]
    public void EmptyLibraryState_OffersSharedAddFolderAction()
    {
        var document = XDocument.Load(FindFile(
            "src", "PrismWave.WinUI", "Views", "Library", "LibraryPage.xaml"));

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attribute("AutomationProperties.Name")?.Value == "Add folder from empty library"
            && element.Attribute("Command")?.Value == "{Binding LibraryFolders.AddFolderCommand}");
    }

    private static string FindFile(params string[] relativeSegments)
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

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relativeSegments));
    }
}
