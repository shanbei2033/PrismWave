using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class AlbumsPage : Page
{
    public AlbumsPage()
    {
        InitializeComponent();
        DataContext = App.Services.Albums;
    }

    private void Albums_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AlbumModel album)
        {
            App.Services.Albums.SelectAlbumCommand.Execute(album);
        }
    }

    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackModel track)
        {
            App.Services.Albums.PlayTrackCommand.Execute(track);
        }
    }
}
