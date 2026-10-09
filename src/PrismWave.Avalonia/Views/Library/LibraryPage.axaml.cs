using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Library;

namespace PrismWave.Avalonia.Views.Library;

public partial class LibraryPage : UserControl
{
    private LibraryViewModel Vm => (LibraryViewModel)DataContext!;

    public LibraryPage()
    {
        InitializeComponent();
        DataContext = App.Services.Library;
    }

    private void Tracks_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is TrackModel track)
        {
            Vm.PlayTrack(track);
        }

        TracksList.SelectedIndex = -1;
    }

    private async void OpenFolderManager_Click(object? sender, RoutedEventArgs e)
    {
        await Dialogs.LibraryFoldersDialog.ShowAsync(this);
    }

    private void OpenLocation_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is TrackModel track && File.Exists(track.Path))
        {
            Process.Start("explorer.exe", $"/select,\"{track.Path}\"");
        }
    }

    private async void RemoveTrack_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is TrackModel track)
        {
            await Vm.RemoveTrackAsync(track, deleteSourceFile: false);
        }
    }
}
