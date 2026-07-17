using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Library;

public sealed partial class FavoritesViewModel : ObservableObject
{
    private readonly ILibraryService _libraryService;
    private readonly IPlaybackService _playbackService;
    private readonly List<TrackModel> _favorites = [];
    private string _searchQuery = string.Empty;
    private string? _currentTrackId;

    public FavoritesViewModel(ILibraryService libraryService, IPlaybackService playbackService)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _libraryService.LibraryChanged += (_, _) => RefreshFavorites();
        _playbackService.StateChanged += (_, _) => RefreshPlaybackIdentity();
        RefreshFavorites();
        RefreshPlaybackIdentity();
    }

    public ObservableCollection<TrackModel> VisibleTracks { get; } = [];

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

    public string? CurrentTrackId
    {
        get => _currentTrackId;
        private set => SetProperty(ref _currentTrackId, value);
    }

    public bool IsEmpty => VisibleTracks.Count == 0;

    [RelayCommand]
    private void PlayTrack(TrackModel track) =>
        _playbackService.Play(track, VisibleTracks.ToList());

    [RelayCommand]
    private void AddTrackToQueue(TrackModel track) => _playbackService.AddToQueue(track);

    [RelayCommand]
    private void PlayTrackNext(TrackModel track) => _playbackService.PlayNext(track);

    [RelayCommand]
    private async Task ToggleFavoriteAsync(TrackModel track)
    {
        await _libraryService.ToggleFavoriteAsync(track);
        _favorites.RemoveAll(item => string.Equals(item.Id, track.Id, StringComparison.Ordinal));
        RemoveVisibleTrack(track.Id);
        OnPropertyChanged(nameof(IsEmpty));
    }

    public Task PersistOrderAsync() =>
        _libraryService.PersistFavoriteOrderAsync(VisibleTracks.ToList());

    private void RefreshFavorites()
    {
        _favorites.Clear();
        _favorites.AddRange(_libraryService.Favorites);
        RefreshVisibleTracks();
    }

    private void RefreshVisibleTracks()
    {
        var query = SearchQuery.Trim();
        var target = _favorites.Where(track => Matches(track, query)).ToList();
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
    }

    private void RefreshPlaybackIdentity()
    {
        var trackId = _playbackService.CurrentTrack?.Id;
        if (!string.Equals(CurrentTrackId, trackId, StringComparison.Ordinal))
        {
            CurrentTrackId = trackId;
        }
    }

    private void RemoveVisibleTrack(string trackId)
    {
        var index = IndexOfVisibleTrack(trackId, 0);
        if (index >= 0)
        {
            VisibleTracks.RemoveAt(index);
        }
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

    private static bool Matches(TrackModel track, string query) =>
        query.Length == 0
        || track.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || track.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || track.Album.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
