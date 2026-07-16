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

    public FavoritesViewModel(ILibraryService libraryService, IPlaybackService playbackService)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _libraryService.LibraryChanged += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<TrackModel> Tracks { get; } = new();
    public bool IsEmpty => Tracks.Count == 0;

    [RelayCommand]
    private void PlayTrack(TrackModel track)
    {
        _playbackService.Play(track, Tracks.ToList());
    }

    [RelayCommand]
    private void PlayAll()
    {
        if (Tracks.Count > 0)
        {
            _playbackService.Play(Tracks[0], Tracks.ToList());
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(TrackModel track)
    {
        await _libraryService.ToggleFavoriteAsync(track);
    }

    public async Task PersistOrderAsync()
    {
        await _libraryService.PersistFavoriteOrderAsync(Tracks.ToList());
    }

    private void Refresh()
    {
        Tracks.Clear();
        foreach (var track in _libraryService.Favorites)
        {
            Tracks.Add(track);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
