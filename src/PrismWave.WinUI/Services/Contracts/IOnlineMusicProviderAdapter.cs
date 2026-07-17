using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public sealed record OnlineProviderResolveContext(
    string ProviderTrackId,
    string? CoverUrl,
    double DurationSeconds,
    OnlineQualityPreference QualityPreference,
    OnlineProviderSession? Session = null);

public interface IOnlineMusicProviderAdapter
{
    string ProviderKey { get; }

    Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
        string query,
        CancellationToken cancellationToken);

    Task<OnlinePlaybackResolution?> ResolveAsync(
        OnlineProviderResolveContext context,
        CancellationToken cancellationToken);
}
