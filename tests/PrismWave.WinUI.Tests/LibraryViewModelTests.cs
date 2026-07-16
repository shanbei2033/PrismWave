using System.ComponentModel;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Library;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class LibraryViewModelTests
{
    [Fact]
    public void PlaybackIdentity_UpdatesCurrentTrackWithoutRebuildingVisibleTracks()
    {
        var track = new TrackModel("track-1", @"C:\Music\song.flac", "Song", "Artist", "Album", "03:00", null);
        var library = new FakeLibraryService([track]);
        var playback = new FakePlaybackService();
        var folders = new LibraryFolderManagerViewModel(library, new FakePicker());
        var viewModel = new LibraryViewModel(library, playback, folders);
        var currentTrackId = typeof(LibraryViewModel).GetProperty("CurrentTrackId");
        Assert.NotNull(currentTrackId);

        var changes = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "CurrentTrackId")
            {
                changes++;
            }
        };

        playback.SetCurrentTrack(track);
        playback.RaiseStateChanged();

        Assert.Equal(track.Id, currentTrackId!.GetValue(viewModel));
        Assert.Equal(1, changes);
        Assert.Single(viewModel.VisibleTracks);
    }

    private sealed class FakePicker : IMusicFolderPicker
    {
        public Task<MusicFolderPickResult> PickAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(MusicFolderPickResult.Canceled());
    }

    private sealed class FakeLibraryService(IReadOnlyList<TrackModel> tracks) : ILibraryService
    {
        public IReadOnlyList<TrackModel> Tracks { get; } = tracks;
        public IReadOnlyList<string> Folders => [];
        public IReadOnlyList<AlbumModel> Albums => [];
        public IReadOnlyList<ArtistModel> Artists => [];
        public IReadOnlyList<TrackModel> Favorites => [];
        public IReadOnlyList<LibraryFolderStatus> FolderStatuses => [];
        public LibraryScanProgress ScanProgress => LibraryScanProgress.Idle;
        public bool IsScanning => false;
        public string? Error => null;
        public event EventHandler? LibraryChanged
        {
            add { }
            remove { }
        }
        public Task AddFolderAsync(string folder) => Task.CompletedTask;
        public Task AddFolderAsync(string folder, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFolderAsync(string folder) => Task.CompletedTask;
        public Task RemoveFolderAsync(string folder, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RescanAsync() => Task.CompletedTask;
        public Task RescanAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(TrackModel track) => Task.CompletedTask;
        public Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile) => Task.CompletedTask;
        public IReadOnlyList<TrackModel> GetAlbumTracks(string albumId) => [];
        public IReadOnlyList<TrackModel> GetArtistTracks(string artistName) => [];
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public TrackModel? CurrentTrack { get; private set; }
        public IReadOnlyList<TrackModel> Queue => [];
        public PlaybackMode Mode => PlaybackMode.Loop;
        public PlaybackStatus Status => PlaybackStatus.Playing;
        public double Volume => 1;
        public double PositionSeconds => 0;
        public double DurationSeconds => 0;
        public bool IsLoading => false;
        public bool IsPlaying => true;
        public string? Error => null;
        public IReadOnlyList<WindowsDsdDeviceModel> WindowsDsdDevices => [];
        public bool WindowsDsdAvailable => false;
        public string? WindowsDsdOutputModeLabel => null;
        public string? WindowsDsdActiveDeviceName => null;
        public string? WindowsDsdFallbackReason => null;
        public event EventHandler? StateChanged;

        public void SetCurrentTrack(TrackModel track) => CurrentTrack = track;
        public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
        public void Play(TrackModel track, IReadOnlyList<TrackModel>? queue = null) { }
        public void Stop() { }
        public void TogglePlayPause() { }
        public void Next() { }
        public void Previous() { }
        public void CycleMode() { }
        public void SetVolume(double volume) { }
        public void Seek(double seconds) { }
        public void PlayFromQueue(TrackModel track) { }
        public void ReorderQueue(IReadOnlyList<TrackModel> tracks) { }
        public void RemoveFromQueue(TrackModel track) { }
        public void ClearQueue() { }
        public Task RefreshWindowsDsdDevicesAsync() => Task.CompletedTask;
    }
}
