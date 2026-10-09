using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Library;

public sealed partial class AlbumsViewModel : ObservableObject
{
    private readonly ILibraryService _libraryService;
    private readonly IPlaybackService _playbackService;
    private AlbumModel? _selectedAlbum;
    private string _searchQuery = string.Empty;
    private string? _currentTrackId;

    public AlbumsViewModel(ILibraryService libraryService, IPlaybackService playbackService)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _libraryService.LibraryChanged += (_, _) => Refresh();
        _playbackService.StateChanged += (_, _) => RefreshPlaybackState();
        Refresh();
    }

    public ObservableCollection<AlbumModel> Albums { get; } = new();
    public ObservableCollection<AlbumModel> FilteredAlbums { get; } = new();
    public ObservableCollection<TrackModel> SelectedTracks { get; } = new();

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                RefreshFilter();
            }
        }
    }

    public AlbumModel? SelectedAlbum
    {
        get => _selectedAlbum;
        private set
        {
            if (SetProperty(ref _selectedAlbum, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedAlbumMetadata));
                OnPropertyChanged(nameof(SelectedAlbumTotalDuration));
            }
        }
    }

    public bool HasSelection => SelectedAlbum is not null;
    public bool IsEmpty => FilteredAlbums.Count == 0;
    public string? CurrentTrackId
    {
        get => _currentTrackId;
        private set => SetProperty(ref _currentTrackId, value);
    }

    public string SelectedAlbumMetadata => SelectedAlbum is null
        ? string.Empty
        : $"{SelectedAlbum.TrackCount} 首歌曲 · {SelectedAlbumTotalDuration}";

    public string SelectedAlbumTotalDuration => FormatTotalDuration(SelectedTracks);

    [RelayCommand]
    private void SelectAlbum(AlbumModel album)
    {
        SelectedAlbum = album;
        RefreshSelectedTracks();
    }

    [RelayCommand]
    private void PlayTrack(TrackModel track) =>
        _playbackService.Play(track, SelectedTracks.ToList());

    [RelayCommand]
    private void PlayAlbum(AlbumModel album)
    {
        var tracks = _libraryService.GetAlbumTracks(album.Id);
        if (tracks.Count > 0)
        {
            _playbackService.Play(tracks[0], tracks);
        }
    }

    [RelayCommand]
    private void PlaySelected()
    {
        if (SelectedTracks.Count > 0)
        {
            _playbackService.Play(SelectedTracks[0], SelectedTracks.ToList());
        }
    }

    [RelayCommand]
    private void AddAlbumToQueue(AlbumModel album)
    {
        foreach (var track in _libraryService.GetAlbumTracks(album.Id))
        {
            _playbackService.AddToQueue(track);
        }
    }

    [RelayCommand]
    private void AddTrackToQueue(TrackModel track) => _playbackService.AddToQueue(track);

    [RelayCommand]
    private void PlayTrackNext(TrackModel track) => _playbackService.PlayNext(track);

    [RelayCommand]
    private async Task ToggleFavoriteAsync(TrackModel track) =>
        await _libraryService.ToggleFavoriteAsync(track);

    [RelayCommand]
    private async Task ToggleAlbumFavoriteAsync(AlbumModel album)
    {
        foreach (var track in _libraryService.GetAlbumTracks(album.Id).Where(track => !track.IsFavorite))
        {
            await _libraryService.ToggleFavoriteAsync(track);
        }
    }

    private void Refresh()
    {
        var selectedId = SelectedAlbum?.Id;
        Albums.Clear();
        foreach (var album in _libraryService.Albums)
        {
            Albums.Add(album);
        }

        SelectedAlbum = selectedId is null
            ? null
            : Albums.FirstOrDefault(album => album.Id == selectedId);
        RefreshFilter();
        RefreshSelectedTracks();
    }

    private void RefreshFilter()
    {
        var query = SearchQuery.Trim();
        FilteredAlbums.Clear();
        foreach (var album in Albums.Where(album => Matches(album, query)))
        {
            FilteredAlbums.Add(album);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool Matches(AlbumModel album, string query)
    {
        if (query.Length == 0
            || album.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || album.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        return _libraryService.GetAlbumTracks(album.Id).Any(track =>
            track.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void RefreshSelectedTracks()
    {
        SelectedTracks.Clear();
        if (SelectedAlbum is not null)
        {
            foreach (var track in _libraryService.GetAlbumTracks(SelectedAlbum.Id))
            {
                SelectedTracks.Add(track);
            }
        }

        OnPropertyChanged(nameof(SelectedAlbumMetadata));
        OnPropertyChanged(nameof(SelectedAlbumTotalDuration));
    }

    private void RefreshPlaybackState()
    {
        var trackId = _playbackService.CurrentTrack?.Id;
        if (!string.Equals(CurrentTrackId, trackId, StringComparison.Ordinal))
        {
            CurrentTrackId = trackId;
        }
    }

    private static string FormatTotalDuration(IEnumerable<TrackModel> tracks)
    {
        var seconds = tracks.Sum(track => track.DurationSeconds > 0
            ? track.DurationSeconds
            : TimeSpan.TryParse(track.Duration, out var duration) ? duration.TotalSeconds : 0);
        if (seconds <= 0)
        {
            return "--:--";
        }

        var total = TimeSpan.FromSeconds(seconds);
        return total.TotalHours >= 1
            ? $"{(int)total.TotalHours} 小时 {total.Minutes} 分钟"
            : $"{Math.Max(1, total.Minutes)} 分钟";
    }
}
