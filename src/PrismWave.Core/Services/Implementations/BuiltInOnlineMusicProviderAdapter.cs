using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

internal sealed class BuiltInOnlineMusicProviderAdapter(
    string providerKey,
    Func<string, CancellationToken, Task<IReadOnlyList<OnlineProviderTrackModel>>> search,
    Func<OnlineProviderResolveContext, CancellationToken, Task<OnlinePlaybackResolution?>> resolve)
    : IOnlineMusicProviderAdapter
{
    public string ProviderKey { get; } = providerKey;

    public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
        string query,
        CancellationToken cancellationToken) => search(query, cancellationToken);

    public Task<OnlinePlaybackResolution?> ResolveAsync(
        OnlineProviderResolveContext context,
        CancellationToken cancellationToken) => resolve(context, cancellationToken);
}
