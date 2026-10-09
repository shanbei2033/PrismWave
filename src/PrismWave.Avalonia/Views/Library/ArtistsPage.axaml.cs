using Avalonia.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave.Avalonia.Views.Library;

public partial class ArtistsPage : UserControl
{
    public ArtistsPage()
    {
        InitializeComponent();
        DataContext = App.Services.Artists;
    }

    private void Artists_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ArtistModel artist)
        {
            App.Services.Artists.SelectArtistCommand.Execute(artist);
            App.Services.Shell.NavigateCommand.Execute("ArtistDetail");
        }

        ArtistsList.SelectedIndex = -1;
    }
}
