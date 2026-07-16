using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Library;
using PrismWave_WinUI.Views.Dialogs;
using Windows.Storage.Pickers;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class LibraryPage : Page
{
    public LibraryPage()
    {
        InitializeComponent();
        DataContext = App.Services.Library;
    }

    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackModel track)
        {
            App.Services.Library.PlayTrackCommand.Execute(track);
        }
    }

    private async void AddFolder_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await App.Services.Library.AddFolderAsync(folder.Path);
        }
    }

    private async void RemoveFolder_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folder })
        {
            await App.Services.Library.RemoveFolderCommand.ExecuteAsync(folder);
        }
    }

    private async void Favorite_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrackModel track })
        {
            await App.Services.Library.ToggleFavoriteCommand.ExecuteAsync(track);
        }
    }

    private async void Tracks_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (DataContext is LibraryViewModel viewModel)
        {
            await viewModel.PersistVisibleOrderAsync();
        }
    }

    private async void TrackDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: TrackModel track })
        {
            await ShowTrackDetailsAsync(track);
        }
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: TrackModel track })
        {
            OpenLocation(track);
        }
    }

    private async void RemoveTrack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: TrackModel track })
        {
            await ConfirmRemoveAsync(track);
        }
    }

    private async Task ShowTrackDetailsAsync(TrackModel track)
    {
        var dialog = new TrackDetailsDialog(track) { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            OpenLocation(track);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await ConfirmRemoveAsync(track);
        }
    }

    private async Task ConfirmRemoveAsync(TrackModel track)
    {
        var dialog = new TrackDeleteDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && DataContext is LibraryViewModel viewModel)
        {
            await viewModel.RemoveTrackAsync(track, dialog.DeleteSourceFile);
        }
    }

    private static void OpenLocation(TrackModel track)
    {
        if (!File.Exists(track.Path))
        {
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add($"/select,{track.Path}");
        Process.Start(startInfo);
    }
}
