using System.Security.Cryptography;
using System.Text;

namespace PrismWave_WinUI.Models;

public enum OnlineQualityPreference
{
    Lossless,
    High,
    Standard
}

public sealed record OnlinePlaybackResolution(
    string PlaybackUrl,
    string Provider,
    string? ProviderTrackId = null,
    IReadOnlyDictionary<string, string>? PlaybackHeaders = null,
    string? CoverUrl = null,
    double DurationSeconds = 0,
    string? CandidateKey = null,
    OnlineQualityPreference Quality = OnlineQualityPreference.Lossless,
    DateTimeOffset? ExpiresAt = null,
    int Attempt = 1,
    bool IsAuthenticatedSource = false,
    long? AccountSessionRevision = null);

public static class OnlinePlaybackCandidateKey
{
    public static string Create(TrackModel track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!string.IsNullOrWhiteSpace(track.OnlineCandidateKey))
        {
            return track.OnlineCandidateKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(track.OnlineProviderTrackId))
        {
            return Create(track.Provider, track.OnlineProviderTrackId, track.PlaybackSource);
        }

        if (Uri.TryCreate(track.Path, UriKind.Absolute, out var descriptor)
            && (descriptor.Scheme.Equals("online", StringComparison.OrdinalIgnoreCase)
                || descriptor.Scheme.Equals("hits", StringComparison.OrdinalIgnoreCase)))
        {
            var provider = descriptor.Host.Equals("online", StringComparison.OrdinalIgnoreCase)
                ? track.Provider
                : descriptor.Host;
            var providerTrackId = Uri.UnescapeDataString(descriptor.AbsolutePath.Trim('/'));
            if (!string.IsNullOrWhiteSpace(providerTrackId))
            {
                return Create(provider, providerTrackId, track.PlaybackSource);
            }
        }

        return Create(track.Provider, providerTrackId: null, track.PlaybackSource);
    }

    public static string Create(
        string? provider,
        string? providerTrackId,
        string playbackUrl)
    {
        if (!string.IsNullOrWhiteSpace(providerTrackId))
        {
            var normalizedProvider = string.IsNullOrWhiteSpace(provider)
                ? "online"
                : provider.Trim().ToLowerInvariant();
            return $"{normalizedProvider}:{providerTrackId.Trim()}";
        }

        var sourceHash = SHA256.HashData(Encoding.UTF8.GetBytes(playbackUrl.Trim()));
        return $"url:{Convert.ToHexString(sourceHash).ToLowerInvariant()}";
    }
}

public sealed class OnlinePlaybackExclusions
{
    private readonly HashSet<string> _candidateKeys;
    private readonly HashSet<string> _normalizedPlaybackUrls;

    public OnlinePlaybackExclusions(
        IEnumerable<string>? candidateKeys = null,
        IEnumerable<string>? playbackUrls = null)
    {
        _candidateKeys = new HashSet<string>(
            candidateKeys?.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        _normalizedPlaybackUrls = new HashSet<string>(
            playbackUrls?.Select(NormalizePlaybackUrl)
                .Where(value => value is not null)
                .Select(value => value!)
                ?? Array.Empty<string>(),
            StringComparer.Ordinal);
    }

    public IReadOnlySet<string> CandidateKeys => _candidateKeys;

    public IReadOnlySet<string> NormalizedPlaybackUrls => _normalizedPlaybackUrls;

    public bool ContainsCandidate(string? candidateKey)
    {
        return !string.IsNullOrWhiteSpace(candidateKey)
            && _candidateKeys.Contains(candidateKey.Trim());
    }

    public bool ContainsPlaybackUrl(string? playbackUrl)
    {
        var normalized = NormalizePlaybackUrl(playbackUrl);
        return normalized is not null && _normalizedPlaybackUrls.Contains(normalized);
    }

    public bool Contains(OnlinePlaybackResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var candidateKey = resolution.CandidateKey
            ?? OnlinePlaybackCandidateKey.Create(
                resolution.Provider,
                resolution.ProviderTrackId,
                resolution.PlaybackUrl);
        return ContainsCandidate(candidateKey) || ContainsPlaybackUrl(resolution.PlaybackUrl);
    }

    public static string? NormalizePlaybackUrl(string? playbackUrl)
    {
        if (!Uri.TryCreate(playbackUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Fragment = string.Empty
        };
        if (uri.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri.AbsoluteUri;
    }

    public override string ToString()
    {
        return $"candidateKeys={_candidateKeys.Count}, playbackUrls={_normalizedPlaybackUrls.Count}";
    }
}

public static class OnlinePlaybackTrack
{
    public static TrackModel ApplyResolution(
        TrackModel track,
        OnlinePlaybackResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(resolution);
        var candidateKey = resolution.CandidateKey
            ?? OnlinePlaybackCandidateKey.Create(
                resolution.Provider,
                resolution.ProviderTrackId,
                resolution.PlaybackUrl);
        return track with
        {
            PlaybackUrl = resolution.PlaybackUrl,
            PlaybackHeaders = resolution.PlaybackHeaders,
            Provider = resolution.Provider,
            CoverPath = track.CoverPath ?? resolution.CoverUrl,
            DurationSeconds = resolution.DurationSeconds > 0
                ? resolution.DurationSeconds
                : track.DurationSeconds,
            OnlineCandidateKey = candidateKey,
            OnlineProviderTrackId = resolution.ProviderTrackId
        };
    }
}
