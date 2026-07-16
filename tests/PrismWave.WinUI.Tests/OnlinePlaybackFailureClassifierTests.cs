using PrismWave_WinUI.Models;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlinePlaybackFailureClassifierTests
{
    [Theory]
    [InlineData("Failed to initialize audio driver 'wasapi'")]
    [InlineData("Could not open audio device wasapi/Headphones")]
    [InlineData("Audio device initialization failed")]
    public void Classify_RecognizesLocalAudioOutputFailures(string message)
    {
        Assert.Equal(
            OnlinePlaybackFailureKind.AudioOutput,
            OnlinePlaybackFailureClassifier.Classify(message));
    }

    [Theory]
    [InlineData("Failed to open https://audio.test/song.mp3")]
    [InlineData("HTTP error 401 Unauthorized")]
    [InlineData("Authentication failed for remote source")]
    [InlineData("HTTP error 403 Forbidden; signed URL expired")]
    [InlineData("Network timeout while reading stream")]
    [InlineData("Loading failed")]
    [InlineData("File not found")]
    [InlineData("I/O error while reading stream")]
    public void Classify_RecognizesRemoteSourceFailures(string message)
    {
        Assert.Equal(
            OnlinePlaybackFailureKind.Source,
            OnlinePlaybackFailureClassifier.Classify(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Decoder failed while parsing audio frame")]
    [InlineData("Failed to open audio decoder")]
    [InlineData("Unclassified mpv failure")]
    public void Classify_LeavesDecoderAndUnknownFailuresUnknown(string? message)
    {
        Assert.Equal(
            OnlinePlaybackFailureKind.Unknown,
            OnlinePlaybackFailureClassifier.Classify(message));
    }
}
