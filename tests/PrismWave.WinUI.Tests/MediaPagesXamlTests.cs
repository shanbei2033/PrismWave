using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class MediaPagesXamlTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SearchPage_UsesEnterOnlySingleColumnLayout()
    {
        var path = FindFile("src", "PrismWave.WinUI", "Views", "Search", "SearchPage.xaml");
        var document = XDocument.Load(path);
        var code = File.ReadAllText(Path.ChangeExtension(path, ".xaml.cs"));

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "AutoSuggestBox"
            && element.Attribute("PlaceholderText")?.Value == "搜索在线和本地音乐"
            && element.Attribute("QueryIcon")?.Value == "Find"
            && element.Attribute("QuerySubmitted") is not null);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Content")?.Value is "Search" or "Clear");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "ColumnDefinition"
            && element.Attribute("Width")?.Value == "260");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("Style")?.Value == "{StaticResource PrismPanelBorderStyle}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ListView"
            && element.Attribute("ItemsSource")?.Value == "{Binding DisplayItems}");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "StableCoverImage");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "MenuFlyoutItem"
            && element.Attribute("Text")?.Value == "删除此记录");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ProgressRing"
            && element.Attribute("Visibility")?.Value == "{Binding IsLoadingStatus, Converter={StaticResource BoolToVisibilityConverter}}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "FontIcon"
            && element.Attribute("Visibility")?.Value == "{Binding IsErrorStatus, Converter={StaticResource BoolToVisibilityConverter}}");
        Assert.Contains("DoubleTapped", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchViewModel_DoesNotDebounceQueryEdits()
    {
        var code = File.ReadAllText(FindFile(
            "src", "PrismWave.WinUI", "ViewModels", "Search", "SearchViewModel.cs"));

        Assert.DoesNotContain("ScheduleSearchAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(350", code, StringComparison.Ordinal);
        Assert.Contains("AddSource(\"online\", \"在线音乐\")", code, StringComparison.Ordinal);
        Assert.Contains("SearchAsync(query, cancellationToken)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AlbumsPage_UsesResponsiveCoverGridWithoutPermanentDetailPanel()
    {
        var path = FindFile("src", "PrismWave.WinUI", "Views", "Library", "AlbumsPage.xaml");
        var document = XDocument.Load(path);

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "AutoSuggestBox"
            && element.Attribute("PlaceholderText")?.Value == "搜索专辑 / 艺术家 / 歌名");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "GridView"
            && element.Attribute("ItemsSource")?.Value == "{Binding FilteredAlbums}");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ItemsWrapGrid");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "StableCoverImage");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute(X + "Name")?.Value == "AlbumPlayOverlay");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "ColumnDefinition"
            && element.Attribute("Width")?.Value == "350");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("ItemsSource")?.Value == "{Binding SelectedTracks}");
    }

    [Fact]
    public void AlbumDetail_UsesOneVirtualizedScrollSurfaceWithHeroBlend()
    {
        var path = FindFile("src", "PrismWave.WinUI", "Views", "Library", "LocalAlbumDetailPage.xaml");
        var document = XDocument.Load(path);
        var code = File.ReadAllText(Path.ChangeExtension(path, ".xaml.cs"));

        var trackList = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ListView"
            && element.Attribute("ItemsSource")?.Value == "{Binding SelectedTracks}");
        Assert.Contains(trackList.Elements(), element => element.Name.LocalName == "ListView.Header");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ScrollViewer");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute(X + "Name")?.Value == "HeroBlurHost");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "LinearGradientBrush");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "StableCoverImage");
        Assert.DoesNotContain(document.Descendants().Attributes("Margin"), attribute =>
            attribute.Value.Split(',').Any(value => value.TrimStart().StartsWith("-", StringComparison.Ordinal)));
        Assert.Contains("GaussianBlurEffect", code, StringComparison.Ordinal);
        Assert.Contains("GetScrollViewerManipulationPropertySet", code, StringComparison.Ordinal);
        Assert.Contains("DoubleTapped", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AlbumDetail_KeepsHeroTransitionAttachedToOpaqueTrackSurface()
    {
        var path = FindFile("src", "PrismWave.WinUI", "Views", "Library", "LocalAlbumDetailPage.xaml");
        var document = XDocument.Load(path);
        var code = File.ReadAllText(Path.ChangeExtension(path, ".xaml.cs"));

        var transition = Assert.Single(document.Descendants(), element =>
            element.Attribute(X + "Name")?.Value == "AlbumTransitionLayer");
        Assert.Equal("False", transition.Attribute("IsHitTestVisible")?.Value);
        Assert.InRange(int.Parse(transition.Attribute("Height")!.Value), 100, 180);

        var backButton = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Click")?.Value == "Back_Click");
        Assert.Equal("3", backButton.Attribute("Canvas.ZIndex")?.Value);

        var trackItemStyle = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Style"
            && element.Attribute(X + "Key")?.Value == "AlbumTrackItemStyle");
        Assert.Contains(trackItemStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Background"
            && element.Attribute("Value")?.Value == "{StaticResource PrismBackgroundBrush}");

        Assert.Contains("CreateInsetClip", code, StringComparison.Ordinal);
        Assert.Contains("AlbumHero", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AlbumDetail_FavoriteButtonProvidesUnclippedFluentIconBounds()
    {
        var path = FindFile("src", "PrismWave.WinUI", "Views", "Library", "LocalAlbumDetailPage.xaml");
        var document = XDocument.Load(path);

        var buttonStyle = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Style"
            && element.Attribute(X + "Key")?.Value == "AlbumTrackActionButtonStyle");
        Assert.Contains(buttonStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Padding"
            && element.Attribute("Value")?.Value == "0");
        Assert.Contains(buttonStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Width"
            && element.Attribute("Value")?.Value == "38");
        Assert.Contains(buttonStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Height"
            && element.Attribute("Value")?.Value == "38");

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "FontIcon"
            && element.Attribute("Glyph")?.Value == "{Binding FavoriteGlyph}"
            && element.Attribute("FontSize")?.Value == "18");
        Assert.DoesNotContain(document.Descendants().Attributes("Margin"), attribute =>
            attribute.Value.Split(',').Any(value => value.TrimStart().StartsWith("-", StringComparison.Ordinal)));
    }

    [Fact]
    public void Shell_DeclaresLocalAlbumDetailAsNestedRoute()
    {
        var shellViewModel = File.ReadAllText(FindFile(
            "src", "PrismWave.WinUI", "ViewModels", "Shell", "ShellViewModel.cs"));
        var shellPage = File.ReadAllText(FindFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

        Assert.Contains("LocalAlbumDetail", shellViewModel, StringComparison.Ordinal);
        Assert.Contains("LocalAlbumDetailPage", shellPage, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtistsPage_UsesSimpleSearchableRowsInsteadOfCoverCards()
    {
        var path = FindFile("src", "PrismWave.WinUI", "Views", "Library", "ArtistsPage.xaml");
        var document = XDocument.Load(path);

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "AutoSuggestBox"
            && element.Attribute("PlaceholderText")?.Value == "搜索艺术家");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ListView"
            && element.Attribute("ItemsSource")?.Value == "{Binding FilteredArtists}");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "GridView");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName is "Image" or "StableCoverImage");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "FontIcon"
            && element.Attribute("Glyph")?.Value == "\uE76C");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("ItemsSource")?.Value == "{Binding SelectedTracks}");
    }

    [Fact]
    public void ArtistDetail_UsesDirectTrackListWithoutHero()
    {
        var path = FindFile("src", "PrismWave.WinUI", "Views", "Library", "ArtistDetailPage.xaml");
        var document = XDocument.Load(path);
        var code = File.ReadAllText(Path.ChangeExtension(path, ".xaml.cs"));

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ListView"
            && element.Attribute("ItemsSource")?.Value == "{Binding SelectedTracks}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Click")?.Value == "Back_Click");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName is "Image" or "StableCoverImage");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute(X + "Name")?.Value?.Contains("Hero", StringComparison.Ordinal) == true);
        Assert.Contains("DoubleTapped", code, StringComparison.Ordinal);
        Assert.Contains("AddTrackToQueueCommand", code, StringComparison.Ordinal);
        Assert.Contains("PlayTrackNextCommand", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_DeclaresArtistDetailAsNestedRoute()
    {
        var shellViewModel = File.ReadAllText(FindFile(
            "src", "PrismWave.WinUI", "ViewModels", "Shell", "ShellViewModel.cs"));
        var shellPage = File.ReadAllText(FindFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

        Assert.Contains("ArtistDetail", shellViewModel, StringComparison.Ordinal);
        Assert.Contains("ArtistDetailPage", shellPage, StringComparison.Ordinal);
    }

    [Fact]
    public void FavoritesPage_UsesDirectVirtualizedTrackListWithoutPanelChrome()
    {
        var path = FindFile("src", "PrismWave.WinUI", "Views", "Library", "FavoritesPage.xaml");
        var document = XDocument.Load(path);
        var code = File.ReadAllText(Path.ChangeExtension(path, ".xaml.cs"));

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "AutoSuggestBox"
            && element.Attribute("PlaceholderText")?.Value == "搜索收藏歌曲");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ListView"
            && element.Attribute("ItemsSource")?.Value == "{Binding VisibleTracks}");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "StableCoverImage");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("Style")?.Value == "{StaticResource PrismPanelBorderStyle}");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Content")?.Value == "Play all");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "AddDeleteThemeTransition");
        Assert.Contains("DoubleTapped", code, StringComparison.Ordinal);
        Assert.Contains("AddTrackToQueueCommand", code, StringComparison.Ordinal);
        Assert.Contains("PlayTrackNextCommand", code, StringComparison.Ordinal);
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
