using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Library;

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly ILibraryService _libraryService;
    private readonly IPlaybackService _playbackService;
    private string _searchQuery = string.Empty;
    private bool _isScanning;
    private string? _error;

    public LibraryViewModel(ILibraryService libraryService, IPlaybackService playbackService)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _libraryService.LibraryChanged += (_, _) => RefreshAll();
        RefreshVisibleTracks();
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                RefreshVisibleTracks();
            }
        }
    }

    public ObservableCollection<TrackModel> VisibleTracks { get; } = new();
    public ObservableCollection<string> Folders { get; } = new();
    public int TrackCount => _libraryService.Tracks.Count;
    public int FolderCount => _libraryService.Folders.Count;
    public int FavoriteCount => _libraryService.Favorites.Count;
    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsEmpty => VisibleTracks.Count == 0;

    [RelayCommand]
    public void PlayTrack(TrackModel track)
    {
        _playbackService.Play(track, VisibleTracks.ToList());
    }

    [RelayCommand]
    public void PlayAll()
    {
        if (VisibleTracks.Count > 0)
        {
            _playbackService.Play(VisibleTracks[0], VisibleTracks.ToList());
        }
    }

    [RelayCommand]
    public async Task RescanAsync()
    {
        await _libraryService.RescanAsync();
    }

    [RelayCommand]
    public async Task RemoveFolderAsync(string folder)
    {
        await _libraryService.RemoveFolderAsync(folder);
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync(TrackModel track)
    {
        await _libraryService.ToggleFavoriteAsync(track);
    }

    public async Task AddFolderAsync(string folder)
    {
        await _libraryService.AddFolderAsync(folder);
    }

    public async Task PersistVisibleOrderAsync()
    {
        await _libraryService.PersistTrackOrderAsync(VisibleTracks.ToList());
    }

    public async Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile)
    {
        await _libraryService.RemoveTrackAsync(track, deleteSourceFile);
    }

    private void RefreshVisibleTracks()
    {
        VisibleTracks.Clear();
        var query = SearchQuery.Trim();
        var tracks = string.IsNullOrWhiteSpace(query)
            ? _libraryService.Tracks
            : _libraryService.Tracks.Where(track =>
                track.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || track.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
                || track.Album.Contains(query, StringComparison.OrdinalIgnoreCase)
                || track.Path.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var track in tracks)
        {
            VisibleTracks.Add(track);
        }

        OnPropertyChanged(nameof(IsEmpty));
        RefreshDerivedState();
    }

    private void RefreshAll()
    {
        RefreshVisibleTracks();
        RefreshDerivedState();
    }

    private void RefreshDerivedState()
    {
        Folders.Clear();
        foreach (var folder in _libraryService.Folders)
        {
            Folders.Add(folder);
        }

        IsScanning = _libraryService.IsScanning;
        Error = _libraryService.Error;
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(FolderCount));
        OnPropertyChanged(nameof(FavoriteCount));
    }
}
