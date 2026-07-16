using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class ArtistsPage : Page
{
    public ArtistsPage()
    {
        InitializeComponent();
        DataContext = App.Services.Artists;
    }

    private void Artists_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistModel artist)
        {
            App.Services.Artists.SelectArtistCommand.Execute(artist);
        }
    }

    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackModel track)
        {
            App.Services.Artists.PlayTrackCommand.Execute(track);
        }
    }
}
