using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class ArtistsPage : Page
{
    public ArtistsPage()
    {
        InitializeComponent();
        DataContext = App.Services.Artists;
    }

    private void Artists_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ArtistModel artist)
        {
            return;
        }

        App.Services.Artists.SelectArtistCommand.Execute(artist);
        App.Services.Shell.NavigateCommand.Execute("ArtistDetail");
    }
}
