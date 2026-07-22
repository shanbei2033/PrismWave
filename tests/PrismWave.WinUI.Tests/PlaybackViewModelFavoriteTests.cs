using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Player;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class PlaybackViewModelFavoriteTests
{
    [Fact]
    public async Task LocalCurrentTrack_TogglesFavoriteAndRefreshesGlyph()
    {
        var track = CreateTrack();
        var library = new FakeLibraryService();
        var viewModel = new PlaybackViewModel(
            new FakePlaybackService(track),
            new EmptyLyricsService(),
            coverService: null,
            libraryService: library);

        Assert.True(viewModel.CanFavoriteCurrentTrack);
        Assert.True(viewModel.ToggleCurrentFavoriteCommand.CanExecute(null));
        Assert.Equal("\uEB51", viewModel.CurrentFavoriteGlyph);

        await viewModel.ToggleCurrentFavoriteCommand.ExecuteAsync(null);

        Assert.Equal(track.Path, Assert.Single(library.Favorites).Path);
        Assert.Equal("\uEB52", viewModel.CurrentFavoriteGlyph);
    }

    [Fact]
    public void RemoteCurrentTrack_DisablesFavoriteAction()
    {
        var remoteTrack = CreateTrack() with
        {
            IsRemote = true,
            Path = "online://provider/track"
        };
        var viewModel = new PlaybackViewModel(
            new FakePlaybackService(remoteTrack),
            new EmptyLyricsService(),
            coverService: null,
            libraryService: new FakeLibraryService());

        Assert.False(viewModel.CanFavoriteCurrentTrack);
        Assert.False(viewModel.ToggleCurrentFavoriteCommand.CanExecute(null));
        Assert.Equal("\uEB51", viewModel.CurrentFavoriteGlyph);
    }

    [Fact]
    public void PlaybackProgress_DoesNotInvalidateUnchangedCoverPath()
    {
        var playback = new FakePlaybackService(CreateTrack());
        var viewModel = new PlaybackViewModel(
            playback,
            new EmptyLyricsService());
        var coverNotifications = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PlaybackViewModel.CurrentCoverPath))
            {
                coverNotifications++;
            }
        };

        playback.RaiseStateChanged();
        playback.RaiseStateChanged();

        Assert.Equal(0, coverNotifications);
    }

    private static TrackModel CreateTrack()
    {
        return new TrackModel(
            "track",
            @"C:\Music\Song.flac",
            "Song",
            "Artist",
            "Album",
            "03:45",
            null,
            DurationSeconds: 225);
    }

    private sealed class FakeLibraryService : ILibraryService
    {
        private readonly List<TrackModel> _favorites = new();

        public IReadOnlyList<TrackModel> Tracks { get; } = Array.Empty<TrackModel>();
        public IReadOnlyList<string> Folders { get; } = Array.Empty<string>();
        public IReadOnlyList<AlbumModel> Albums { get; } = Array.Empty<AlbumModel>();
        public IReadOnlyList<ArtistModel> Artists { get; } = Array.Empty<ArtistModel>();
        public IReadOnlyList<TrackModel> Favorites => _favorites;
        public bool IsScanning => false;
        public string? Error => null;

        public event EventHandler? LibraryChanged;

        public Task AddFolderAsync(string folder) => Task.CompletedTask;
        public Task RemoveFolderAsync(string folder) => Task.CompletedTask;
        public Task RescanAsync() => Task.CompletedTask;

        public Task ToggleFavoriteAsync(TrackModel track)
        {
            var existing = _favorites.FindIndex(item =>
                string.Equals(item.Path, track.Path, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                _favorites.RemoveAt(existing);
            }
            else
            {
                _favorites.Add(track with { IsFavorite = true });
            }

            LibraryChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile) => Task.CompletedTask;
        public IReadOnlyList<TrackModel> GetAlbumTracks(string albumId) => Array.Empty<TrackModel>();
        public IReadOnlyList<TrackModel> GetArtistTracks(string artistName) => Array.Empty<TrackModel>();
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        private readonly TrackModel _track;

        public FakePlaybackService(TrackModel track)
        {
            _track = track;
            CurrentTrack = track;
            Queue = new[] { track };
        }

        public TrackModel? CurrentTrack { get; }
        public IReadOnlyList<TrackModel> Queue { get; }
        public PlaybackMode Mode => PlaybackMode.Loop;
        public PlaybackStatus Status => PlaybackStatus.Paused;
        public double Volume => 0.8;
        public double PositionSeconds => 0;
        public double DurationSeconds => _track.DurationSeconds;
        public bool IsLoading => false;
        public bool IsPlaying => false;
        public string? Error => null;

        public event EventHandler? StateChanged;

        public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

        public void Play(TrackModel value, IReadOnlyList<TrackModel>? queue = null) { }
        public void Stop() { }
        public void TogglePlayPause() { }
        public void Next() { }
        public void Previous() { }
        public void CycleMode() { }
        public void SetVolume(double volume) { }
        public void Seek(double seconds) { }
        public void PlayFromQueue(TrackModel value) { }
        public void ReorderQueue(IReadOnlyList<TrackModel> tracks) { }
        public void RemoveFromQueue(TrackModel value) { }
        public void ClearQueue() { }
    }

    private sealed class EmptyLyricsService : ILyricsService
    {
        public Task<IReadOnlyList<LyricLineModel>> LoadLyricsAsync(
            TrackModel track,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LyricLineModel>>(Array.Empty<LyricLineModel>());

        public Task<LyricsDocumentModel> LoadLyricsDocumentAsync(
            TrackModel track,
            string? sourceOverride = null,
            bool forceOnline = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LyricsDocumentModel.Empty());

        public Task<IReadOnlyList<LyricsSearchResultModel>> SearchOnlineLyricsAsync(
            TrackModel track,
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LyricsSearchResultModel>>(Array.Empty<LyricsSearchResultModel>());

        public Task<LyricsDocumentModel> LoadSearchResultAsync(
            TrackModel track,
            LyricsSearchResultModel result,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LyricsDocumentModel.Empty());

        public string GetPreferredSource(TrackModel track) => "local";
        public double GetOffsetSeconds(TrackModel track) => 0;
        public Task SetPreferredSourceAsync(TrackModel track, string source) => Task.CompletedTask;
        public Task SetOffsetSecondsAsync(TrackModel track, double seconds) => Task.CompletedTask;
    }
}
