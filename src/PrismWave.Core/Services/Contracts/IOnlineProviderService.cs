using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IOnlineProviderService
{
    IReadOnlyList<string> SearchProviders { get; }

    Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
        string query,
        IReadOnlyCollection<string> providers,
        CancellationToken cancellationToken = default)
    {
        var requested = providers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (await SearchAsync(query, cancellationToken))
            .Where(result => requested.Contains(result.Provider))
            .ToList();
    }

    Task<IReadOnlyList<OnlineProviderTrackModel>> SearchProviderAsync(
        string query,
        string provider,
        CancellationToken cancellationToken = default) =>
        SearchAsync(query, new[] { provider }, cancellationToken);

    Task<OnlinePlaybackResolution?> ResolveAsync(
        string provider,
        string providerTrackId,
        string? coverUrl = null,
        double durationSeconds = 0,
        CancellationToken cancellationToken = default);

    Task<OnlinePlaybackResolution?> SearchAndResolveAsync(
        TrackModel track,
        string? preferredProvider = null,
        CancellationToken cancellationToken = default);

    async Task<OnlinePlaybackResolution?> SearchAndResolveAsync(
        TrackModel track,
        string? preferredProvider,
        IReadOnlySet<string> excludedCandidateKeys,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        return await SearchAndResolveAsync(
            track,
            preferredProvider,
            new OnlinePlaybackExclusions(excludedCandidateKeys),
            attempt,
            cancellationToken);
    }

    async Task<OnlinePlaybackResolution?> SearchAndResolveAsync(
        TrackModel track,
        string? preferredProvider,
        OnlinePlaybackExclusions exclusions,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        var resolved = await SearchAndResolveAsync(track, preferredProvider, cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        var candidateKey = resolved.CandidateKey
            ?? OnlinePlaybackCandidateKey.Create(
                resolved.Provider,
                resolved.ProviderTrackId,
                resolved.PlaybackUrl);
        return exclusions.ContainsCandidate(candidateKey)
            || exclusions.ContainsPlaybackUrl(resolved.PlaybackUrl)
            ? null
            : resolved with { CandidateKey = candidateKey, Attempt = Math.Max(1, attempt) };
    }

    void InvalidatePlaybackUrl(string playbackUrl);
}
