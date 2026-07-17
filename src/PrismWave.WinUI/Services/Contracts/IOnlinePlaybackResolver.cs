using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IOnlinePlaybackResolver
{
    Task<OnlinePlaybackResolution?> ResolveAsync(TrackModel track, CancellationToken cancellationToken = default);

    async Task<OnlinePlaybackResolution?> ResolveNextAsync(
        TrackModel track,
        IReadOnlySet<string> excludedCandidateKeys,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        return await ResolveNextAsync(
            track,
            new OnlinePlaybackExclusions(excludedCandidateKeys),
            attempt,
            cancellationToken);
    }

    async Task<OnlinePlaybackResolution?> ResolveNextAsync(
        TrackModel track,
        OnlinePlaybackExclusions exclusions,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(track with { PlaybackUrl = null }, cancellationToken);
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
