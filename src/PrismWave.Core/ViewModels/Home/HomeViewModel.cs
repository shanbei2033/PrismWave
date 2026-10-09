using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Home;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IOnlineHomeService _homeService;
    private readonly IPlaybackService _playbackService;
    private readonly ICoverService? _coverService;
    private readonly Dictionary<TrackCoverKey, string> _coverOverrides = new();
    private HomeSectionModel _topPlaylist = new("daily-top-100", "Trending", string.Empty, Array.Empty<HomeTrackModel>());
    private HomeSectionModel _selectedPlaylist = new("daily-top-100", "Trending", string.Empty, Array.Empty<HomeTrackModel>());
    private HomeSectionModel _editorialSection = EmptyEditorialSection();
    private string _generatedAt = string.Empty;
    private string _recommendationsStatus = string.Empty;
    private bool _isRefreshing;
    private AlbumModel? _selectedAlbum;
    private bool _isAlbumLoading;
    private string? _albumError;
    private string? _bannerBackdrop;

    public HomeViewModel(
        IOnlineHomeService homeService,
        IPlaybackService playbackService,
        ICoverService? coverService = null)
    {
        _homeService = homeService;
        _playbackService = playbackService;
        _coverService = coverService;
        _homeService.HomeChanged += (_, _) => RefreshFromService();
        _playbackService.StateChanged += (_, _) => SynchronizeCurrentTrackCover();
        if (_coverService is not null)
        {
            _coverService.CoverChanged += CoverService_CoverChanged;
        }

        RefreshFromService();
    }

    public HomeSectionModel TopPlaylist
    {
        get => _topPlaylist;
        private set => SetProperty(ref _topPlaylist, value);
    }

    public HomeSectionModel EditorialSection
    {
        get => _editorialSection;
        private set => SetProperty(ref _editorialSection, value);
    }

    public HomeSectionModel SelectedPlaylist
    {
        get => _selectedPlaylist;
        private set => SetProperty(ref _selectedPlaylist, value);
    }

    public ObservableCollection<HomeSectionModel> Sections { get; } = new();
    public ObservableCollection<HomeTrackModel> GlobalTrendingTracks { get; } = new();
    public ObservableCollection<HomeSectionModel> ChannelSections { get; } = new();
    public ObservableCollection<HomeSectionModel> GenreSections { get; } = new();
    public ObservableCollection<AlbumModel> Albums { get; } = new();
    public ObservableCollection<HomeTrackModel> BannerTracks { get; } = new();
    public string? BannerBackdrop
    {
        get => _bannerBackdrop;
        private set => SetProperty(ref _bannerBackdrop, value);
    }
    public string GeneratedAt
    {
        get => _generatedAt;
        private set => SetProperty(ref _generatedAt, value);
    }

    public string RecommendationsStatus
    {
        get => _recommendationsStatus;
        private set => SetProperty(ref _recommendationsStatus, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public AlbumModel? SelectedAlbum
    {
        get => _selectedAlbum;
        private set => SetProperty(ref _selectedAlbum, value);
    }

    public ObservableCollection<HomeTrackModel> SelectedAlbumTracks { get; } = new();
    public bool IsAlbumLoading
    {
        get => _isAlbumLoading;
        private set => SetProperty(ref _isAlbumLoading, value);
    }

    public string? AlbumError
    {
        get => _albumError;
        private set
        {
            if (SetProperty(ref _albumError, value))
            {
                OnPropertyChanged(nameof(HasAlbumError));
            }
        }
    }

    public bool HasAlbumError => !string.IsNullOrWhiteSpace(AlbumError);

    [RelayCommand]
    private void PlayHomeTrack(HomeTrackModel track)
    {
        var section = SelectedPlaylist.Tracks.Any(item => ReferenceEquals(item, track))
            ? SelectedPlaylist
            : Sections.FirstOrDefault(candidate =>
                candidate.Tracks.Any(item => ReferenceEquals(item, track)))
              ?? (SelectedPlaylist.Tracks.Contains(track) ? SelectedPlaylist : null)
              ?? Sections.FirstOrDefault(candidate => candidate.Tracks.Contains(track));
        var source = (section?.Tracks ?? TopPlaylist.Tracks).ToList();
        var selectedIndex = source.FindIndex(item =>
            ReferenceEquals(item, track) || item.Equals(track));
        if (selectedIndex < 0)
        {
            source.Insert(0, track);
            selectedIndex = 0;
        }

        var idPrefix = section is null ? "top" : $"section-{section.Id}";
        var queue = source
            .Select((item, index) => ToTrack(item, $"{idPrefix}-{index}"))
            .ToList();
        var selectedTrack = queue[selectedIndex];
        if (_playbackService.CurrentTrack?.Id == selectedTrack.Id)
        {
            if (_playbackService.IsPlaying)
            {
                return;
            }

            if (_playbackService.Status == PlaybackStatus.Paused)
            {
                _playbackService.TogglePlayPause();
                return;
            }
        }

        _playbackService.Play(selectedTrack, queue);
    }

    [RelayCommand]
    private void PlayTopPlaylist()
    {
        var queue = TopPlaylist.Tracks
            .Select((track, index) => ToTrack(track, $"top-{index}"))
            .ToList();

        if (queue.Count > 0)
        {
            _playbackService.Play(queue[0], queue);
        }
    }

    [RelayCommand]
    private void SelectHomeSection(HomeSectionModel section)
    {
        SelectedPlaylist = section;
    }

    [RelayCommand]
    private void PlaySelectedPlaylist()
    {
        var queue = SelectedPlaylist.Tracks
            .Select((track, index) => ToTrack(track, $"section-{SelectedPlaylist.Id}-{index}"))
            .ToList();

        if (queue.Count > 0)
        {
            _playbackService.Play(queue[0], queue);
        }
    }

    [RelayCommand]
    private async Task RefreshHomeAsync()
    {
        await _homeService.RefreshAsync(force: true);
    }

    [RelayCommand]
    private async Task SelectAlbumAsync(AlbumModel album)
    {
        SelectedAlbum = album;
        SelectedAlbumTracks.Clear();
        IsAlbumLoading = true;
        AlbumError = null;
        var tracks = await _homeService.LoadAlbumTracksAsync(album.Id);
        foreach (var track in tracks)
        {
            SelectedAlbumTracks.Add(ApplyCoverOverride(track));
        }

        IsAlbumLoading = false;
        if (tracks.Count == 0)
        {
            AlbumError = "No album tracks are currently available.";
        }
    }

    [RelayCommand]
    private void PlaySelectedAlbumTrack(HomeTrackModel track)
    {
        var queue = SelectedAlbumTracks
            .Select((item, index) => ToTrack(item, $"album-{SelectedAlbum?.Id}-{index}"))
            .ToList();
        _playbackService.Play(ToTrack(track, $"album-picked-{SelectedAlbum?.Id}-{track.Title}"), queue);
    }

    [RelayCommand]
    private void PlaySelectedAlbum()
    {
        var queue = SelectedAlbumTracks
            .Select((track, index) => ToTrack(track, $"album-{SelectedAlbum?.Id}-{index}"))
            .ToList();
        if (queue.Count > 0)
        {
            _playbackService.Play(queue[0], queue);
        }
    }

    private void RefreshFromService()
    {
        var selectedPlaylistId = SelectedPlaylist.Id;
        TopPlaylist = ApplyCoverOverrides(_homeService.TopPlaylist);
        GeneratedAt = $"Generated {_homeService.GeneratedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC";
        IsRefreshing = _homeService.IsRefreshing;
        RecommendationsStatus = _homeService.RecommendationsUnavailable
            ? $"Recommendations unavailable{FormatError(_homeService.Error)}"
            : _homeService.RecommendationsPendingGeneration
                ? $"Today is not generated yet · {_homeService.SourceDescription}"
                : $"Schema 8 · {_homeService.SourceDescription}";

        Sections.Clear();
        foreach (var section in _homeService.Sections)
        {
            Sections.Add(ApplyCoverOverrides(section));
        }
        SelectedPlaylist = selectedPlaylistId.Equals(TopPlaylist.Id, StringComparison.OrdinalIgnoreCase)
            ? TopPlaylist
            : Sections.FirstOrDefault(section =>
                section.Id.Equals(selectedPlaylistId, StringComparison.OrdinalIgnoreCase)) ?? TopPlaylist;

        Albums.Clear();
        foreach (var album in _homeService.Albums)
        {
            Albums.Add(album);
        }

        RefreshDerivedCollections();
    }

    private void RefreshDerivedCollections()
    {
        var globalTrending = Sections.FirstOrDefault(section =>
            section.Id.Equals("global-hot", StringComparison.OrdinalIgnoreCase))
            ?? Sections.FirstOrDefault();

        var editorial = Sections.FirstOrDefault(section =>
            section.Id.Equals("streamable-now", StringComparison.OrdinalIgnoreCase))
            ?? Sections.FirstOrDefault(section =>
                !ReferenceEquals(section, globalTrending))
            ?? EmptyEditorialSection();
        EditorialSection = editorial;

        GlobalTrendingTracks.Clear();
        if (globalTrending is not null)
        {
            foreach (var track in globalTrending.Tracks.Take(10))
            {
                GlobalTrendingTracks.Add(track);
            }
        }

        ChannelSections.Clear();
        foreach (var section in Sections.Where(section =>
                     section.Id.Equals("world-charts", StringComparison.OrdinalIgnoreCase) ||
                     section.Id.Equals("audius-trending", StringComparison.OrdinalIgnoreCase)))
        {
            ChannelSections.Add(section);
        }

        GenreSections.Clear();
        foreach (var section in Sections.Where(section =>
                     section.Id.StartsWith("style-", StringComparison.OrdinalIgnoreCase)))
        {
            GenreSections.Add(section);
        }

        BannerTracks.Clear();
        var bannerCandidates = TopPlaylist.Tracks
            .Concat(Sections.SelectMany(section => section.Tracks))
            .Where(track => !string.IsNullOrWhiteSpace(track.CoverUrl))
            .DistinctBy(track => track.CoverUrl)
            .Take(4);
        foreach (var track in bannerCandidates)
        {
            BannerTracks.Add(track);
        }

        BannerBackdrop = BannerTracks.LastOrDefault()?.CoverUrl;
    }

    private void SynchronizeCurrentTrackCover()
    {
        var currentTrack = _playbackService.CurrentTrack;
        if (currentTrack is not { IsRemote: true }
            || !TryCreateCoverKey(currentTrack.Title, currentTrack.Artist, out var key))
        {
            return;
        }

        var coverPath = _coverService?.ResolveCoverPath(currentTrack) ?? currentTrack.CoverPath;
        if (string.IsNullOrWhiteSpace(coverPath)
            || (_coverOverrides.TryGetValue(key, out var existing)
                && string.Equals(existing, coverPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _coverOverrides[key] = coverPath;
        ApplyCoverOverridesToCurrentCatalog();
    }

    private void ApplyCoverOverridesToCurrentCatalog()
    {
        var selectedPlaylistId = SelectedPlaylist.Id;
        TopPlaylist = ApplyCoverOverrides(TopPlaylist);
        for (var index = 0; index < Sections.Count; index++)
        {
            Sections[index] = ApplyCoverOverrides(Sections[index]);
        }

        SelectedPlaylist = selectedPlaylistId.Equals(TopPlaylist.Id, StringComparison.OrdinalIgnoreCase)
            ? TopPlaylist
            : Sections.FirstOrDefault(section =>
                section.Id.Equals(selectedPlaylistId, StringComparison.OrdinalIgnoreCase)) ?? TopPlaylist;

        for (var index = 0; index < SelectedAlbumTracks.Count; index++)
        {
            SelectedAlbumTracks[index] = ApplyCoverOverride(SelectedAlbumTracks[index]);
        }

        RefreshDerivedCollections();
    }

    private HomeSectionModel ApplyCoverOverrides(HomeSectionModel section)
    {
        var tracks = section.Tracks.Select(ApplyCoverOverride).ToList();
        return section with { Tracks = tracks };
    }

    private HomeTrackModel ApplyCoverOverride(HomeTrackModel track)
    {
        return TryCreateCoverKey(track.Title, track.Artist, out var key)
               && _coverOverrides.TryGetValue(key, out var coverPath)
               && !string.Equals(track.CoverUrl, coverPath, StringComparison.OrdinalIgnoreCase)
            ? track with { CoverUrl = coverPath }
            : track;
    }

    private void CoverService_CoverChanged(object? sender, CoverChangedEventArgs e)
    {
        if (TryCreateCoverKey(e.Title, e.Artist, out var changedKey))
        {
            _coverOverrides[changedKey] = e.CoverPath;
            ApplyCoverOverridesToCurrentCatalog();
            return;
        }

        var currentTrack = _playbackService.CurrentTrack;
        if (currentTrack is not null
            && (string.Equals(currentTrack.Id, e.TrackId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(currentTrack.Path, e.TrackPath, StringComparison.OrdinalIgnoreCase)))
        {
            SynchronizeCurrentTrackCover();
        }
    }

    private static bool TryCreateCoverKey(
        string title,
        string artist,
        out TrackCoverKey key)
    {
        var normalizedTitle = NormalizeIdentityText(title);
        var normalizedArtist = NormalizeIdentityText(artist);
        key = new TrackCoverKey(normalizedTitle, normalizedArtist);
        return normalizedTitle.Length > 0 && normalizedArtist.Length > 0;
    }

    private static string NormalizeIdentityText(string value)
    {
        return string.Join(
                ' ',
                value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    private readonly record struct TrackCoverKey(string Title, string Artist);

    private static string FormatError(string? error)
    {
        return string.IsNullOrWhiteSpace(error) ? string.Empty : $": {error}";
    }

    private static HomeSectionModel EmptyEditorialSection()
    {
        return new HomeSectionModel(
            "streamable-now",
            "可直接播放",
            string.Empty,
            Array.Empty<HomeTrackModel>());
    }

    private static TrackModel ToTrack(HomeTrackModel track, string id)
    {
        return new TrackModel(
            id,
            $"online://{track.Provider}/{Uri.EscapeDataString(track.ProviderTrackId ?? track.Title)}",
            track.Title,
            track.Artist,
            track.Album,
            track.Duration,
            track.CoverUrl,
            true,
            track.Provider,
            track.AudioUrl);
    }
}
