using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using PrismWave_WinUI.Infrastructure.Animation;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class AlbumsPage : Page
{
    private const double DesiredItemWidth = 188;
    private const double ItemSpacing = 14;

    public AlbumsPage()
    {
        InitializeComponent();
        DataContext = App.Services.Albums;
    }

    private void Albums_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AlbumModel album)
        {
            return;
        }

        App.Services.Albums.SelectAlbumCommand.Execute(album);
        App.Services.Shell.NavigateCommand.Execute("LocalAlbumDetail");
    }

    private void AlbumGrid_Loaded(object sender, RoutedEventArgs e) => UpdateGridItemWidth();

    private void AlbumGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateGridItemWidth();

    private void UpdateGridItemWidth()
    {
        if (AlbumGrid.ItemsPanelRoot is not ItemsWrapGrid panel || AlbumGrid.ActualWidth <= 0)
        {
            return;
        }

        var available = Math.Max(220, AlbumGrid.ActualWidth - 8);
        var columns = Math.Max(1, (int)Math.Floor((available + ItemSpacing) / (DesiredItemWidth + ItemSpacing)));
        panel.ItemWidth = Math.Max(148, Math.Floor(available / columns) - ItemSpacing);
    }

    private void AlbumCover_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement cover && Math.Abs(cover.Height - e.NewSize.Width) > 0.5)
        {
            cover.Height = e.NewSize.Width;
        }
    }

    private void AlbumCard_PointerEntered(object sender, PointerRoutedEventArgs e) => SetOverlayOpacity(sender, 1);

    private void AlbumCard_PointerExited(object sender, PointerRoutedEventArgs e) => SetOverlayOpacity(sender, 0);

    private static void SetOverlayOpacity(object sender, double opacity)
    {
        if (sender is not FrameworkElement element
            || element.FindName("AlbumPlayOverlay") is not Button overlay)
        {
            return;
        }

        overlay.IsHitTestVisible = opacity > 0;
        AnimateOpacity(overlay, opacity);
        if (element.FindName("AlbumHoverShade") is FrameworkElement shade)
        {
            AnimateOpacity(shade, opacity);
        }
    }

    private static void AnimateOpacity(FrameworkElement element, double target)
    {
        if (!MotionPolicy.ShouldAnimateInteraction())
        {
            element.Opacity = target;
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(180);
        animation.InsertKeyFrame(1f, (float)target, visual.Compositor.CreateCubicBezierEasingFunction(
            new System.Numerics.Vector2(0.2f, 0f),
            new System.Numerics.Vector2(0f, 1f)));
        visual.StartAnimation("Opacity", animation);
    }

    private void PlayAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (GetAlbum(sender) is { } album)
        {
            App.Services.Albums.PlayAlbumCommand.Execute(album);
        }
    }

    private void AddAlbumToQueue_Click(object sender, RoutedEventArgs e)
    {
        if (GetAlbum(sender) is { } album)
        {
            App.Services.Albums.AddAlbumToQueueCommand.Execute(album);
        }
    }

    private void FavoriteAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (GetAlbum(sender) is { } album)
        {
            App.Services.Albums.ToggleAlbumFavoriteCommand.Execute(album);
        }
    }

    private void ViewAlbumArtist_Click(object sender, RoutedEventArgs e)
    {
        if (GetAlbum(sender) is not { } album)
        {
            return;
        }

        var artist = App.Services.LibraryService.Artists.FirstOrDefault(item =>
            string.Equals(item.Name, album.Artist, StringComparison.CurrentCultureIgnoreCase));
        if (artist is null)
        {
            return;
        }

        App.Services.Artists.SelectArtistCommand.Execute(artist);
        App.Services.Shell.NavigateCommand.Execute("ArtistDetail");
    }

    private static AlbumModel? GetAlbum(object sender) =>
        sender is FrameworkElement { Tag: AlbumModel album } ? album : null;
}
