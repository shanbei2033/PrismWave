using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PrismWave.Avalonia.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Library;

namespace PrismWave.Avalonia.Views.Library;

public partial class LocalAlbumDetailPage : UserControl
{
    public LocalAlbumDetailPage()
    {
        InitializeComponent();
        DataContext = App.Services.Albums;
        AttachedToVisualTree += (_, _) => AttachTrackIndex();
        DetachedFromVisualTree += (_, _) => DetachTrackIndex();
        RebuildIndexedTracks();
    }

    private AlbumsViewModel Vm => (AlbumsViewModel)DataContext!;

    private void AttachTrackIndex()
    {
        Vm.SelectedTracks.CollectionChanged += SelectedTracks_CollectionChanged;
        RebuildIndexedTracks();
    }

    private void DetachTrackIndex()
    {
        Vm.SelectedTracks.CollectionChanged -= SelectedTracks_CollectionChanged;
    }

    private void SelectedTracks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildIndexedTracks();

    private void RebuildIndexedTracks()
    {
        AlbumTracksList.ItemsSource = IndexedTrack.Wrap(Vm.SelectedTracks);
    }

    private void Back_Click(object? sender, RoutedEventArgs e) =>
        App.Services.Shell.GoBackCommand.Execute(null);

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

    private async void FavoriteTrack_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is TrackModel track)
        {
            await Vm.ToggleFavoriteCommand.ExecuteAsync(track);
        }
    }

    private void ViewTrackArtist_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not TrackModel track)
        {
            return;
        }

        var artist = App.Services.LibraryService.Artists.FirstOrDefault(item =>
            string.Equals(item.Name, track.Artist, StringComparison.CurrentCultureIgnoreCase));
        if (artist is null)
        {
            return;
        }

        App.Services.Artists.SelectArtistCommand.Execute(artist);
        App.Services.Shell.NavigateCommand.Execute("ArtistDetail");
    }
}
