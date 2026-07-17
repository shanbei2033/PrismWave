using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IHitsPlaybackSession
{
    bool IsActive { get; }
    TrackModel? CurrentTrack { get; }
    double PositionSeconds { get; }
    double DurationSeconds { get; }
    bool IsLoading { get; }
    bool IsPlaying { get; }
    string? Error { get; }
    event EventHandler? StateChanged;
    long Begin();
    void Play(TrackModel track);
    void Pause();
    void Resume();
    void Seek(double seconds);
    void Stop();
    void End();
}
