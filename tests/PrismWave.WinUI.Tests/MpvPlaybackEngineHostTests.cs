using PrismWave_WinUI.Infrastructure.Audio;
using PrismWave_WinUI.Models;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class MpvPlaybackEngineHostTests
{
    [Fact]
    public void Exclusive_AdvancesToSharedThenMpvAndStops()
    {
        var state = new AudioOutputFailoverState(AudioOutputPolicy.WasapiExclusiveId);

        Assert.Equal(AudioOutputRoute.WasapiExclusive, state.ActiveRoute);
        Assert.True(state.TryAdvance("exclusive failed"));
        Assert.Equal(AudioOutputRoute.WasapiShared, state.ActiveRoute);
        Assert.True(state.TryAdvance("shared failed"));
        Assert.Equal(AudioOutputRoute.Mpv, state.ActiveRoute);
        Assert.False(state.TryAdvance("mpv failed"));
        Assert.Equal("shared failed", state.FallbackReason);
    }

    [Fact]
    public void ResetPreference_RestoresTheRequestedFirstRoute()
    {
        var state = new AudioOutputFailoverState(AudioOutputPolicy.WasapiExclusiveId);
        state.TryAdvance("exclusive failed");

        state.Reset(AudioOutputPolicy.WasapiSharedId);

        Assert.Equal(AudioOutputRoute.WasapiShared, state.ActiveRoute);
        Assert.Null(state.FallbackReason);
    }

    [Fact]
    public void TryFallback_DisposesOldEngineAndCreatesNextRoute()
    {
        var factory = new FakePlaybackEngineFactory();
        using var host = new MpvPlaybackEngineHost(
            factory,
            AudioOutputPolicy.WasapiExclusiveId,
            "auto");
        var oldEngine = factory.Created[0].Engine;

        Assert.True(host.TryFallback("exclusive device rejected"));

        Assert.True(oldEngine.IsDisposed);
        Assert.Equal(1, oldEngine.StopCount);
        Assert.Equal(AudioOutputRoute.WasapiShared, host.ActiveRoute);
        Assert.Equal(2, factory.Created.Count);
        Assert.Equal("exclusive device rejected", host.FallbackReason);
    }

    [Fact]
    public void RetiredEngineEvents_AreIgnored()
    {
        var factory = new FakePlaybackEngineFactory();
        using var host = new MpvPlaybackEngineHost(
            factory,
            AudioOutputPolicy.WasapiSharedId,
            "auto");
        var starts = 0;
        host.PlaybackStarted += (_, _) => starts++;
        var retired = factory.Created[0].Engine;

        host.TryFallback("shared failed");
        retired.RaisePlaybackStarted(1, "old");
        factory.Created[1].Engine.RaisePlaybackStarted(2, "new");

        Assert.Equal(1, starts);
    }

    [Fact]
    public void ResetSamePreferenceWhileFallbackActive_RecreatesPreferredRoute()
    {
        var factory = new FakePlaybackEngineFactory();
        using var host = new MpvPlaybackEngineHost(
            factory,
            AudioOutputPolicy.WasapiSharedId,
            "auto");
        host.TryFallback("shared failed");

        var replaced = host.ResetPreference(AudioOutputPolicy.WasapiSharedId, "auto");

        Assert.True(replaced);
        Assert.Equal(AudioOutputRoute.WasapiShared, host.ActiveRoute);
        Assert.Null(host.FallbackReason);
        Assert.Equal(3, factory.Created.Count);
    }

    [Fact]
    public void ForcedResetSamePreference_RetiresEngineAtSessionBoundary()
    {
        var factory = new FakePlaybackEngineFactory();
        using var host = new MpvPlaybackEngineHost(
            factory,
            AudioOutputPolicy.WasapiSharedId,
            "auto");
        var retired = factory.Created[0].Engine;
        var ended = 0;
        host.PlaybackEnded += (_, _) => ended++;

        var replaced = host.ResetPreference(
            AudioOutputPolicy.WasapiSharedId,
            "auto",
            forceRestart: true);
        retired.RaisePlaybackEnded();

        Assert.True(replaced);
        Assert.True(retired.IsDisposed);
        Assert.Equal(2, factory.Created.Count);
        Assert.Equal(0, ended);
    }

    private sealed class FakePlaybackEngineFactory : IPlaybackEngineFactory
    {
        public List<CreatedEngine> Created { get; } = [];

        public IPlaybackEngine Create(AudioOutputRoute route, string outputDevice)
        {
            var engine = new FakePlaybackEngine();
            Created.Add(new CreatedEngine(route, outputDevice, engine));
            return engine;
        }
    }

    private sealed record CreatedEngine(
        AudioOutputRoute Route,
        string OutputDevice,
        FakePlaybackEngine Engine);

    private sealed class FakePlaybackEngine : IPlaybackEngine
    {
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public bool IsPlaying { get; set; }
        public string? Error { get; set; }
        public bool IsDisposed { get; private set; }
        public int StopCount { get; private set; }
        public event EventHandler? PlaybackEnded;
        public event EventHandler<PlaybackLoadEventArgs>? PlaybackStarted;
        public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;
        public event EventHandler? StateChanged;

        public bool Load(TrackModel track, double volume, bool autoplay, out string? error) =>
            Load(track, volume, autoplay, 0, track.Id, out error);

        public bool Load(
            TrackModel track,
            double volume,
            bool autoplay,
            long loadSequence,
            string sourceKey,
            out string? error)
        {
            error = null;
            return true;
        }

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;

        public void Stop()
        {
            StopCount++;
            IsPlaying = false;
        }

        public void Seek(double seconds) => PositionSeconds = seconds;
        public void SetVolume(double volume) { }
        public void Dispose() => IsDisposed = true;

        public void RaisePlaybackStarted(long sequence, string sourceKey) =>
            PlaybackStarted?.Invoke(this, new PlaybackLoadEventArgs(sequence, sourceKey));

        public void RaisePlaybackFailed(string message, long sequence, string sourceKey) =>
            PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(message, sequence, sourceKey));

        public void RaisePlaybackEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
