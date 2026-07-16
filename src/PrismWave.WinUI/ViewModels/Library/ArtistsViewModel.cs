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

    public ArtistsViewModel(ILibraryService libraryService, IPlaybackService playbackService)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _libraryService.LibraryChanged += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<ArtistModel> Artists { get; } = new();
    public ObservableCollection<TrackModel> SelectedTracks { get; } = new();

    public ArtistModel? SelectedArtist
    {
        get => _selectedArtist;
        private set
        {
            if (SetProperty(ref _selectedArtist, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedArtist is not null;

    [RelayCommand]
    private void SelectArtist(ArtistModel artist)
    {
        SelectedArtist = artist;
        RefreshSelectedTracks();
    }

    [RelayCommand]
    private void PlayTrack(TrackModel track)
    {
        _playbackService.Play(track, SelectedTracks.ToList());
    }

    [RelayCommand]
    private void PlaySelected()
    {
        if (SelectedTracks.Count > 0)
        {
            _playbackService.Play(SelectedTracks[0], SelectedTracks.ToList());
        }
    }

    private void Refresh()
    {
        Artists.Clear();
        foreach (var artist in _libraryService.Artists)
        {
            Artists.Add(artist);
        }

        SelectedArtist = SelectedArtist is null
            ? Artists.FirstOrDefault()
            : Artists.FirstOrDefault(artist => artist.Name == SelectedArtist.Name) ?? Artists.FirstOrDefault();
        RefreshSelectedTracks();
    }

    private void RefreshSelectedTracks()
    {
        SelectedTracks.Clear();
        if (SelectedArtist is null)
        {
            return;
        }

        foreach (var track in _libraryService.GetArtistTracks(SelectedArtist.Name))
        {
            SelectedTracks.Add(track);
        }
    }
}
