using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave_WinUI.Views.Dialogs;

public sealed partial class LyricsSearchDialog : ContentDialog
{
    private LyricsSearchViewModel ViewModel { get; }

    public LyricsSearchDialog(PlaybackViewModel playback)
    {
        InitializeComponent();
        ViewModel = new LyricsSearchViewModel(playback);
        ViewModel.ResultApplied += ViewModel_ResultApplied;
        DataContext = ViewModel;
        Opened += LyricsSearchDialog_Opened;
        Closed += LyricsSearchDialog_Closed;
    }

    private void LyricsSearchDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        QueryBox.Focus(FocusState.Programmatic);
        QueryBox.SelectAll();
        ViewModel.SearchCommand.Execute(null);
    }

    private void LyricsSearchDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.ResultApplied -= ViewModel_ResultApplied;
    }

    private void ViewModel_ResultApplied(object? sender, EventArgs e)
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

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricsSearchResultModel result)
        {
            ViewModel.SelectResultCommand.Execute(result);
        }
    }
}
