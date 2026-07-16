using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IPlaybackService
{
    TrackModel? CurrentTrack { get; }
    IReadOnlyList<TrackModel> Queue { get; }
    long QueueRevision => 0;
    PlaybackMode Mode { get; }
    PlaybackStatus Status { get; }
    double Volume { get; }
    double PositionSeconds { get; }
    double DurationSeconds { get; }
    bool IsLoading { get; }
    bool IsPlaying { get; }
    string? Error { get; }
    IReadOnlyList<WindowsDsdDeviceModel> WindowsDsdDevices { get; }
    bool WindowsDsdAvailable { get; }
    string? WindowsDsdOutputModeLabel { get; }
    string? WindowsDsdActiveDeviceName { get; }
    string? WindowsDsdFallbackReason { get; }
    string ActiveAudioOutputModeLabel => string.Empty;
    string? AudioOutputFallbackReason => null;
    event EventHandler? StateChanged;
    void Play(TrackModel track, IReadOnlyList<TrackModel>? queue = null);
    void Stop();
    void TogglePlayPause();
    void Next();
    void Previous();
    void CycleMode();
    void SetVolume(double volume);
    void Seek(double seconds);
    void PlayFromQueue(TrackModel track);
    void ReorderQueue(IReadOnlyList<TrackModel> tracks);
    void RemoveFromQueue(TrackModel track);
    void ClearQueue();
    Task RefreshWindowsDsdDevicesAsync();
}
