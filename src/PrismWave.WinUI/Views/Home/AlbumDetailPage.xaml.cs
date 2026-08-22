using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Home;

public sealed partial class AlbumDetailPage : Page
{
    public AlbumDetailPage()
    {
        InitializeComponent();
        DataContext = App.Services.Home;
        Loaded += (_, _) => DataContext = App.Services.Home;
        Unloaded += (_, _) => DataContext = null;
    }

    private void Back_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        App.Services.Shell.GoBackCommand.Execute(null);
    }

    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeTrackModel track)
        {
            App.Services.Home.PlaySelectedAlbumTrackCommand.Execute(track);
        }
    }
}
