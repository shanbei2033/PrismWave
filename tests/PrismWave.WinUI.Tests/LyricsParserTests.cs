using PrismWave_WinUI.Models;
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

    [Fact]
    public void ParseKrc_CreatesWordTimedSegmentsWithTrailingTags()
    {
        const string raw = "[offset:0]\n[0,3000]你<0,300,0>好<300,400,0>世<700,500,0>\n[3000,2000]第二行<3000,500,0>";

        var document = LyricsParser.Parse(raw, provider: "kugou-krc");

        Assert.Equal(2, document.Lines.Count);
        var first = document.Lines[0];
        Assert.Equal(0d, first.TimeSeconds);
        Assert.Equal("你好世", first.Text);
        Assert.Equal(3, first.TimedSegments.Count);
        Assert.Equal((0d, 0.3, "你"), (first.TimedSegments[0].StartSeconds, first.TimedSegments[0].EndSeconds, first.TimedSegments[0].Text));
        Assert.Equal((0.3, 0.7, "好"), (first.TimedSegments[1].StartSeconds, first.TimedSegments[1].EndSeconds, first.TimedSegments[1].Text));
        Assert.Equal(3d, document.Lines[1].TimeSeconds);
        Assert.True(document.HasTimedSegments);
    }

    [Fact]
    public void Parse_DoesNotRouteQrcOrYrcIntoKrcBranch()
    {
        const string qrc = "[0,2000]你(0,500)好(500,500)";
        var document = LyricsParser.Parse(qrc, provider: "qqmusic-qrc");
        Assert.Equal("你好", Assert.Single(document.Lines).Text);
        Assert.Equal(2, Assert.Single(document.Lines).TimedSegments.Count);

        const string yrc = "[1000,1200](1000,400,0)你(1400,400,0)好";
        var yrcDocument = LyricsParser.Parse(yrc, provider: "netease-yrc");
        Assert.Equal("你好", Assert.Single(yrcDocument.Lines).Text);
        Assert.Equal(2, Assert.Single(yrcDocument.Lines).TimedSegments.Count);
    }

    [Fact]
    public void Parse_MergesSameTimestampPlainLinesIntoSingleLine()
    {
        const string raw = "[00:34.09]わかる真似をして\n[00:34.09]なにも知らないね\n[00:34.09]アナタ\n[00:44.46]なにが悲しいの";

        var document = LyricsParser.Parse(raw);

        Assert.Equal(2, document.Lines.Count);
        Assert.Equal("わかる真似をして なにも知らないね アナタ", document.Lines[0].Text);
        Assert.Equal(34.09, document.Lines[0].TimeSeconds, 3);
        Assert.Null(document.Lines[0].CompanionLines);
        Assert.Equal("なにが悲しいの", document.Lines[1].Text);
    }

    [Fact]
    public void Parse_SplitsBilingualSameTimestampGroupIntoPrimaryAndCompanion()
    {
        const string raw = "[00:02.91]インターネット・エンジェルという現象は\n[00:02.91]intaanetto enjeru to iu genshou wa\n[00:05.40]仮定された有機交流電燈の";

        var document = LyricsParser.Parse(raw);

        Assert.Equal(2, document.Lines.Count);
        Assert.Equal("インターネット・エンジェルという現象は", document.Lines[0].Text);
        Assert.Equal("intaanetto enjeru to iu genshou wa", Assert.Single(document.Lines[0].CompanionLines!).Text);
        Assert.Null(document.Lines[1].CompanionLines);
    }

    [Fact]
    public void Parse_KeepsWordTimedLineAsPrimaryWithPlainCompanion()
    {
        const string raw = "[00:01.00]<00:01.00>你好\n[00:01.00]Hello World";

        var document = LyricsParser.Parse(raw);

        var line = Assert.Single(document.Lines);
        Assert.Equal("你好", line.Text);
        Assert.True(line.HasTimedSegments);
        Assert.Equal("Hello World", Assert.Single(line.CompanionLines!).Text);
    }

    [Fact]
    public void Parse_AbsorbsOverlappingBackingVocalIntoHostLine()
    {
        const string raw = "[00:25.01]<00:25.01>I'm <00:25.44>just <00:25.86>a <00:26.34>poor <00:27.19>boy\n[00:25.10]<00:25.10>(Ooh, <00:27.60>poor)\n[00:28.55]<00:28.55>I <00:28.91>need <00:29.28>no <00:29.70>sym<00:30.05>pathy";

        var document = LyricsParser.Parse(raw);

        Assert.Equal(2, document.Lines.Count);
        Assert.Equal("I'm just a poor boy", document.Lines[0].Text);
        Assert.True(document.Lines[0].HasTimedSegments);
        var backing = Assert.Single(document.Lines[0].CompanionLines!);
        Assert.Equal("(Ooh, poor)", backing.Text);
        Assert.True(backing.HasTimedSegments);
        Assert.Equal(25.1, backing.TimedSegments[0].StartSeconds, 3);
        Assert.Equal("I need no sympathy", document.Lines[1].Text);
        Assert.Null(document.Lines[1].CompanionLines);
    }

    [Fact]
    public void Parse_AbsorbsBackingVocalStartingLaterInsideHostRange()
    {
        const string raw = "[02:21.90]<02:21.90>Ma<02:22.13>ma, <02:25.15>ooh-<02:25.58>ooh\n[02:25.35]<02:25.35>(Any <02:26.18>way <02:27.05>the <02:27.88>wind <02:28.59>blows)";

        var document = LyricsParser.Parse(raw);

        var host = Assert.Single(document.Lines);
        Assert.Equal("Mama, ooh-ooh", host.Text);
        Assert.Equal("(Any way the wind blows)", Assert.Single(host.CompanionLines!).Text);
    }

    [Fact]
    public void Parse_KeepsBackingVocalWithoutHostAsIndependentLine()
    {
        const string raw = "[00:05.00](Solo intro)\n[00:10.00]Main line";

        var document = LyricsParser.Parse(raw);

        Assert.Equal(2, document.Lines.Count);
        Assert.Equal("(Solo intro)", document.Lines[0].Text);
        Assert.Null(document.Lines[0].CompanionLines);
        Assert.Equal("Main line", document.Lines[1].Text);
    }

    [Fact]
    public void LyricsKind_DetectsKrcWordTimingAsWordSynced()
    {
        var result = new LyricsSearchResultModel(
            "1",
            "Song",
            "Artist",
            string.Empty,
            0,
            "[0,3000]你<0,300,0>好<300,400,0>",
            null,
            "kugou-krc");

        Assert.Equal(LyricsSyncKind.WordSynced, result.LyricsKind);
        Assert.Equal("逐字", result.LyricsKindLabel);
    }
}
