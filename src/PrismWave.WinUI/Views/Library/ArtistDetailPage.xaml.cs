using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class ArtistDetailPage : Page
{
    public ArtistDetailPage()
    {
        InitializeComponent();
        DataContext = App.Services.Artists;
        Loaded += (_, _) => DataContext = App.Services.Artists;
        Unloaded += (_, _) => DataContext = null;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => App.Services.Shell.GoBackCommand.Execute(null);

    private void TrackRow_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (GetTrack(sender) is { } track)
        {
            App.Services.Artists.PlayTrackCommand.Execute(track);
            e.Handled = true;
        }
    }

    private void ArtistTracksList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue
            && args.ItemContainer.ContentTemplateRoot is FrameworkElement root
            && root.FindName("TrackIndex") is TextBlock index)
        {
            index.Text = (args.ItemIndex + 1).ToString("00");
        }
    }

    private void PlayTrack_Click(object sender, RoutedEventArgs e) =>
        ExecuteTrackCommand(sender, App.Services.Artists.PlayTrackCommand);

    private void AddTrackToQueue_Click(object sender, RoutedEventArgs e) =>
        ExecuteTrackCommand(sender, App.Services.Artists.AddTrackToQueueCommand);

    private void PlayTrackNext_Click(object sender, RoutedEventArgs e) =>
        ExecuteTrackCommand(sender, App.Services.Artists.PlayTrackNextCommand);

    private void FavoriteTrack_Click(object sender, RoutedEventArgs e) =>
        ExecuteTrackCommand(sender, App.Services.Artists.ToggleFavoriteCommand);

    private void ViewTrackAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (GetTrack(sender) is not { } track)
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

    private static TrackModel? GetTrack(object sender) =>
        sender is FrameworkElement { Tag: TrackModel track } ? track : null;

    private static void ExecuteTrackCommand(object sender, System.Windows.Input.ICommand command)
    {
        if (GetTrack(sender) is { } track && command.CanExecute(track))
        {
            command.Execute(track);
        }
    }
}
