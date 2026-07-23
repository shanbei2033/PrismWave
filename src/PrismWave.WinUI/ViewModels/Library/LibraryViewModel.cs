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
    private string? _currentTrackId;

    public LibraryViewModel(
        ILibraryService libraryService,
        IPlaybackService playbackService,
        LibraryFolderManagerViewModel libraryFolders)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        LibraryFolders = libraryFolders;
        _libraryService.LibraryChanged += (_, _) => RefreshAll();
        _playbackService.StateChanged += (_, _) => RefreshCurrentTrack();
        RefreshVisibleTracks();
        RefreshCurrentTrack();
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
    public LibraryFolderManagerViewModel LibraryFolders { get; }
    public string? CurrentTrackId
    {
        get => _currentTrackId;
        private set => SetProperty(ref _currentTrackId, value);
    }
    public int TrackCount => _libraryService.Tracks.Count;
    public int FolderCount => _libraryService.Folders.Count;
    public int FavoriteCount => _libraryService.Favorites.Count;
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
    public async Task ToggleFavoriteAsync(TrackModel track)
    {
        await _libraryService.ToggleFavoriteAsync(track);
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
        var query = SearchQuery.Trim();
        var target = (string.IsNullOrWhiteSpace(query)
                ? _libraryService.Tracks
                : _libraryService.Tracks.Where(track =>
                    track.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || track.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || track.Album.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || track.Path.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var targetIds = target.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);

        for (var index = VisibleTracks.Count - 1; index >= 0; index--)
        {
            if (!targetIds.Contains(VisibleTracks[index].Id))
            {
                VisibleTracks.RemoveAt(index);
            }
        }

        for (var targetIndex = 0; targetIndex < target.Count; targetIndex++)
        {
            var targetTrack = target[targetIndex];
            if (targetIndex < VisibleTracks.Count
                && string.Equals(VisibleTracks[targetIndex].Id, targetTrack.Id, StringComparison.Ordinal))
            {
                if (!ReferenceEquals(VisibleTracks[targetIndex], targetTrack))
                {
                    VisibleTracks[targetIndex] = targetTrack;
                }

                continue;
            }

            var existingIndex = IndexOfVisibleTrack(targetTrack.Id, targetIndex + 1);
            if (existingIndex >= 0)
            {
                VisibleTracks.Move(existingIndex, targetIndex);
                if (!ReferenceEquals(VisibleTracks[targetIndex], targetTrack))
                {
                    VisibleTracks[targetIndex] = targetTrack;
                }
            }
            else
            {
                VisibleTracks.Insert(targetIndex, targetTrack);
            }
        }

        while (VisibleTracks.Count > target.Count)
        {
            VisibleTracks.RemoveAt(VisibleTracks.Count - 1);
        }

        OnPropertyChanged(nameof(IsEmpty));
        RefreshDerivedState();
    }

    private int IndexOfVisibleTrack(string trackId, int startIndex)
    {
        for (var index = Math.Max(0, startIndex); index < VisibleTracks.Count; index++)
        {
            if (string.Equals(VisibleTracks[index].Id, trackId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void RefreshAll()
    {
        RefreshVisibleTracks();
        RefreshDerivedState();
    }

    private void RefreshDerivedState()
    {
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(FolderCount));
        OnPropertyChanged(nameof(FavoriteCount));
    }

    private void RefreshCurrentTrack() => CurrentTrackId = _playbackService.CurrentTrack?.Id;
}
