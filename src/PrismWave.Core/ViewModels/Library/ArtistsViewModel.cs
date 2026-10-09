using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Library;

public sealed partial class ArtistsViewModel : ObservableObject
{
    private readonly ILibraryService _libraryService;
    private readonly IPlaybackService _playbackService;
    private ArtistModel? _selectedArtist;
    private string _searchQuery = string.Empty;
    private string? _currentTrackId;

    public ArtistsViewModel(ILibraryService libraryService, IPlaybackService playbackService)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _libraryService.LibraryChanged += (_, _) => Refresh();
        _playbackService.StateChanged += (_, _) => RefreshPlaybackState();
        Refresh();
    }

    public ObservableCollection<ArtistModel> Artists { get; } = new();
    public ObservableCollection<ArtistModel> FilteredArtists { get; } = new();
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

    public ArtistModel? SelectedArtist
    {
        get => _selectedArtist;
        private set
        {
            if (SetProperty(ref _selectedArtist, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedArtistMetadata));
            }
        }
    }

    public bool HasSelection => SelectedArtist is not null;
    public bool IsEmpty => FilteredArtists.Count == 0;
    public string SelectedArtistMetadata => SelectedArtist is null
        ? string.Empty
        : $"{SelectedArtist.TrackCount} 首歌曲";
    public string? CurrentTrackId
    {
        get => _currentTrackId;
        private set => SetProperty(ref _currentTrackId, value);
    }

    [RelayCommand]
    private void SelectArtist(ArtistModel artist)
    {
        SelectedArtist = artist;
        RefreshSelectedTracks();
    }

    [RelayCommand]
    private void PlayTrack(TrackModel track) =>
        _playbackService.Play(track, SelectedTracks.ToList());

    [RelayCommand]
    private void PlaySelected()
    {
        if (SelectedTracks.Count > 0)
        {
            _playbackService.Play(SelectedTracks[0], SelectedTracks.ToList());
        }
    }

    [RelayCommand]
    private void AddTrackToQueue(TrackModel track) => _playbackService.AddToQueue(track);

    [RelayCommand]
    private void PlayTrackNext(TrackModel track) => _playbackService.PlayNext(track);

    [RelayCommand]
    private async Task ToggleFavoriteAsync(TrackModel track) =>
        await _libraryService.ToggleFavoriteAsync(track);

    private void Refresh()
    {
        var selectedName = SelectedArtist?.Name;
        Artists.Clear();
        foreach (var artist in _libraryService.Artists)
        {
            Artists.Add(artist);
        }

        SelectedArtist = selectedName is null
            ? null
            : Artists.FirstOrDefault(artist => string.Equals(
                artist.Name,
                selectedName,
                StringComparison.CurrentCultureIgnoreCase));
        RefreshFilter();
        RefreshSelectedTracks();
    }

    private void RefreshFilter()
    {
        var query = SearchQuery.Trim();
        FilteredArtists.Clear();
        foreach (var artist in Artists.Where(artist =>
                     query.Length == 0
                     || artist.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            FilteredArtists.Add(artist);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RefreshSelectedTracks()
    {
        SelectedTracks.Clear();
        if (SelectedArtist is not null)
        {
            foreach (var track in _libraryService.GetArtistTracks(SelectedArtist.Name))
            {
                SelectedTracks.Add(track);
            }
        }

        OnPropertyChanged(nameof(SelectedArtistMetadata));
    }

    private void RefreshPlaybackState()
    {
        var trackId = _playbackService.CurrentTrack?.Id;
        if (!string.Equals(CurrentTrackId, trackId, StringComparison.Ordinal))
        {
            CurrentTrackId = trackId;
        }
    }
}
