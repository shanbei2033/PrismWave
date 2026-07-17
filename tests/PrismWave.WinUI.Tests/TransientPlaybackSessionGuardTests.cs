using PrismWave_WinUI.Infrastructure.Audio;
using PrismWave_WinUI.Models;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class TransientPlaybackSessionGuardTests
{
    [Fact]
    public void Begin_ClonesQueueAndOwnsOneActiveRevision()
    {
        var queue = new List<TrackModel> { Track("main") };
        var snapshot = new PlaybackSessionSnapshot(queue[0], queue, PlaybackMode.Shuffle, 42, 180, true);
        var guard = new TransientPlaybackSessionGuard();

        var revision = guard.Begin(snapshot);
        queue.Clear();

        Assert.True(guard.IsCurrent(revision));
        Assert.Single(guard.Snapshot!.Queue);
    }

    [Fact]
    public void BeginWhileActive_PreservesOriginalSnapshot()
    {
        var guard = new TransientPlaybackSessionGuard();
        var first = guard.Begin(Snapshot("first"));
        var second = guard.Begin(Snapshot("second"));

        Assert.Equal(first, second);
        Assert.True(guard.TryEnd(first, out var restored));
        Assert.Equal("first", restored!.Track!.Id);
    }

    [Fact]
    public void TryEnd_IsIdempotentAndRejectsPreviousRevision()
    {
        var guard = new TransientPlaybackSessionGuard();
        var first = guard.Begin(Snapshot("first"));
        Assert.True(guard.TryEnd(first, out _));
        var second = guard.Begin(Snapshot("second"));

        Assert.False(guard.TryEnd(first, out _));
        Assert.True(guard.TryEnd(second, out _));
        Assert.False(guard.TryEnd(second, out _));
    }

    private static PlaybackSessionSnapshot Snapshot(string id)
    {
        var track = Track(id);
        return new PlaybackSessionSnapshot(track, new[] { track }, PlaybackMode.Loop, 12, 180, true);
    }

    private static TrackModel Track(string id) => new(
        id,
        $@"C:\Music\{id}.flac",
        id,
        "Artist",
        "Album",
        "03:00",
        null);
}
