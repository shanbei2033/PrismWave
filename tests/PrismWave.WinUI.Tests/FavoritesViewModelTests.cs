using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Library;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class FavoritesViewModelTests
{
    [Fact]
    public void LibraryRefresh_DiffsExistingCollectionInsteadOfReplacingIt()
    {
        var first = Track("first", "First", "Artist A", "Album A");
        var second = Track("second", "Second", "Artist B", "Album B");
        var library = new FakeLibraryService([first, second]);
        var viewModel = new FavoritesViewModel(library, new FakePlaybackService());
        var visible = viewModel.VisibleTracks;

        library.SetFavorites([second]);
        library.RaiseChanged();

        Assert.Same(visible, viewModel.VisibleTracks);
        Assert.Equal([second], viewModel.VisibleTracks);
    }

    [Fact]
    public void SearchQuery_FiltersTitleArtistAndAlbum()
    {
        var first = Track("first", "First", "Artist A", "Album A");
        var second = Track("second", "Second", "Artist B", "Album B");
        var viewModel = new FavoritesViewModel(new FakeLibraryService([first, second]), new FakePlaybackService());

        viewModel.SearchQuery = "album b";

        Assert.Equal([second], viewModel.VisibleTracks);
    }

    [Fact]
    public async Task RemovingFavorite_RemovesRowWithoutWaitingForFullLibraryReload()
    {
        var track = Track("first", "First", "Artist A", "Album A");
        var library = new FakeLibraryService([track]);
        var viewModel = new FavoritesViewModel(library, new FakePlaybackService());

        await viewModel.ToggleFavoriteCommand.ExecuteAsync(track);

        Assert.Empty(viewModel.VisibleTracks);
        Assert.Same(track, library.ToggledTrack);
    }

    [Fact]
    public void QueueCommands_ForwardSelectedTrack()
    {
        var track = Track("first", "First", "Artist A", "Album A");
        var playback = new FakePlaybackService();
        var viewModel = new FavoritesViewModel(new FakeLibraryService([track]), playback);

        viewModel.AddTrackToQueueCommand.Execute(track);
        viewModel.PlayTrackNextCommand.Execute(track);

        Assert.Same(track, playback.AddedTrack);
        Assert.Same(track, playback.NextTrack);
    }

    [Fact]
    public void PlaybackIdentity_DoesNotRebuildRowsOnPositionUpdates()
    {
        var track = Track("first", "First", "Artist A", "Album A");
        var playback = new FakePlaybackService();
        var viewModel = new FavoritesViewModel(new FakeLibraryService([track]), playback);
        var visible = viewModel.VisibleTracks;
        var changes = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FavoritesViewModel.CurrentTrackId))
            {
                changes++;
            }
        };

        playback.CurrentTrack = track;
        playback.RaiseStateChanged();
        playback.RaiseStateChanged();

        Assert.Same(visible, viewModel.VisibleTracks);
        Assert.Equal(track.Id, viewModel.CurrentTrackId);
        Assert.Equal(1, changes);
    }

    private static TrackModel Track(string id, string title, string artist, string album) =>
        new(id, $@"C:\Music\{id}.flac", title, artist, album, "03:00", null);

    private sealed class FakeLibraryService(IReadOnlyList<TrackModel> favorites) : ILibraryService
    {
        private IReadOnlyList<TrackModel> _favorites = favorites;
        public TrackModel? ToggledTrack { get; private set; }
        public IReadOnlyList<TrackModel> Tracks => _favorites;
        public IReadOnlyList<string> Folders => [];
        public IReadOnlyList<AlbumModel> Albums => [];
        public IReadOnlyList<ArtistModel> Artists => [];
        public IReadOnlyList<TrackModel> Favorites => _favorites;
        public bool IsScanning => false;
        public string? Error => null;
        public event EventHandler? LibraryChanged;
        public void SetFavorites(IReadOnlyList<TrackModel> value) => _favorites = value;
        public void RaiseChanged() => LibraryChanged?.Invoke(this, EventArgs.Empty);
        public Task AddFolderAsync(string folder) => Task.CompletedTask;
        public Task RemoveFolderAsync(string folder) => Task.CompletedTask;
        public Task RescanAsync() => Task.CompletedTask;
        public Task ToggleFavoriteAsync(TrackModel track)
        {
            ToggledTrack = track;
            return Task.CompletedTask;
        }
        public Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile) => Task.CompletedTask;
        public IReadOnlyList<TrackModel> GetAlbumTracks(string albumId) => [];
        public IReadOnlyList<TrackModel> GetArtistTracks(string artistName) => [];
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public TrackModel? CurrentTrack { get; set; }
        public TrackModel? AddedTrack { get; private set; }
        public TrackModel? NextTrack { get; private set; }
        public IReadOnlyList<TrackModel> Queue => [];
        public PlaybackMode Mode => PlaybackMode.Loop;
        public PlaybackStatus Status => PlaybackStatus.Paused;
        public double Volume => 1;
        public double PositionSeconds => 0;
        public double DurationSeconds => 0;
        public bool IsLoading => false;
        public bool IsPlaying => false;
        public string? Error => null;
        public event EventHandler? StateChanged;
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
        public void AddToQueue(TrackModel track) => AddedTrack = track;
        public void PlayNext(TrackModel track) => NextTrack = track;
        public void ReorderQueue(IReadOnlyList<TrackModel> tracks) { }
        public void RemoveFromQueue(TrackModel track) { }
        public void ClearQueue() { }
    }
}
