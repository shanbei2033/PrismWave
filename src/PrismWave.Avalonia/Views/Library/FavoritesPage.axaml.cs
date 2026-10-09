using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Library;

namespace PrismWave.Avalonia.Views.Library;

public partial class FavoritesPage : UserControl
{
    private FavoritesViewModel Vm => (FavoritesViewModel)DataContext!;

    public FavoritesPage()
    {
        InitializeComponent();
        DataContext = App.Services.Favorites;
    }

    private void TrackRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.Tag is TrackModel track)
        {
            Vm.PlayTrackCommand.Execute(track);
            e.Handled = true;
        }
    }

    private void PlayTrack_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is TrackModel track)
        {
            Vm.PlayTrackCommand.Execute(track);
        }
    }

    private void AddTrackToQueue_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is TrackModel track)
        {
            Vm.AddTrackToQueueCommand.Execute(track);
        }
    }

    private void PlayTrackNext_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is TrackModel track)
        {
            Vm.PlayTrackNextCommand.Execute(track);
        }
    }

    private async void Favorite_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is TrackModel track)
        {
            await Vm.ToggleFavoriteCommand.ExecuteAsync(track);
        }
    }
}
