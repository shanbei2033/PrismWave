using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Library;
using PrismWave_WinUI.Views.Dialogs;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class LibraryPage : Page
{
    public LibraryPage()
    {
        InitializeComponent();
        // 每次页面加载（包括从导航缓存恢复）都重新绑定 DataContext，
        // 否则从 TrackEditor 等嵌套路由返回时 DataContext 为 null 导致列表空白。
        Loaded += (_, _) => DataContext = App.Services.Library;
        Unloaded += (_, _) => DataContext = null;
    }

    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackModel track)
        {
            App.Services.Library.PlayTrackCommand.Execute(track);
        }
    }

    private async void Favorite_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrackModel track })
        {
            await App.Services.Library.ToggleFavoriteCommand.ExecuteAsync(track);
        }
    }

    private async void OpenFolderManager_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LibraryFoldersDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
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

    private async void EditMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: TrackModel track })
        {
            return;
        }

        await App.Services.TrackEditor.LoadAsync(track);
        if (App.Services.Shell.NavigateCommand.CanExecute("TrackEditor"))
        {
            App.Services.Shell.NavigateCommand.Execute("TrackEditor");
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

        // Security: Validate file path and prevent command injection
        var normalizedPath = Path.GetFullPath(track.Path);
        
        // Check for path traversal attempts
        if (normalizedPath.Contains(".."))
        {
            StartupLog.Write($"library.location.security-block.path-traversal: path={normalizedPath}");
            return;
        }
        
        // Validate file is an audio file
        var extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
        var allowedAudioExtensions = new[] { ".mp3", ".flac", ".wav", ".ogg", ".wma", ".m4a", ".aac" };
        if (!allowedAudioExtensions.Contains(extension))
        {
            StartupLog.Write($"library.location.security-block-invalid-type: path={normalizedPath}, extension={extension}");
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add($"/select,{normalizedPath}");
        Process.Start(startInfo);
    }
}
