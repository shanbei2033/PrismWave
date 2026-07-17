using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class MpvPlaybackEngineStructureTests
{
    private static readonly string Source = File.ReadAllText(FindSource());

    [Theory]
    [InlineData("SetOption(\"audio-display\", \"no\")")]
    [InlineData("SetOption(\"video\", \"no\")")]
    [InlineData("SetOption(\"force-window\", \"no\")")]
    [InlineData("SetOption(\"cover-art-auto\", \"no\")")]
    [InlineData("SetOption(\"sub-auto\", \"no\")")]
    public void Constructor_DisablesEveryVideoAndCoverWindowPath(string statement) =>
        Assert.Contains(statement, Source, StringComparison.Ordinal);

    [Fact]
    public void OutputOptions_AreAppliedBeforeMpvInitialize()
    {
        var output = Source.IndexOf("ApplyOutputOptions(route, outputDevice)", StringComparison.Ordinal);
        var initialize = Source.IndexOf("mpv_initialize(_handle)", StringComparison.Ordinal);

        Assert.InRange(output, 0, initialize - 1);
    }

    [Fact]
    public void PlaybackStarted_IsDrivenByPlaybackRestart()
    {
        Assert.Contains("MpvEventPlaybackRestartId = 21", Source, StringComparison.Ordinal);
        Assert.Contains("PlaybackStarted?.Invoke", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureOutput(", Source, StringComparison.Ordinal);
    }

    private static string FindSource() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "PrismWave.WinUI", "Infrastructure", "Audio", "MpvPlaybackEngine.cs"));
}
