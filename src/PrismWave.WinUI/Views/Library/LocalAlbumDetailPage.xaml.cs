using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PrismWave_WinUI.Infrastructure.Animation;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class LocalAlbumDetailPage : Page
{
    private CompositionEffectBrush? _blurBrush;
    private CompositionMaskBrush? _blurMaskBrush;
    private CompositionLinearGradientBrush? _blurMaskGradient;
    private SpriteVisual? _blurVisual;
    private InsetClip? _heroClip;
    private ScrollViewer? _scrollViewer;

    public LocalAlbumDetailPage()
    {
        InitializeComponent();
        DataContext = App.Services.Albums;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureHeroClip();
            EnsureHeroBlur();
            _ = DispatcherQueue.TryEnqueue(AttachScrollAnimations);
        }
        catch (Exception exception)
        {
            StartupLog.Write($"album.detail.visuals.failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(HeroCoverImage, true);
        var heroVisual = ElementCompositionPreview.GetElementVisual(HeroCoverImage);
        heroVisual.StopAnimation("Translation.Y");
        heroVisual.StopAnimation("Opacity");
        var heroHostVisual = ElementCompositionPreview.GetElementVisual(AlbumHero);
        heroHostVisual.Clip = null;
        _heroClip?.Dispose();
        _heroClip = null;
        ElementCompositionPreview.SetElementChildVisual(HeroBlurHost, null);
        _blurBrush?.Dispose();
        _blurBrush = null;
        _blurMaskBrush?.Dispose();
        _blurMaskBrush = null;
        _blurMaskGradient?.Dispose();
        _blurMaskGradient = null;
        _blurVisual?.Dispose();
        _blurVisual = null;
        _scrollViewer = null;
    }

    private void EnsureHeroClip()
    {
        var heroVisual = ElementCompositionPreview.GetElementVisual(AlbumHero);
        _heroClip ??= heroVisual.Compositor.CreateInsetClip();
        heroVisual.Clip = _heroClip;
    }

    private void EnsureHeroBlur()
    {
        if (_blurVisual is not null)
        {
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(HeroBlurHost).Compositor;
        var blur = new GaussianBlurEffect
        {
            Name = "AlbumHeroBlur",
            BlurAmount = 18f,
            BorderMode = EffectBorderMode.Hard,
            Optimization = EffectOptimization.Balanced,
            Source = new CompositionEffectSourceParameter("HeroBackdrop")
        };
        var factory = compositor.CreateEffectFactory(blur);
        _blurBrush = factory.CreateBrush();
        _blurBrush.SetSourceParameter("HeroBackdrop", compositor.CreateBackdropBrush());
        _blurMaskGradient = compositor.CreateLinearGradientBrush();
        _blurMaskGradient.StartPoint = new Vector2(0.5f, 0f);
        _blurMaskGradient.EndPoint = new Vector2(0.5f, 1f);
        _blurMaskGradient.ColorStops.Add(compositor.CreateColorGradientStop(0f, Microsoft.UI.Colors.Transparent));
        _blurMaskGradient.ColorStops.Add(compositor.CreateColorGradientStop(0.42f, Microsoft.UI.Colors.White));
        _blurMaskGradient.ColorStops.Add(compositor.CreateColorGradientStop(1f, Microsoft.UI.Colors.White));
        _blurMaskBrush = compositor.CreateMaskBrush();
        _blurMaskBrush.Source = _blurBrush;
        _blurMaskBrush.Mask = _blurMaskGradient;
        _blurVisual = compositor.CreateSpriteVisual();
        _blurVisual.RelativeSizeAdjustment = Vector2.One;
        _blurVisual.Brush = _blurMaskBrush;
        ElementCompositionPreview.SetElementChildVisual(HeroBlurHost, _blurVisual);
    }

    private void AttachScrollAnimations()
    {
        try
        {
            _scrollViewer = FindDescendant<ScrollViewer>(AlbumTracksList);
            if (_scrollViewer is null || !MotionPolicy.ShouldAnimateInteraction())
            {
                return;
            }

            var properties = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(_scrollViewer);
            ElementCompositionPreview.SetIsTranslationEnabled(HeroCoverImage, true);
            var heroVisual = ElementCompositionPreview.GetElementVisual(HeroCoverImage);
            var compositor = heroVisual.Compositor;
            var parallax = compositor.CreateExpressionAnimation(
                "Clamp(-scroll.Translation.Y * 0.18f, 0.0f, 72.0f)");
            parallax.SetReferenceParameter("scroll", properties);
            heroVisual.StartAnimation("Translation.Y", parallax);
            var fade = compositor.CreateExpressionAnimation(
                "Clamp(1.0f + scroll.Translation.Y / 360.0f, 0.16f, 1.0f)");
            fade.SetReferenceParameter("scroll", properties);
            heroVisual.StartAnimation("Opacity", fade);
        }
        catch (Exception exception)
        {
            StartupLog.Write($"album.detail.scroll-animation.failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        App.Services.Shell.GoBackCommand.Execute(null);

    private void TrackRow_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (GetTrack(sender) is { } track)
        {
            App.Services.Albums.PlayTrackCommand.Execute(track);
            e.Handled = true;
        }
    }

    private void AlbumTracksList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue
            && args.ItemContainer.ContentTemplateRoot is FrameworkElement root
            && root.FindName("TrackIndex") is TextBlock index)
        {
            index.Text = (args.ItemIndex + 1).ToString("00");
        }
    }

    private void PlayTrack_Click(object sender, RoutedEventArgs e) =>
        ExecuteTrackCommand(sender, App.Services.Albums.PlayTrackCommand);

    private void AddTrackToQueue_Click(object sender, RoutedEventArgs e) =>
        ExecuteTrackCommand(sender, App.Services.Albums.AddTrackToQueueCommand);

    private void PlayTrackNext_Click(object sender, RoutedEventArgs e) =>
        ExecuteTrackCommand(sender, App.Services.Albums.PlayTrackNextCommand);

    private void FavoriteTrack_Click(object sender, RoutedEventArgs e) =>
        ExecuteTrackCommand(sender, App.Services.Albums.ToggleFavoriteCommand);

    private void ViewTrackArtist_Click(object sender, RoutedEventArgs e)
    {
        if (GetTrack(sender) is not { } track)
        {
            return;
        }

        var artist = App.Services.LibraryService.Artists.FirstOrDefault(item =>
            string.Equals(item.Name, track.Artist, StringComparison.CurrentCultureIgnoreCase));
        if (artist is null)
        {
            return;
        }

        App.Services.Artists.SelectArtistCommand.Execute(artist);
        App.Services.Shell.NavigateCommand.Execute("ArtistDetail");
    }

    private static TrackModel? GetTrack(object sender) =>
        sender is FrameworkElement { Tag: TrackModel track } ? track : null;

    private static void ExecuteTrackCommand(object sender, System.Windows.Input.ICommand command)
    {
        if (GetTrack(sender) is { } track && command.CanExecute(track))
        {
            command.Execute(track);
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
