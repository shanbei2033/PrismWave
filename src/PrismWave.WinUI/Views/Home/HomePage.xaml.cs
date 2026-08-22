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
        // Re-bind on Loaded: nested navigation caches this page instance and
        // restores it without re-running the constructor, so Unloaded's null
        // assignment must be undone when the cached page re-enters the tree.
        Loaded += (_, _) => DataContext = App.Services.Home;
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
