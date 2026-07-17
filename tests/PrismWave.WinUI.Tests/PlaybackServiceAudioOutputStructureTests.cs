using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class PlaybackServiceAudioOutputStructureTests
{
    private static readonly string Source = File.ReadAllText(SourcePath());

    [Fact]
    public void Service_UsesReplaceableHostAndPlaybackStartedEvent()
    {
        Assert.Contains("MpvPlaybackEngineHost", Source, StringComparison.Ordinal);
        Assert.Contains("_mpvHost.PlaybackStarted", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("_mpvEngine", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalLoads_ArmFiveSecondWatchdog()
    {
        Assert.Contains("TimeSpan.FromSeconds(5)", Source, StringComparison.Ordinal);
        Assert.Contains("ArmLocalStartupWatchdog", Source, StringComparison.Ordinal);
        Assert.Contains("CancelLocalStartupWatchdog", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteReplacement_PreservesPositionAndAutoplayIntent()
    {
        Assert.Contains("CaptureMpvPlaybackSnapshot", Source, StringComparison.Ordinal);
        Assert.Contains("RestoreMpvPlaybackSnapshot", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalFailure_AttemptsAudioRouteFallbackBeforeFinalFailure()
    {
        var fallback = Source.IndexOf("TryFallbackAudioOutput", StringComparison.Ordinal);
        var finalFailure = Source.IndexOf("SetPlaybackFailed(", fallback, StringComparison.Ordinal);

        Assert.InRange(fallback, 0, Source.Length - 1);
        Assert.InRange(finalFailure, fallback + 1, Source.Length - 1);
    }

    [Fact]
    public void HitsSession_UsesExistingHostAndFreezesPrimaryState()
    {
        var main = Read("Services", "Implementations", "PlaybackService.cs");
        var hits = Read("Services", "Implementations", "PlaybackService.HitsSession.cs");

        Assert.Contains("partial class PlaybackService", main);
        Assert.Contains("IHitsPlaybackSession", main);
        Assert.Contains("CapturePrimaryPlaybackSession", hits);
        Assert.Contains("_mpvHost.ResetPreference(\"wasapi_shared\"", hits);
        Assert.Contains("RestorePrimaryPlaybackSession", hits);
        Assert.DoesNotContain("new MpvPlaybackEngineHost", hits);
        Assert.DoesNotContain("SaveAsync", hits);
    }

    [Fact]
    public void HostCallbacks_AreRoutedByTransientRevision()
    {
        var main = Read("Services", "Implementations", "PlaybackService.cs");

        Assert.Contains("TryHandleHitsPlaybackStarted", main);
        Assert.Contains("TryHandleHitsPlaybackFailed", main);
        Assert.Contains("TryHandleHitsPlaybackEnded", main);
        Assert.Contains("TryRefreshHitsPosition", main);
    }

    private static string SourcePath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "PrismWave.WinUI", "Services", "Implementations", "PlaybackService.cs"));

    private static string Read(params string[] segments)
    {
        var path = Path.Combine(
            new[]
            {
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "PrismWave.WinUI"
            }.Concat(segments).ToArray());
        return File.ReadAllText(Path.GetFullPath(path));
    }
}
