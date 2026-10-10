using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PrismWave_WinUI.Models;

namespace PrismWave.Avalonia.Controls.Home;

/// <summary>
/// 首页趋势横幅（对齐 WinUI TrendingBanner）。
/// 宽度 ≥720 时展示右侧封面拼贴，否则隐藏。
/// </summary>
public sealed partial class TrendingBanner : UserControl
{
    private INotifyCollectionChanged? _observableTracks;

    private double _lastResponsiveWidth = -1;

    public TrendingBanner()
    {
        InitializeComponent();
        LayoutUpdated += OnSelfLayoutUpdated;
    }

    private void OnSelfLayoutUpdated(object? sender, EventArgs e)
    {
        var width = Bounds.Width;
        if (double.IsNaN(width) || width <= 0 || Math.Abs(width - _lastResponsiveWidth) < 0.5)
        {
            return;
        }

        _lastResponsiveWidth = width;
        var wide = width >= 720;
        CoverCollage.IsVisible = wide;
        HeroLayout.ColumnDefinitions[1].Width = new GridLength(wide ? 320 : 0);
        BannerTitle.FontSize = wide ? 44 : 36;
    }

    public static readonly StyledProperty<string> TitleProperty = StyledProperty<string>.Register<
        TrendingBanner, string>(nameof(Title), "今日趋势");

    public static readonly StyledProperty<string> SubtitleProperty = StyledProperty<string>.Register<
        TrendingBanner, string>(nameof(Subtitle), string.Empty);

    public static readonly StyledProperty<string?> BackdropUrlProperty = StyledProperty<string?>.Register<
        TrendingBanner, string?>(nameof(BackdropUrl));

    public static readonly StyledProperty<IEnumerable?> TracksProperty = StyledProperty<IEnumerable?>.Register<
        TrendingBanner, IEnumerable?>(nameof(Tracks));

    public static readonly StyledProperty<ICommand?> PlayCommandProperty = StyledProperty<ICommand?>.Register<
        TrendingBanner, ICommand?>(nameof(PlayCommand));

    public static readonly StyledProperty<string?> CoverOneUrlProperty = StyledProperty<string?>.Register<
        TrendingBanner, string?>(nameof(CoverOneUrl));

    public static readonly StyledProperty<string?> CoverTwoUrlProperty = StyledProperty<string?>.Register<
        TrendingBanner, string?>(nameof(CoverTwoUrl));

    public static readonly StyledProperty<string?> CoverThreeUrlProperty = StyledProperty<string?>.Register<
        TrendingBanner, string?>(nameof(CoverThreeUrl));

    public static readonly StyledProperty<string?> CoverFourUrlProperty = StyledProperty<string?>.Register<
        TrendingBanner, string?>(nameof(CoverFourUrl));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string? BackdropUrl
    {
        get => GetValue(BackdropUrlProperty);
        set => SetValue(BackdropUrlProperty, value);
    }

    public IEnumerable? Tracks
    {
        get => GetValue(TracksProperty);
        set => SetValue(TracksProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public string? CoverOneUrl
    {
        get => GetValue(CoverOneUrlProperty);
        private set => SetValue(CoverOneUrlProperty, value);
    }

    public string? CoverTwoUrl
    {
        get => GetValue(CoverTwoUrlProperty);
        private set => SetValue(CoverTwoUrlProperty, value);
    }

    public string? CoverThreeUrl
    {
        get => GetValue(CoverThreeUrlProperty);
        private set => SetValue(CoverThreeUrlProperty, value);
    }

    public string? CoverFourUrl
    {
        get => GetValue(CoverFourUrlProperty);
        private set => SetValue(CoverFourUrlProperty, value);
    }

    public event EventHandler? OpenRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TracksProperty)
        {
            AttachTracks(change.OldValue as IEnumerable, change.NewValue as IEnumerable);
        }
    }

    private void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AttachTracks(IEnumerable? oldTracks, IEnumerable? newTracks)
    {
        if (_observableTracks is not null)
        {
            _observableTracks.CollectionChanged -= Tracks_CollectionChanged;
        }

        _observableTracks = newTracks as INotifyCollectionChanged;
        if (_observableTracks is not null)
        {
            _observableTracks.CollectionChanged += Tracks_CollectionChanged;
        }

        UpdateCoverSlots();
    }

    private void Tracks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateCoverSlots();
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
