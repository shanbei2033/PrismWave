using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class FavoritesPage : Page
{
    public FavoritesPage()
    {
        InitializeComponent();
        DataContext = App.Services.Favorites;
        Unloaded += (_, _) => DataContext = null;
    }

    private void TrackRow_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackModel track })
        {
            App.Services.Favorites.PlayTrackCommand.Execute(track);
            e.Handled = true;
        }
    }

    private void PlayTrack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackModel track })
        {
            App.Services.Favorites.PlayTrackCommand.Execute(track);
        }
    }

    private void AddTrackToQueue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackModel track })
        {
            App.Services.Favorites.AddTrackToQueueCommand.Execute(track);
        }
    }

    private void PlayTrackNext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackModel track })
        {
            App.Services.Favorites.PlayTrackNextCommand.Execute(track);
        }
    }

    private async void Tracks_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        await App.Services.Favorites.PersistOrderAsync();

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackModel track })
        {
            await App.Services.Favorites.ToggleFavoriteCommand.ExecuteAsync(track);
        }
    }
}
