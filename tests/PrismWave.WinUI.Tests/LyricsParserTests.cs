using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class LyricsParserTests
{
    [Fact]
    public void ParseNeteaseYrc_CreatesWordTimedSegments()
    {
        const string raw = "[1000,1200](1000,400,0)你(1400,400,0)好(1800,400,0)呀";

        var document = LyricsParser.Parse(raw, provider: "netease-yrc");

        var line = Assert.Single(document.Lines);
        Assert.Equal("你好呀", line.Text);
        Assert.Equal(3, line.TimedSegments.Count);
        Assert.Equal(1, line.TimedSegments[0].StartSeconds);
        Assert.Equal(1.4, line.TimedSegments[0].EndSeconds);
        Assert.True(document.HasTimedSegments);
    }

    [Fact]
    public void ParseLrc_IgnoresMetadataAndExpandsMultipleTimeTags()
    {
        const string raw = "[ti:Example]\n[ar:Artist]\n[00:02.50][00:05.00]Hello\n[00:08]World";

        var document = LyricsParser.Parse(raw, source: "local", provider: "sidecar");

        Assert.True(document.IsSynced);
        Assert.Equal("local", document.Source);
        Assert.Equal("sidecar", document.Provider);
        Assert.Collection(
            document.Lines,
            line => Assert.Equal((2.5, "Hello"), (line.TimeSeconds, line.Text)),
            line => Assert.Equal((5d, "Hello"), (line.TimeSeconds, line.Text)),
            line => Assert.Equal((8d, "World"), (line.TimeSeconds, line.Text)));
    }

    [Fact]
    public void ParseQrc_DecodesXmlAndBuildsTimedWordSegments()
    {
        const string raw = "<QrcInfos><LyricInfo LyricContent=\"[0,2000]你(0,500)好(500,500)&amp;世(1000,500)界(1500,500)&#10;[2200,1000]下一句(2200,1000)\" /></QrcInfos>";

        var document = LyricsParser.Parse(raw);

        Assert.True(document.IsSynced);
        Assert.Equal(2, document.Lines.Count);
        Assert.Equal("你好&世界", document.Lines[0].Text);
        Assert.Collection(
            document.Lines[0].TimedSegments,
            segment => Assert.Equal((0d, 0.5, "你"), (segment.StartSeconds, segment.EndSeconds, segment.Text)),
            segment => Assert.Equal((0.5, 1d, "好"), (segment.StartSeconds, segment.EndSeconds, segment.Text)),
            segment => Assert.Equal((1d, 1.5, "&世"), (segment.StartSeconds, segment.EndSeconds, segment.Text)),
            segment => Assert.Equal((1.5, 2d, "界"), (segment.StartSeconds, segment.EndSeconds, segment.Text)));
        Assert.Equal(2.2, document.Lines[1].TimeSeconds, 3);
    }

    [Fact]
    public void ParseEnhancedLrc_FinalizesWordEndsAtNextWordAndLine()
    {
        const string raw = "[00:01.00]<00:01.00>Hello <00:01.50>world\n[00:03.00]<00:03.00>Next";

        var document = LyricsParser.Parse(raw);

        Assert.Equal("Hello world", document.Lines[0].Text);
        Assert.Collection(
            document.Lines[0].TimedSegments,
            segment => Assert.Equal((1d, 1.5), (segment.StartSeconds, segment.EndSeconds)),
            segment => Assert.Equal((1.5, 3d), (segment.StartSeconds, segment.EndSeconds)));
        Assert.Equal(6d, document.Lines[1].TimedSegments[0].EndSeconds);
    }

    [Fact]
    public void ParsePlainLyrics_DistributesLinesAcrossDurationHint()
    {
        const string raw = "First\nSecond\nThird";

        var document = LyricsParser.Parse(raw, durationHintSeconds: 9);

        Assert.False(document.IsSynced);
        Assert.Equal(new[] { 0d, 3d, 6d }, document.Lines.Select(line => line.TimeSeconds));
    }

    [Fact]
    public void QqQrcDecoder_DecryptsTripleDesZlibPayload()
    {
        const string encrypted = "28308A27A460E589D0583A2625C428682F393E78386138F7";

        var decoded = QqQrcDecoder.Decrypt(encrypted);

        Assert.Equal("[0,1000]Hi(0,1000)", decoded);
        Assert.Null(QqQrcDecoder.Decrypt("not-hex"));
    }
}
