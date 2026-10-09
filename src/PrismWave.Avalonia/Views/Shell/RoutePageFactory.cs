using Avalonia.Controls;
using PrismWave.Avalonia.Views.Library;
using PrismWave.Avalonia.Views.Placeholders;

namespace PrismWave.Avalonia.Views.Shell;

/// <summary>
/// Shell 路由 → 页面实例工厂。Phase 4-6 逐步替换剩余占位页。
/// </summary>
public static class RoutePageFactory
{
    public static Control Create(string route)
    {
        return route switch
        {
            "Library" => new LibraryPage(),
            "Albums" => new AlbumsPage(),
            "Artists" => new ArtistsPage(),
            "Favorites" => new FavoritesPage(),
            "LocalAlbumDetail" => new LocalAlbumDetailPage(),
            "ArtistDetail" => new ArtistDetailPage(),
            "Home" => new RoutePlaceholderPage("首页"),
            "Search" => new RoutePlaceholderPage("搜索"),
            "Hits" => new RoutePlaceholderPage("HITS"),
            "Settings" => new RoutePlaceholderPage("设置"),
            _ => new RoutePlaceholderPage(route)
        };
    }
}
