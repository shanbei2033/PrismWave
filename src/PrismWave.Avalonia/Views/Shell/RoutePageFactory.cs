using Avalonia.Controls;
using PrismWave.Avalonia.Views.Home;
using PrismWave.Avalonia.Views.Hits;
using PrismWave.Avalonia.Views.Library;
using PrismWave.Avalonia.Views.Settings;
using PrismWave.Avalonia.Views.Placeholders;
using PrismWave.Avalonia.Views.Search;

namespace PrismWave.Avalonia.Views.Shell;

/// <summary>
/// Shell 路由 → 页面实例工厂。
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
            "Home" => new HomePage(),
            "Search" => new SearchPage(),
            "TopPlaylist" => new TopPlaylistPage(),
            "AlbumDetail" => new AlbumDetailPage(),
            "Hits" => new HitsStatusPage(),
            "Settings" => new SettingsPage(),
            _ => new RoutePlaceholderPage(route)
        };
    }
}
