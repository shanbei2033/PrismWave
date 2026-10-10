using Avalonia.Controls;
using Avalonia.Interactivity;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Library;

namespace PrismWave.Avalonia.Views.Library;

public partial class AlbumsPage : UserControl
{
    private AlbumsViewModel Vm => (AlbumsViewModel)DataContext!;

    public AlbumsPage()
    {
        InitializeComponent();
        DataContext = App.Services.Albums;
    }

    private void Albums_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is AlbumModel album)
        {
            Vm.SelectAlbumCommand.Execute(album);
            App.Services.Shell.NavigateCommand.Execute("LocalAlbumDetail");
        }

        AlbumGrid.SelectedIndex = -1;
    }

    private void PlayAlbum_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is AlbumModel album)
        {
            App.Services.Albums.PlayAlbumCommand.Execute(album);
        }
    }

    private void AddAlbumToQueue_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is AlbumModel album)
        {
            App.Services.Albums.AddAlbumToQueueCommand.Execute(album);
        }
    }

    private void FavoriteAlbum_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is AlbumModel album)
        {
            App.Services.Albums.ToggleAlbumFavoriteCommand.Execute(album);
        }
    }

    private void ViewAlbumArtist_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is AlbumModel album && !string.IsNullOrEmpty(album.Artist))
        {
            var artists = App.Services.Artists;
            var artist = artists.Artists.FirstOrDefault(a =>
                string.Equals(a.Name, album.Artist, StringComparison.OrdinalIgnoreCase));
            if (artist is not null)
            {
                artists.SelectArtistCommand.Execute(artist);
                App.Services.Shell.NavigateCommand.Execute("ArtistDetail");
            }
        }
    }
}
