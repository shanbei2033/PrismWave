using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IOnlineAudioCache
{
    OnlineAudioCacheStatus Status { get; }
    event EventHandler? CacheChanged;
    TrackModel? TryGetCachedTrack(TrackModel track);
    Task CacheAsync(
        TrackModel track,
        OnlinePlaybackResolution resolution,
        CancellationToken cancellationToken = default);
    void Invalidate(TrackModel cachedTrack);
    Task ClearAsync(CancellationToken cancellationToken = default);
    void Refresh();
}
