using System.Collections.Specialized;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Player;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class PlaybackQueueSynchronizationTests
{
    [Fact]
    public void ProgressUpdate_DoesNotResetOrReplaceQueueRows()
    {
        var service = new MutablePlaybackService(CreateTrack("a"), CreateTrack("b"));
        var viewModel = CreateViewModel(service);
        var first = viewModel.QueueItems[0];
        var second = viewModel.QueueItems[1];
        var resetCount = 0;
        var collectionChanges = 0;
        viewModel.QueueItems.CollectionChanged += (_, args) =>
        {
            collectionChanges++;
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }
        };

        service.PublishPosition(12.5);

        Assert.Same(first, viewModel.QueueItems[0]);
        Assert.Same(second, viewModel.QueueItems[1]);
        Assert.Equal(0, collectionChanges);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public void ExternalQueueRevision_ReusesAndMovesStableRows()
    {
        var trackA = CreateTrack("a");
        var trackB = CreateTrack("b");
        var trackC = CreateTrack("c");
        var service = new MutablePlaybackService(trackA, trackB, trackC);
        var viewModel = CreateViewModel(service);
        var rowA = viewModel.QueueItems[0];
        var rowB = viewModel.QueueItems[1];

        service.ReplaceQueue(trackB, trackA, CreateTrack("d"));

        Assert.Same(rowB, viewModel.QueueItems[0]);
        Assert.Same(rowA, viewModel.QueueItems[1]);
        Assert.Equal(new[] { "b", "a", "d" }, viewModel.QueueItems.Select(item => item.Track.Id));
        Assert.Equal(new[] { 1, 2, 3 }, viewModel.QueueItems.Select(item => item.Position));
    }

    [Fact]
    public void CompleteQueueReorder_CommitsOnceAndPreservesCurrentTrack()
    {
        var trackA = CreateTrack("a");
        var trackB = CreateTrack("b");
        var trackC = CreateTrack("c");
        var service = new MutablePlaybackService(trackA, trackB, trackC)
        {
            CurrentTrack = trackB
        };
        var viewModel = CreateViewModel(service);

        viewModel.BeginQueueReorder();
        viewModel.QueueItems.Move(0, 2);
        viewModel.CompleteQueueReorder();

        Assert.Equal(1, service.ReorderCallCount);
        Assert.Equal(new[] { "b", "c", "a" }, service.Queue.Select(track => track.Id));
        Assert.Same(trackB, service.CurrentTrack);
    }

    [Fact]
    public void QueueRows_UseResolvedCoversAndExposeCurrentState()
    {
        var trackA = CreateTrack("a");
        var trackB = CreateTrack("b");
        var service = new MutablePlaybackService(trackA, trackB)
        {
            CurrentTrack = trackB
        };
        var viewModel = new PlaybackViewModel(
            service,
            new EmptyLyricsService(),
            new FakeCoverService());

        Assert.Equal(@"C:\Resolved\a.jpg", viewModel.QueueItems[0].CoverPath);
        Assert.False(viewModel.QueueItems[0].IsCurrent);
        Assert.Equal(@"C:\Resolved\b.jpg", viewModel.QueueItems[1].CoverPath);
        Assert.True(viewModel.QueueItems[1].IsCurrent);
        Assert.Equal("2", viewModel.QueueItems[1].PositionLabel);
        Assert.Equal("列表循环", viewModel.ModeLabel);
    }

    private static PlaybackViewModel CreateViewModel(MutablePlaybackService service) =>
        new(service, new EmptyLyricsService());

    private static TrackModel CreateTrack(string id) => new(
        id,
        $@"C:\Music\{id}.flac",
        $"Track {id}",
        "Artist",
        "Album",
        "03:00",
        $@"C:\Covers\{id}.jpg",
        DurationSeconds: 180);

    private sealed class MutablePlaybackService(params TrackModel[] tracks) : IPlaybackService
    {
        private readonly List<TrackModel> _queue = [.. tracks];

        public TrackModel? CurrentTrack { get; set; } = tracks.FirstOrDefault();
        public IReadOnlyList<TrackModel> Queue => _queue;
        public long QueueRevision { get; private set; } = 1;
        public int ReorderCallCount { get; private set; }
        public PlaybackMode Mode => PlaybackMode.Loop;
        public PlaybackStatus Status => PlaybackStatus.Playing;
        public double Volume => 0.8;
        public double PositionSeconds { get; private set; }
        public double DurationSeconds => 180;
        public bool IsLoading => false;
        public bool IsPlaying => true;
        public string? Error => null;
        public IReadOnlyList<WindowsDsdDeviceModel> WindowsDsdDevices => [];
        public bool WindowsDsdAvailable => false;
        public string? WindowsDsdOutputModeLabel => null;
        public string? WindowsDsdActiveDeviceName => null;
        public string? WindowsDsdFallbackReason => null;

        public event EventHandler? StateChanged;

        public void PublishPosition(double seconds)
        {
            PositionSeconds = seconds;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ReplaceQueue(params TrackModel[] tracks)
        {
            _queue.Clear();
            _queue.AddRange(tracks);
            QueueRevision++;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ReorderQueue(IReadOnlyList<TrackModel> tracks)
        {
            ReorderCallCount++;
            _queue.Clear();
            _queue.AddRange(tracks);
            QueueRevision++;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Play(TrackModel track, IReadOnlyList<TrackModel>? queue = null) { }
        public void Stop() { }
        public void TogglePlayPause() { }
        public void Next() { }
        public void Previous() { }
        public void CycleMode() { }
        public void SetVolume(double volume) { }
        public void Seek(double seconds) { }
        public void PlayFromQueue(TrackModel track) { }
        public void RemoveFromQueue(TrackModel track) { }
        public void ClearQueue() { }
        public Task RefreshWindowsDsdDevicesAsync() => Task.CompletedTask;
    }

    private sealed class FakeCoverService : ICoverService
    {
        public event EventHandler<CoverChangedEventArgs>? CoverChanged
        {
            add { }
            remove { }
        }

        public string? ResolveCoverPath(TrackModel track) => $@"C:\Resolved\{track.Id}.jpg";

        public Task<IReadOnlyList<CoverSearchResultModel>> SearchOnlineCoversAsync(
            TrackModel track,
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CoverSearchResultModel>>([]);

        public Task<string> ApplyOnlineCoverAsync(
            TrackModel track,
            CoverSearchResultModel result,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
    }

    private sealed class EmptyLyricsService : ILyricsService
    {
        public Task<IReadOnlyList<LyricLineModel>> LoadLyricsAsync(
            TrackModel track,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LyricLineModel>>([]);

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
            Task.FromResult<IReadOnlyList<LyricsSearchResultModel>>([]);

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
