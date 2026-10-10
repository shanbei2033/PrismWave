using Avalonia.Controls;
using Avalonia.Interactivity;
using PrismWave_WinUI.Models;

namespace PrismWave.Avalonia.Views.Home;

public sealed partial class AlbumDetailPage : UserControl
{
    public AlbumDetailPage()
    {
        InitializeComponent();
        DataContext = App.Services.Home;
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        App.Services.Shell.GoBackCommand.Execute(null);
    }

    private void TracksList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        var track = listBox.SelectedItem as HomeTrackModel;
        listBox.SelectedIndex = -1;
        if (track is not null)
        {
            App.Services.Home.PlaySelectedAlbumTrackCommand.Execute(track);
        }
    }
}
