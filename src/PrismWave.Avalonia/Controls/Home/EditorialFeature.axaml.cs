using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PrismWave_WinUI.Models;

namespace PrismWave.Avalonia.Controls.Home;

/// <summary>
/// 首页编辑精选（对齐 WinUI EditorialFeature）。
/// 宽度 ≥760 媒体左/文案右，否则媒体上/文案下。
/// </summary>
public sealed partial class EditorialFeature : UserControl
{
    private double _lastResponsiveWidth = -1;

    public EditorialFeature()
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
        ApplyResponsive(width);
    }

    private void ApplyResponsive(double width)
    {
        var wide = width >= 760;
        LayoutRoot.ColumnDefinitions[0].Width = wide ? new GridLength(300) : new GridLength(1, GridUnitType.Star);
        LayoutRoot.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        LayoutRoot.ColumnSpacing = wide ? 32 : 0;
        Grid.SetRow(FeatureMedia, 0);
        Grid.SetColumn(FeatureMedia, 0);
        FeatureMedia.Height = wide ? 220 : 200;
        Grid.SetRow(FeatureCopy, wide ? 0 : 1);
        Grid.SetColumn(FeatureCopy, wide ? 1 : 0);
    }

    public static readonly StyledProperty<HomeSectionModel?> SectionProperty = StyledProperty<HomeSectionModel?>.Register<
        EditorialFeature, HomeSectionModel?>(nameof(Section));

    public static readonly StyledProperty<ICommand?> PlayCommandProperty = StyledProperty<ICommand?>.Register<
        EditorialFeature, ICommand?>(nameof(PlayCommand));

    public static readonly StyledProperty<string> FeatureTitleProperty = StyledProperty<string>.Register<
        EditorialFeature, string>(nameof(FeatureTitle), "Play Now");

    public static readonly StyledProperty<string> TrackCountLabelProperty = StyledProperty<string>.Register<
        EditorialFeature, string>(nameof(TrackCountLabel), "TOP20");

    public static readonly StyledProperty<string> FeaturedTrackTitleProperty = StyledProperty<string>.Register<
        EditorialFeature, string>(nameof(FeaturedTrackTitle), string.Empty);

    public static readonly StyledProperty<string> FeaturedTrackArtistProperty = StyledProperty<string>.Register<
        EditorialFeature, string>(nameof(FeaturedTrackArtist), string.Empty);

    public static readonly StyledProperty<string?> FeaturedCoverUrlProperty = StyledProperty<string?>.Register<
        EditorialFeature, string?>(nameof(FeaturedCoverUrl));

    public static readonly StyledProperty<HomeTrackModel?> FeaturedTrackProperty = StyledProperty<HomeTrackModel?>.Register<
        EditorialFeature, HomeTrackModel?>(nameof(FeaturedTrack));

    public HomeSectionModel? Section
    {
        get => GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public string FeatureTitle
    {
        get => GetValue(FeatureTitleProperty);
        private set => SetValue(FeatureTitleProperty, value);
    }

    public string TrackCountLabel
    {
        get => GetValue(TrackCountLabelProperty);
        private set => SetValue(TrackCountLabelProperty, value);
    }

    public string FeaturedTrackTitle
    {
        get => GetValue(FeaturedTrackTitleProperty);
        private set => SetValue(FeaturedTrackTitleProperty, value);
    }

    public string FeaturedTrackArtist
    {
        get => GetValue(FeaturedTrackArtistProperty);
        private set => SetValue(FeaturedTrackArtistProperty, value);
    }

    public string? FeaturedCoverUrl
    {
        get => GetValue(FeaturedCoverUrlProperty);
        private set => SetValue(FeaturedCoverUrlProperty, value);
    }

    public HomeTrackModel? FeaturedTrack
    {
        get => GetValue(FeaturedTrackProperty);
        private set => SetValue(FeaturedTrackProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SectionProperty)
        {
            UpdateFeature();
        }
    }

    private void UpdateFeature()
    {
        var section = Section;
        if (section is null)
        {
            FeatureTitle = "Play Now";
            TrackCountLabel = "TOP20";
            FeaturedTrackTitle = "暂无推荐";
            FeaturedTrackArtist = string.Empty;
            FeaturedCoverUrl = null;
            FeaturedTrack = null;
            return;
        }

        var featured = section.Tracks.FirstOrDefault();
        FeaturedTrack = featured;
        FeatureTitle = "Play Now";
        TrackCountLabel = "TOP20";
        FeaturedTrackTitle = featured?.Title ?? "暂无推荐";
        FeaturedTrackArtist = featured?.Artist ?? string.Empty;
        FeaturedCoverUrl = section.Tracks
            .Select(track => track.CoverUrl)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }
}
