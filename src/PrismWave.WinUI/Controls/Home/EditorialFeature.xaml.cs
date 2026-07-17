using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Controls.Home;

public sealed partial class EditorialFeature : UserControl
{
    private HomeTrackModel[] _featureCandidates = [];
    private int _featureCandidateIndex;

    public EditorialFeature()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(
        nameof(Section),
        typeof(HomeSectionModel),
        typeof(EditorialFeature),
        new PropertyMetadata(null, OnSectionChanged));

    public static readonly DependencyProperty PlayCommandProperty = DependencyProperty.Register(
        nameof(PlayCommand),
        typeof(ICommand),
        typeof(EditorialFeature),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FeatureTitleProperty = DependencyProperty.Register(
        nameof(FeatureTitle), typeof(string), typeof(EditorialFeature), new PropertyMetadata("Play Now"));

    public static readonly DependencyProperty TrackCountLabelProperty = DependencyProperty.Register(
        nameof(TrackCountLabel), typeof(string), typeof(EditorialFeature), new PropertyMetadata("TOP20"));

    public static readonly DependencyProperty FeaturedTrackTitleProperty = DependencyProperty.Register(
        nameof(FeaturedTrackTitle), typeof(string), typeof(EditorialFeature), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FeaturedTrackArtistProperty = DependencyProperty.Register(
        nameof(FeaturedTrackArtist), typeof(string), typeof(EditorialFeature), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FeaturedCoverUrlProperty = DependencyProperty.Register(
        nameof(FeaturedCoverUrl), typeof(string), typeof(EditorialFeature), new PropertyMetadata(null));

    public static readonly DependencyProperty FeaturedTrackProperty = DependencyProperty.Register(
        nameof(FeaturedTrack), typeof(HomeTrackModel), typeof(EditorialFeature), new PropertyMetadata(null));

    public HomeSectionModel? Section
    {
        get => (HomeSectionModel?)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => (ICommand?)GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public string FeatureTitle
    {
        get => (string)GetValue(FeatureTitleProperty);
        private set => SetValue(FeatureTitleProperty, value);
    }

    public string TrackCountLabel
    {
        get => (string)GetValue(TrackCountLabelProperty);
        private set => SetValue(TrackCountLabelProperty, value);
    }

    public string FeaturedTrackTitle
    {
        get => (string)GetValue(FeaturedTrackTitleProperty);
        private set => SetValue(FeaturedTrackTitleProperty, value);
    }

    public string FeaturedTrackArtist
    {
        get => (string)GetValue(FeaturedTrackArtistProperty);
        private set => SetValue(FeaturedTrackArtistProperty, value);
    }

    public string? FeaturedCoverUrl
    {
        get => (string?)GetValue(FeaturedCoverUrlProperty);
        private set => SetValue(FeaturedCoverUrlProperty, value);
    }

    public HomeTrackModel? FeaturedTrack
    {
        get => (HomeTrackModel?)GetValue(FeaturedTrackProperty);
        private set => SetValue(FeaturedTrackProperty, value);
    }

    private static void OnSectionChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        ((EditorialFeature)dependencyObject).UpdateFeature();
    }

    private void UpdateFeature()
    {
        if (Section is null)
        {
            _featureCandidates = [];
            _featureCandidateIndex = 0;
            FeatureTitle = "Play Now";
            TrackCountLabel = "TOP20";
            FeaturedTrackTitle = "暂无推荐";
            FeaturedTrackArtist = string.Empty;
            FeaturedCoverUrl = null;
            FeaturedTrack = null;
            return;
        }

        FeaturedTrack = Section.Tracks.FirstOrDefault();
        _featureCandidates = Section.Tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.CoverUrl))
            .DistinctBy(track => track.CoverUrl, StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        _featureCandidateIndex = 0;
        FeatureTitle = "Play Now";
        TrackCountLabel = "TOP20";
        if (_featureCandidates.Length > 0)
        {
            ApplyFeaturedCandidate(_featureCandidateIndex);
        }
        else
        {
            FeaturedTrackTitle = FeaturedTrack?.Title ?? "暂无推荐";
            FeaturedTrackArtist = FeaturedTrack?.Artist ?? string.Empty;
            FeaturedCoverUrl = null;
        }
    }

    private void EditorialArtwork_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _featureCandidateIndex++;
        if (_featureCandidateIndex < _featureCandidates.Length)
        {
            ApplyFeaturedCandidate(_featureCandidateIndex);
        }
        else
        {
            FeaturedCoverUrl = null;
        }
    }

    private void ApplyFeaturedCandidate(int index)
    {
        FeaturedTrack = _featureCandidates[index];
        FeaturedTrackTitle = FeaturedTrack.Title;
        FeaturedTrackArtist = FeaturedTrack.Artist;
        FeaturedCoverUrl = FeaturedTrack.CoverUrl;
    }

    private void EditorialFeature_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var state = e.NewSize.Width >= 760 ? "Wide" : "Compact";
        VisualStateManager.GoToState(this, state, useTransitions: false);
    }
}
