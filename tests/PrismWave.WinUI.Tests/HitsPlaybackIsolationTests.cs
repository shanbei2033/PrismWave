using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Hits;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class HitsPlaybackIsolationTests
{
    [Fact]
    public void ViewModel_HasNoPrimaryPlayerOrSettingsDependency()
    {
        var source = ReadSource("ViewModels", "Hits", "HitsStatusViewModel.cs");

        Assert.Contains("IHitsPlaybackSession playbackSession", source);
        Assert.DoesNotContain("IPlaybackService", source);
        Assert.DoesNotContain("ISettingsService", source);
        Assert.DoesNotContain("SaveAsync", source);
    }

    [Fact]
    public void AppServices_UsesSamePlaybackServiceAsTransientSession()
    {
        var source = ReadSource("Infrastructure", "AppServices.cs");

        Assert.Contains("IHitsPlaybackSession hitsPlaybackSession = playbackService", source);
        Assert.Contains("new HitsStatusViewModel(hitsService, hitsPlaybackSession)", source);
    }

    [Fact]
    public async Task PrepareAndEnd_UsesOnlyTransientSession()
    {
        var item = ScheduleItem("hits", "HITS song", 45);
        var hits = new FakeHitsService(ReadySnapshot(item, 45));
        var session = new FakeHitsPlaybackSession();
        var viewModel = new HitsStatusViewModel(hits, session);

        await viewModel.PrepareHitsSessionCommand.ExecuteAsync(null);
        viewModel.EndHitsSessionCommand.Execute(null);

        Assert.Equal(1, session.BeginCount);
        Assert.Equal(1, session.PlayCount);
        Assert.Equal(1, session.EndCount);
        Assert.False(viewModel.IsSessionActive);
        Assert.False(viewModel.IsPaused);
    }

    [Fact]
    public async Task PauseAndResume_UsesExplicitTransientControls()
    {
        var item = ScheduleItem("hits", "HITS song", 30);
        var hits = new FakeHitsService(ReadySnapshot(item, 30));
        var session = new FakeHitsPlaybackSession();
        var viewModel = new HitsStatusViewModel(hits, session);

        await viewModel.PrepareHitsSessionCommand.ExecuteAsync(null);
        viewModel.ToggleLivePlaybackCommand.Execute(null);
        viewModel.ToggleLivePlaybackCommand.Execute(null);

        Assert.Equal(1, session.PauseCount);
        Assert.Equal(1, session.ResumeCount);
    }

    private static HitsScheduleItemModel ScheduleItem(string id, string title, double offset)
    {
        var start = DateTimeOffset.UtcNow.AddSeconds(-offset);
        return new HitsScheduleItemModel(
            1,
            id,
            "HITS",
            start,
            start.AddMinutes(4),
            new TrackModel(
                id,
                $"hits://direct/{id}",
                title,
                "Artist",
                "Album",
                "04:00",
                null,
                true,
                "direct",
                $"https://audio.test/{id}.flac",
                DurationSeconds: 240));
    }

    private static HitsStateSnapshot ReadySnapshot(HitsScheduleItemModel item, double offset) => new(
        HitsStatusKind.Ready,
        "On air",
        "2026-07-17",
        DateTimeOffset.UtcNow,
        new[] { item },
        item,
        null,
        offset,
        false,
        false);

    private static string ReadSource(params string[] segments)
    {
        var path = Path.Combine(
            new[]
            {
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "PrismWave.WinUI"
            }.Concat(segments).ToArray());
        return File.ReadAllText(Path.GetFullPath(path));
    }

    private sealed class FakeHitsService(HitsStateSnapshot state) : IHitsService
    {
        public HitsStateSnapshot Current { get; private set; } = state;
        public event EventHandler? StateChanged;
        public Task RefreshAsync(DateTimeOffset? nowUtc = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void UpdatePosition(DateTimeOffset nowUtc) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeHitsPlaybackSession : IHitsPlaybackSession
    {
        public int BeginCount { get; private set; }
        public int PlayCount { get; private set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int EndCount { get; private set; }
        public bool IsActive { get; private set; }
        public TrackModel? CurrentTrack { get; private set; }
        public double PositionSeconds { get; private set; }
        public double DurationSeconds => CurrentTrack?.DurationSeconds ?? 0;
        public bool IsLoading => false;
        public bool IsPlaying { get; private set; }
        public string? Error => null;
        public event EventHandler? StateChanged;

        public long Begin()
        {
            BeginCount++;
            IsActive = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return BeginCount;
        }

        public void Play(TrackModel track)
        {
            PlayCount++;
            CurrentTrack = track;
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            PauseCount++;
            IsPlaying = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Resume()
        {
            ResumeCount++;
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(double seconds) => PositionSeconds = seconds;
        public void Stop() => IsPlaying = false;

        public void End()
        {
            EndCount++;
            IsActive = false;
            CurrentTrack = null;
            IsPlaying = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
