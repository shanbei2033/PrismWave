using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave_WinUI.Views.Dialogs;

public sealed partial class CoverSearchDialog : ContentDialog
{
    private CoverSearchViewModel ViewModel { get; }

    public CoverSearchDialog(ICoverService coverService, TrackModel track)
    {
        InitializeComponent();
        ViewModel = new CoverSearchViewModel(coverService, track);
        ViewModel.CoverApplied += ViewModel_CoverApplied;
        DataContext = ViewModel;
        Opened += CoverSearchDialog_Opened;
        Closed += CoverSearchDialog_Closed;
    }

    private void CoverSearchDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        QueryBox.Focus(FocusState.Programmatic);
        QueryBox.SelectAll();
        ViewModel.SearchCommand.Execute(null);
    }

    private void CoverSearchDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.CoverApplied -= ViewModel_CoverApplied;
    }

    private void ViewModel_CoverApplied(object? sender, EventArgs e)
    {
        Hide();
    }

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ViewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ResultsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CoverSearchResultModel result)
        {
            ViewModel.SelectResultCommand.Execute(result);
        }
    }
}
