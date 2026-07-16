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

    public AlbumsViewModel(ILibraryService libraryService, IPlaybackService playbackService)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _libraryService.LibraryChanged += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<AlbumModel> Albums { get; } = new();
    public ObservableCollection<TrackModel> SelectedTracks { get; } = new();

    public AlbumModel? SelectedAlbum
    {
        get => _selectedAlbum;
        private set
        {
            if (SetProperty(ref _selectedAlbum, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedAlbum is not null;

    [RelayCommand]
    private void SelectAlbum(AlbumModel album)
    {
        SelectedAlbum = album;
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
        Albums.Clear();
        foreach (var album in _libraryService.Albums)
        {
            Albums.Add(album);
        }

        SelectedAlbum = SelectedAlbum is null
            ? Albums.FirstOrDefault()
            : Albums.FirstOrDefault(album => album.Id == SelectedAlbum.Id) ?? Albums.FirstOrDefault();
        RefreshSelectedTracks();
    }

    private void RefreshSelectedTracks()
    {
        SelectedTracks.Clear();
        if (SelectedAlbum is null)
        {
            return;
        }

        foreach (var track in _libraryService.GetAlbumTracks(SelectedAlbum.Id))
        {
            SelectedTracks.Add(track);
        }
    }
}
