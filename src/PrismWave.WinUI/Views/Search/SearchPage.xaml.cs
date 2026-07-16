using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Search;

public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
        DataContext = App.Services.Search;
    }

    private void History_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string value)
        {
            App.Services.Search.SelectHistoryCommand.Execute(value);
        }
    }

    private void Results_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResultModel result)
        {
            App.Services.Search.PlaySearchResultCommand.Execute(result);
        }
    }
}
