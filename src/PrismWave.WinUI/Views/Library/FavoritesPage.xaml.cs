using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class FavoritesPage : Page
{
    public FavoritesPage()
    {
        InitializeComponent();
        DataContext = App.Services.Favorites;
    }

    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackModel track)
        {
            App.Services.Favorites.PlayTrackCommand.Execute(track);
        }
    }

    private async void Tracks_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        await App.Services.Favorites.PersistOrderAsync();
    }

    private async void Favorite_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrackModel track })
        {
            await App.Services.Favorites.ToggleFavoriteCommand.ExecuteAsync(track);
        }
    }
}
