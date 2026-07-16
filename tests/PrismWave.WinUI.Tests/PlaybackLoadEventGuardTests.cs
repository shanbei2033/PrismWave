using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class PlaybackLoadEventGuardTests
{
    [Fact]
    public void TryAccept_RejectsStaleRevisionAndSourceCallbacks()
    {
        var guard = new PlaybackLoadEventGuard();
        var oldLoad = guard.BeginLoad(3, "netease:old", autoplay: true);
        var currentLoad = guard.BeginLoad(4, "qq:new", autoplay: true);

        Assert.False(guard.TryAccept(
            oldLoad.Sequence,
            oldLoad.SourceKey,
            currentRevision: 4,
            currentSourceKey: "qq:new",
            out _));
        Assert.False(guard.TryAccept(
            currentLoad.Sequence,
            "qq:wrong",
            currentRevision: 4,
            currentSourceKey: "qq:new",
            out _));
        Assert.True(guard.TryAccept(
            currentLoad.Sequence,
            currentLoad.SourceKey,
            currentRevision: 4,
            currentSourceKey: "qq:new",
            out _));
    }

    [Fact]
    public void BeginLoad_InvalidatesOlderSameSourceCallbackAndPreservesPausedIntent()
    {
        var guard = new PlaybackLoadEventGuard();
        var oldLoad = guard.BeginLoad(7, "netease:same", autoplay: true);
        var retry = guard.BeginLoad(7, "netease:same", autoplay: false);

        Assert.False(guard.TryAccept(
            oldLoad.Sequence,
            oldLoad.SourceKey,
            currentRevision: 7,
            currentSourceKey: "netease:same",
            out _));
        Assert.True(guard.TryAccept(
            retry.Sequence,
            retry.SourceKey,
            currentRevision: 7,
            currentSourceKey: "netease:same",
            out var accepted));
        Assert.False(accepted.Autoplay);
    }

    [Fact]
    public void Invalidate_RejectsPreviouslyCurrentCallback()
    {
        var guard = new PlaybackLoadEventGuard();
        var load = guard.BeginLoad(2, "audius:one", autoplay: true);

        guard.Invalidate();

        Assert.False(guard.TryAccept(
            load.Sequence,
            load.SourceKey,
            currentRevision: 2,
            currentSourceKey: "audius:one",
            out _));
    }
}
