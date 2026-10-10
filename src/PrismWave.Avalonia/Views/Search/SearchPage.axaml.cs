using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using PrismWave_WinUI.Models;

namespace PrismWave.Avalonia.Views.Search;

public sealed partial class SearchPage : UserControl
{
    public SearchPage()
    {
        InitializeComponent();
        DataContext = App.Services.Search;
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            App.Services.Search.RunSearchCommand.Execute(null);
        }
    }

    private void HistoryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        var value = listBox.SelectedItem as string;
        listBox.SelectedIndex = -1;
        if (value is not null)
        {
            App.Services.Search.SelectHistoryCommand.Execute(value);
        }
    }

    private void RemoveHistory_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as FluentAvalonia.UI.Controls.MenuFlyoutItem)?.Tag is string value)
        {
            App.Services.Search.RemoveHistoryCommand.Execute(value);
        }
    }

    private void ResultRow_Tapped(object? sender, TappedEventArgs e)
    {
        if (IsInsideButton(e.Source as Visual))
        {
            return;
        }

        if (sender is Grid { Tag: SearchResultModel result })
        {
            App.Services.Search.PlaySearchResultCommand.Execute(result);
        }
    }

    private void ResultRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsInsideButton(e.Source as Visual))
        {
            return;
        }

        if (sender is Grid { Tag: SearchResultModel result })
        {
            App.Services.Search.PlaySearchResultCommand.Execute(result);
        }
    }

    private void ResultMoreButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        // 菜单挂在行 Grid 的 ContextFlyout 上（与右键共用同一实例），锚定按钮弹出。
        for (var parent = button.Parent as Control; parent is not null; parent = parent.Parent as Control)
        {
            if (parent.ContextFlyout is FluentAvalonia.UI.Controls.FAMenuFlyout flyout)
            {
                flyout.ShowAt(button);
                return;
            }
        }
    }

    private static bool IsInsideButton(Visual? source)
    {
        while (source is not null)
        {
            if (source is Button)
            {
                return true;
            }

            source = source.GetVisualParent();
        }

        return false;
    }

    private void PlayResult_Click(object? sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.PlaySearchResultCommand);

    private void AddToQueue_Click(object? sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.AddToQueueCommand);

    private void PlayNext_Click(object? sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.PlayNextCommand);

    private void Favorite_Click(object? sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.ToggleFavoriteCommand);

    private void AddToLibrary_Click(object? sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.AddToLibraryCommand);

    private void ViewAlbum_Click(object? sender, RoutedEventArgs e)
    {
        if (GetResult(sender) is not { } result)
        {
            return;
        }

        var album = App.Services.LibraryService.Albums.FirstOrDefault(item =>
            string.Equals(item.Title, result.Album, StringComparison.CurrentCultureIgnoreCase)
            && (string.Equals(item.Artist, result.Artist, StringComparison.CurrentCultureIgnoreCase)
                || string.IsNullOrWhiteSpace(item.Artist)));
        if (album is null)
        {
            return;
        }

        App.Services.Albums.SelectAlbumCommand.Execute(album);
        App.Services.Shell.NavigateCommand.Execute("LocalAlbumDetail");
    }

    private void ViewArtist_Click(object? sender, RoutedEventArgs e)
    {
        if (GetResult(sender) is not { } result)
        {
            return;
        }

        var artist = App.Services.LibraryService.Artists.FirstOrDefault(item =>
            string.Equals(item.Name, result.Artist, StringComparison.CurrentCultureIgnoreCase));
        if (artist is null)
        {
            return;
        }

        App.Services.Artists.SelectArtistCommand.Execute(artist);
        App.Services.Shell.NavigateCommand.Execute("ArtistDetail");
    }

    private static SearchResultModel? GetResult(object? sender) =>
        (sender as FluentAvalonia.UI.Controls.MenuFlyoutItem)?.Tag as SearchResultModel;

    private static void ExecuteResultCommand(object? sender, System.Windows.Input.ICommand command)
    {
        if (GetResult(sender) is { } result && command.CanExecute(result))
        {
            command.Execute(result);
        }
    }
}
