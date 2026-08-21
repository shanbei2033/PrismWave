using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Library;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class TrackEditorViewModelTests : IDisposable
{
    private readonly FakeMetadataService _metadata = new();
    private readonly FakeLibraryService _library = new();
    private readonly FakePlaybackService _playback = new();
    private readonly TrackEditorViewModel _viewModel;

    public TrackEditorViewModelTests()
    {
        _viewModel = new TrackEditorViewModel(_metadata, _library, _playback);
    }

    [Fact]
    public async Task LoadAsync_PopulatesFieldsAndKeepsSaveDisabled()
    {
        _metadata.NextLoad = new TrackMetadataModel(
            "Title", "Artist", "Album", "AA", 2003, "Pop", "Lyrics", null, true);

        await _viewModel.LoadAsync(CreateTrack("song.mp3"));

        Assert.Equal("Title", _viewModel.Title);
        Assert.Equal("Artist", _viewModel.Artist);
        Assert.Equal("Album", _viewModel.Album);
        Assert.Equal("2003", _viewModel.Year);
        Assert.True(_viewModel.CanEditNow);
        Assert.False(_viewModel.HasChanges);
        Assert.False(_viewModel.CanSave);
    }

    [Fact]
    public async Task EditingField_MarksDirtyAndEnablesSave()
    {
        await LoadDefaultAsync();

        _viewModel.Title = "Changed";

        Assert.True(_viewModel.HasChanges);
        Assert.True(_viewModel.CanSave);
    }

    [Fact]
    public async Task SaveAsync_PersistsAndRefreshesSingleTrackWithoutFullRescan()
    {
        await LoadDefaultAsync();
        _viewModel.Title = "Changed";
        var completed = false;
        _viewModel.SaveCompleted += (_, _) => completed = true;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, _library.RefreshCount);
        Assert.Equal(0, _library.RescanCount);
        Assert.Equal("Changed", _metadata.LastSaved?.Title);
        Assert.True(completed);
        Assert.False(_viewModel.StatusIsError);
    }

    [Fact]
    public async Task SaveAsync_FallsBackToFullRescanWhenTrackMissingFromLibrary()
    {
        await LoadDefaultAsync();
        _viewModel.Title = "Changed";
        _library.RefreshResult = false;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, _library.RefreshCount);
        Assert.Equal(1, _library.RescanCount);
    }

    [Fact]
    public async Task PlayingTrack_LocksEditingAndBlocksSave()
    {
        var track = CreateTrack("playing.mp3");
        _playback.SetCurrentTrack(track);
        await _viewModel.LoadAsync(track);

        Assert.False(_viewModel.CanEditNow);
        Assert.False(_viewModel.CanSave);
        Assert.NotNull(_viewModel.LockedReason);
        Assert.Contains("播放", _viewModel.LockedReason);

        _viewModel.Title = "Changed";
        await _viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Null(_metadata.LastSaved);
    }

    [Fact]
    public async Task SwitchingAwayFromTrack_UnlocksEditing()
    {
        var track = CreateTrack("playing.mp3");
        _playback.SetCurrentTrack(track);
        await _viewModel.LoadAsync(track);
        Assert.False(_viewModel.CanEditNow);

        _playback.SetCurrentTrack(null);
        _playback.RaiseStateChanged();

        Assert.True(_viewModel.CanEditNow);
    }

    [Fact]
    public async Task NonPlayingQueueTrack_RemainsEditable()
    {
        _playback.SetCurrentTrack(CreateTrack("other.mp3"));
        await _viewModel.LoadAsync(CreateTrack("queue-item.mp3"));

        Assert.True(_viewModel.CanEditNow);
    }

    [Fact]
    public async Task UnwritableFormat_ShowsReadOnlyLockReason()
    {
        _metadata.NextLoad = new TrackMetadataModel(
            string.Empty, string.Empty, string.Empty, string.Empty, 0, string.Empty, string.Empty, null, false);

        await _viewModel.LoadAsync(CreateTrack("song.dsf"));

        Assert.False(_viewModel.IsFormatWritable);
        Assert.False(_viewModel.CanEditNow);
        Assert.NotNull(_viewModel.LockedReason);
        Assert.Contains("格式", _viewModel.LockedReason);
    }

    [Fact]
    public async Task SaveFailure_ShowsActionableErrorStatus()
    {
        await LoadDefaultAsync();
        _viewModel.Title = "Changed";
        _metadata.SaveResult = TrackMetadataSaveResult.FileLocked;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(_viewModel.StatusIsError);
        Assert.Contains("占用", _viewModel.StatusMessage);
        Assert.Equal(0, _library.RescanCount);
    }

    private async Task LoadDefaultAsync()
    {
        _metadata.NextLoad = new TrackMetadataModel(
            "Title", "Artist", "Album", "AA", 2003, "Pop", "Lyrics", null, true);
        await _viewModel.LoadAsync(CreateTrack("song.mp3"));
    }

    private static TrackModel CreateTrack(string fileName) => new(
        Path.Combine("C:\\Music", fileName),
        Path.Combine("C:\\Music", fileName),
        fileName,
        "Artist",
        "Album",
        "03:00",
        null,
        IsRemote: false,
        "Local",
        DurationSeconds: 180);

    public void Dispose()
    {
        _viewModel.Dispose();
    }

    private sealed class FakeMetadataService : ITrackMetadataService
    {
        public TrackMetadataModel NextLoad { get; set; } = new(
            string.Empty, string.Empty, string.Empty, string.Empty, 0, string.Empty, string.Empty, null, true);

        public TrackMetadataSaveResult SaveResult { get; set; } = TrackMetadataSaveResult.Success;

        public TrackMetadataModel? LastSaved { get; private set; }

        public Task<TrackMetadataModel> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextLoad);
        }

        public Task<TrackMetadataSaveResult> SaveAsync(
            string path,
            TrackMetadataModel metadata,
            string? newCoverImagePath = null,
            bool removeCover = false,
            CancellationToken cancellationToken = default)
        {
            LastSaved = metadata;
            return Task.FromResult(SaveResult);
        }
    }

    private sealed class FakeLibraryService : ILibraryService
    {
        public int RescanCount { get; private set; }
        public int RefreshCount { get; private set; }
        public bool RefreshResult { get; set; } = true;

        public IReadOnlyList<TrackModel> Tracks => [];
        public IReadOnlyList<string> Folders => [];
        public IReadOnlyList<AlbumModel> Albums => [];
        public IReadOnlyList<ArtistModel> Artists => [];
        public IReadOnlyList<TrackModel> Favorites => [];
        public bool IsScanning => false;
        public string? Error => null;
        public event EventHandler? LibraryChanged
        {
            add { }
            remove { }
        }

        public Task AddFolderAsync(string folder) => Task.CompletedTask;
        public Task RemoveFolderAsync(string folder) => Task.CompletedTask;
        public Task RescanAsync()
        {
            RescanCount++;
            return Task.CompletedTask;
        }

        public Task<bool> RefreshTrackAsync(TrackModel track)
        {
            RefreshCount++;
            return Task.FromResult(RefreshResult);
        }

        public Task ToggleFavoriteAsync(TrackModel track) => Task.CompletedTask;
        public Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile) => Task.CompletedTask;
        public IReadOnlyList<TrackModel> GetAlbumTracks(string albumId) => [];
        public IReadOnlyList<TrackModel> GetArtistTracks(string artistName) => [];
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        private TrackModel? _currentTrack;

        public TrackModel? CurrentTrack => _currentTrack;
        public IReadOnlyList<TrackModel> Queue => [];
        public long QueueRevision => 0;
        public PlaybackMode Mode => PlaybackMode.Loop;
        public PlaybackStatus Status => PlaybackStatus.Playing;
        public double Volume => 1;
        public double PositionSeconds => 0;
        public double DurationSeconds => 0;
        public bool IsLoading => false;
        public bool IsPlaying => true;
        public string? Error => null;
        public event EventHandler? StateChanged;

        public void SetCurrentTrack(TrackModel? track) => _currentTrack = track;
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
    }
}
