using System.Collections;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Controls.Home;

public sealed partial class TrendingBanner : UserControl
{
    private readonly WeakCollectionChangedListener<TrendingBanner> _tracksListener;

    public TrendingBanner()
    {
        InitializeComponent();
        _tracksListener = new WeakCollectionChangedListener<TrendingBanner>(
            this,
            static (self, _, _) => self.UpdateCoverSlots());
        Unloaded += (_, _) => _tracksListener.Unsubscribe();
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(TrendingBanner), new PropertyMetadata("今日趋势"));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(TrendingBanner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BackdropUrlProperty = DependencyProperty.Register(
        nameof(BackdropUrl), typeof(string), typeof(TrendingBanner), new PropertyMetadata(null));

    public static readonly DependencyProperty TracksProperty = DependencyProperty.Register(
        nameof(Tracks),
        typeof(IEnumerable),
        typeof(TrendingBanner),
        new PropertyMetadata(null, OnTracksPropertyChanged));

    public static readonly DependencyProperty PlayCommandProperty = DependencyProperty.Register(
        nameof(PlayCommand), typeof(ICommand), typeof(TrendingBanner), new PropertyMetadata(null));

    public static readonly DependencyProperty CoverOneUrlProperty = DependencyProperty.Register(
        nameof(CoverOneUrl), typeof(string), typeof(TrendingBanner), new PropertyMetadata(null));

    public static readonly DependencyProperty CoverTwoUrlProperty = DependencyProperty.Register(
        nameof(CoverTwoUrl), typeof(string), typeof(TrendingBanner), new PropertyMetadata(null));

    public static readonly DependencyProperty CoverThreeUrlProperty = DependencyProperty.Register(
        nameof(CoverThreeUrl), typeof(string), typeof(TrendingBanner), new PropertyMetadata(null));

    public static readonly DependencyProperty CoverFourUrlProperty = DependencyProperty.Register(
        nameof(CoverFourUrl), typeof(string), typeof(TrendingBanner), new PropertyMetadata(null));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string? BackdropUrl
    {
        get => (string?)GetValue(BackdropUrlProperty);
        set => SetValue(BackdropUrlProperty, value);
    }

    public IEnumerable? Tracks
    {
        get => (IEnumerable?)GetValue(TracksProperty);
        set => SetValue(TracksProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => (ICommand?)GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public string? CoverOneUrl
    {
        get => (string?)GetValue(CoverOneUrlProperty);
        private set => SetValue(CoverOneUrlProperty, value);
    }

    public string? CoverTwoUrl
    {
        get => (string?)GetValue(CoverTwoUrlProperty);
        private set => SetValue(CoverTwoUrlProperty, value);
    }

    public string? CoverThreeUrl
    {
        get => (string?)GetValue(CoverThreeUrlProperty);
        private set => SetValue(CoverThreeUrlProperty, value);
    }

    public string? CoverFourUrl
    {
        get => (string?)GetValue(CoverFourUrlProperty);
        private set => SetValue(CoverFourUrlProperty, value);
    }

    public event EventHandler? OpenRequested;

    private void HeroSurface_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is Microsoft.UI.Xaml.DependencyObject source)
        {
            var ancestor = source;
            while (ancestor is not null)
            {
                if (ancestor is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                {
                    return;
                }
                ancestor = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(ancestor);
            }
        }

        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BannerRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var state = e.NewSize.Width >= 720 ? "Wide" : "Compact";
        VisualStateManager.GoToState(this, state, useTransitions: false);
    }

    private static void OnTracksPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var banner = (TrendingBanner)dependencyObject;
        banner._tracksListener.Subscribe(args.NewValue);
        banner.UpdateCoverSlots();
    }

    private void UpdateCoverSlots()
    {
        var coverUrls = Tracks?
            .OfType<HomeTrackModel>()
            .Select(track => track.CoverUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray() ?? [];

        CoverOneUrl = coverUrls.ElementAtOrDefault(0);
        CoverTwoUrl = coverUrls.ElementAtOrDefault(1);
        CoverThreeUrl = coverUrls.ElementAtOrDefault(2);
        CoverFourUrl = coverUrls.ElementAtOrDefault(3);
    }
}
