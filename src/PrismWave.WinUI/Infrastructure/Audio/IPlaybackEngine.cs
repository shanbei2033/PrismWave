using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Audio;

public class PlaybackLoadEventArgs(long loadSequence, string sourceKey) : EventArgs
{
    public long LoadSequence { get; } = loadSequence;
    public string SourceKey { get; } = sourceKey;
}

public sealed class PlaybackFailedEventArgs(
    string message,
    long loadSequence = 0,
    string sourceKey = "") : PlaybackLoadEventArgs(loadSequence, sourceKey)
{
    public string Message { get; } = message;
}

public interface IPlaybackEngine : IDisposable
{
    double PositionSeconds { get; }
    double DurationSeconds { get; }
    bool IsPlaying { get; }
    string? Error { get; }
    event EventHandler? PlaybackEnded;
    event EventHandler<PlaybackLoadEventArgs>? MediaOpened;
    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;
    event EventHandler? StateChanged;
    bool Load(TrackModel track, double volume, bool autoplay, out string? error);
    void Play();
    void Pause();
    void Stop();
    void Seek(double seconds);
    void SetVolume(double volume);
}
