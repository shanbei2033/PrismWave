using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Search;

public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
        DataContext = App.Services.Search;
        Unloaded += (_, _) => DataContext = null;
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(sender.Text))
        {
            App.Services.Search.RunSearchCommand.Execute(null);
        }
    }

    private void History_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string value)
        {
            App.Services.Search.SelectHistoryCommand.Execute(value);
        }
    }

    private void RemoveHistory_Click(object sender, RoutedEventArgs e)
    {
        // MenuFlyoutItem lives outside the visual tree (inside a Flyout),
        // so Tag binding on it may not resolve. Walk up from the flyout's
        // placement target to find the parent Grid whose Tag holds the
        // history string.
        if (sender is not FrameworkElement element)
        {
            return;
        }

        string? value = element.Tag as string ?? element.DataContext as string;
        if (string.IsNullOrEmpty(value))
        {
            // The flyout's owner is the Grid that carries Tag="{Binding}"
            var owner = element.XamlRoot is null
                ? null
                : FindAncestorWithTag(element);
            if (owner is { Tag: string tag })
            {
                value = tag;
            }
        }

        if (!string.IsNullOrEmpty(value))
        {
            App.Services.Search.RemoveHistoryCommand.Execute(value);
        }
    }

    private static FrameworkElement? FindAncestorWithTag(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.Tag is string)
            {
                return fe;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ResultRow_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SearchResultModel result })
        {
            App.Services.Search.PlaySearchResultCommand.Execute(result);
            e.Handled = true;
        }
    }

    private void ResultRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.Handled || IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is FrameworkElement { Tag: SearchResultModel result })
        {
            App.Services.Search.PlaySearchResultCommand.Execute(result);
            e.Handled = true;
        }
    }

    private void ResultMoreButton_Tapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
            {
                return true;
            }

            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void PlayResult_Click(object sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.PlaySearchResultCommand);

    private void AddToQueue_Click(object sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.AddToQueueCommand);

    private void PlayNext_Click(object sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.PlayNextCommand);

    private void Favorite_Click(object sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.ToggleFavoriteCommand);

    private void AddToLibrary_Click(object sender, RoutedEventArgs e) =>
        ExecuteResultCommand(sender, App.Services.Search.AddToLibraryCommand);

    private void ViewAlbum_Click(object sender, RoutedEventArgs e)
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

    private void ViewArtist_Click(object sender, RoutedEventArgs e)
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

    private static SearchResultModel? GetResult(object sender) =>
        sender is FrameworkElement { Tag: SearchResultModel result } ? result : null;

    private static void ExecuteResultCommand(object sender, System.Windows.Input.ICommand command)
    {
        if (GetResult(sender) is { } result && command.CanExecute(result))
        {
            command.Execute(result);
        }
    }
}
