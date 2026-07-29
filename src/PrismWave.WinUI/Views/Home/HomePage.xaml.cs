using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Controls.Home;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Home;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        DataContext = App.Services.Home;
        Unloaded += (_, _) => DataContext = null;
    }

    private void TrendingBanner_OpenRequested(object? sender, EventArgs e)
    {
        App.Services.Home.SelectHomeSectionCommand.Execute(App.Services.Home.TopPlaylist);
        App.Services.Shell.NavigateCommand.Execute("TopPlaylist");
    }

    private void GenreExplorer_OpenRequested(object? sender, SectionOpenRequestedEventArgs e)
    {
        App.Services.Home.SelectHomeSectionCommand.Execute(e.Section);
        App.Services.Shell.NavigateCommand.Execute("TopPlaylist");
    }

    private void Albums_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AlbumModel album)
        {
            App.Services.Home.SelectAlbumCommand.Execute(album);
            App.Services.Shell.NavigateCommand.Execute("AlbumDetail");
        }
    }
}
