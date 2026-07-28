using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IOnlineSearchService
{
    Task<IReadOnlyList<SearchResultModel>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a cover URL for a track by searching other music sources.
    /// Used as a cross-source fallback when the original provider doesn't return a cover.
    /// </summary>
    Task<string?> ResolveCoverAsync(string title, string artist, CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<SearchResultModel>> SearchLocalAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        (await SearchAsync(query, cancellationToken)).Where(result => result.IsLocal).ToList();

    async Task<IReadOnlyList<SearchResultModel>> SearchProviderAsync(
        string query,
        string provider,
        CancellationToken cancellationToken = default) =>
        (await SearchAsync(query, cancellationToken))
            .Where(result => string.Equals(result.ProviderKey, provider, StringComparison.OrdinalIgnoreCase))
            .ToList();
}
