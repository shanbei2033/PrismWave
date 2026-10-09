using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PrismWave.Avalonia.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Library;

namespace PrismWave.Avalonia.Views.Library;

public partial class ArtistDetailPage : UserControl
{
    public ArtistDetailPage()
    {
        InitializeComponent();
        DataContext = App.Services.Artists;
        AttachedToVisualTree += (_, _) => AttachTrackIndex();
        DetachedFromVisualTree += (_, _) => DetachTrackIndex();
        RebuildIndexedTracks();
    }

    private ArtistsViewModel Vm => (ArtistsViewModel)DataContext!;

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
        ArtistTracksList.ItemsSource = IndexedTrack.Wrap(Vm.SelectedTracks);
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

    private void ViewTrackAlbum_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not TrackModel track)
        {
            return;
        }

        var album = App.Services.LibraryService.Albums.FirstOrDefault(item =>
            string.Equals(item.Title, track.Album, StringComparison.CurrentCultureIgnoreCase)
            && string.Equals(item.Artist, track.Artist, StringComparison.CurrentCultureIgnoreCase));
        if (album is null)
        {
            return;
        }

        App.Services.Albums.SelectAlbumCommand.Execute(album);
        App.Services.Shell.NavigateCommand.Execute("LocalAlbumDetail");
    }
}
