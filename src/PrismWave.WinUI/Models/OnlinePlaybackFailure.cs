namespace PrismWave_WinUI.Models;

public enum OnlinePlaybackFailureKind
{
    Unknown,
    AudioOutput,
    Source
}

public static class OnlinePlaybackFailureClassifier
{
    private static readonly string[] AudioOutputMarkers =
    {
        "audio device",
        "audio output",
        "audio driver",
        "ao/",
        "wasapi",
        "asio"
    };

    private static readonly string[] SourceMarkers =
    {
        "failed to open",
        "could not be opened",
        "loading failed",
        "file not found",
        "i/o error",
        "io error",
        "network",
        "http error",
        "authentication",
        "unauthorized",
        "forbidden",
        "expired",
        "timed out",
        "timeout",
        "connection",
        "dns",
        "tls"
    };

    private static readonly string[] DecoderMarkers =
    {
        "decoder",
        "codec",
        "demux"
    };

    public static OnlinePlaybackFailureKind Classify(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return OnlinePlaybackFailureKind.Unknown;
        }

        if (AudioOutputMarkers.Any(marker =>
            message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return OnlinePlaybackFailureKind.AudioOutput;
        }

        if (DecoderMarkers.Any(marker =>
            message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return OnlinePlaybackFailureKind.Unknown;
        }

        if (SourceMarkers.Any(marker =>
            message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return OnlinePlaybackFailureKind.Source;
        }

        return OnlinePlaybackFailureKind.Unknown;
    }
}
