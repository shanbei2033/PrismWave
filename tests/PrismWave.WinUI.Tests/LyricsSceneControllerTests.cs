using PrismWave_WinUI.Infrastructure.Lyrics;
using PrismWave_WinUI.Models;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class LyricsSceneControllerTests
{
    private static readonly IReadOnlyList<LyricLineModel> Lines =
    [
        new(0, "00:00", "first"),
        new(10, "00:10", "second"),
        new(20, "00:20", "third"),
        new(30, "00:30", "fourth"),
        new(40, "00:40", "fifth")
    ];

    [Fact]
    public void PlaybackSamples_NeverMovePresentationOrActiveLineBackward()
    {
        var controller = CreateController();

        controller.UpdatePlaybackSample(9.96, true, LyricsPositionUpdateKind.TrackChanged, 0, 600);
        var crossed = controller.Advance(0.06, 600);
        controller.UpdatePlaybackSample(9.98, true, LyricsPositionUpdateKind.Sample, 0.06, 600);
        var reconciled = controller.Advance(0.09, 600);

        Assert.Equal(10.02, crossed.PresentationPositionSeconds, 3);
        Assert.Equal(1, crossed.ActiveIndex);
        Assert.Equal(10.05, reconciled.PresentationPositionSeconds, 3);
        Assert.Equal(1, reconciled.ActiveIndex);
    }

    [Fact]
    public void ExplicitBackwardSeek_ImmediatelyResetsPositionAndLine()
    {
        var controller = CreateController();
        controller.UpdatePlaybackSample(25, true, LyricsPositionUpdateKind.TrackChanged, 0, 600);

        var frame = controller.UpdatePlaybackSample(4.2, true, LyricsPositionUpdateKind.Seek, 1, 600);

        Assert.Equal(4.2, frame.PresentationPositionSeconds, 3);
        Assert.Equal(0, frame.ActiveIndex);
    }

    [Fact]
    public void NaturalTransition_UsesOneMonotonicScrollCoordinate()
    {
        var controller = CreateController();
        controller.UpdatePlaybackSample(9.9, true, LyricsPositionUpdateKind.TrackChanged, 0, 600);
        var start = controller.ScrollOffset;

        var first = controller.Advance(0.11, 600);
        var middle = controller.Advance(0.20, 600);
        var end = controller.Advance(0.43, 600);

        Assert.True(first.ScrollOffset >= start);
        Assert.True(middle.ScrollOffset >= first.ScrollOffset);
        Assert.True(end.ScrollOffset >= middle.ScrollOffset);
        Assert.False(end.IsTransitioning);
        Assert.Equal(controller.GetLineCenter(1), end.ScrollOffset, 3);
    }

    [Fact]
    public void ActiveState_DoesNotChangeMeasuredGeometry()
    {
        var controller = CreateController();
        var before = Enumerable.Range(0, Lines.Count)
            .Select(controller.GetLineBounds)
            .ToArray();

        controller.UpdatePlaybackSample(10.1, true, LyricsPositionUpdateKind.TrackChanged, 0, 600);
        controller.Advance(0.32, 600);

        var after = Enumerable.Range(0, Lines.Count)
            .Select(controller.GetLineBounds)
            .ToArray();
        Assert.Equal(before, after);
    }

    [Fact]
    public void ManualBrowse_HitTestsUsingTheSameSceneCoordinate()
    {
        var controller = CreateController();
        controller.UpdatePlaybackSample(10.1, false, LyricsPositionUpdateKind.TrackChanged, 0, 600);
        controller.BeginManualBrowse();
        controller.ScrollBy(controller.GetLineCenter(3) - controller.ScrollOffset, 600);

        Assert.Equal(3, controller.HitTest(300, 600));
        Assert.Equal(0, controller.GetLineVisualState(3).BlurAmount, 3);
    }

    [Fact]
    public void RapidLongJump_RepositionsScrollBeforeFocusTransition()
    {
        var controller = CreateController();
        controller.UpdatePlaybackSample(0.1, false, LyricsPositionUpdateKind.TrackChanged, 0, 600);

        var frame = controller.UpdatePlaybackSample(40.1, false, LyricsPositionUpdateKind.Seek, 1, 600);

        Assert.Equal(4, frame.ActiveIndex);
        Assert.Equal(controller.GetLineCenter(4), frame.ScrollOffset, 3);
        Assert.True(frame.IsTransitioning);
    }

    [Fact]
    public void LineAdvance_KaraokeProgressRestartsFromZeroOnNewActiveLine()
    {
        var controller = CreateController();
        controller.UpdatePlaybackSample(9.9, true, LyricsPositionUpdateKind.TrackChanged, 0, 600);
        controller.Advance(0.5, 600);
        var firstProgress = controller.GetLineVisualState(0).KaraokeProgress;
        Assert.True(firstProgress > 0.9, $"first line should be nearly complete, got {firstProgress}");

        controller.UpdatePlaybackSample(10.5, true, LyricsPositionUpdateKind.Sample, 0.6, 600);
        controller.Advance(0.7, 600);

        // 新激活行的逐字进度必须从零开始，不能被旧行终值（≈1）污染而直接全白。
        var secondProgress = controller.GetLineVisualState(1).KaraokeProgress;
        Assert.True(secondProgress < 0.5, $"new active line progress should restart, got {secondProgress}");
    }

    private static LyricsSceneController CreateController()
    {
        var controller = new LyricsSceneController();
        controller.SetLyrics(Lines, 1);
        controller.SetLineMetrics([68, 110, 68, 96, 68], 28);
        return controller;
    }
}
