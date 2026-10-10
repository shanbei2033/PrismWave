using Avalonia.Controls;
using PrismWave.Avalonia.Controls.Home;

namespace PrismWave.Avalonia.Views.Home;

public sealed partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        DataContext = App.Services.Home;
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
}
