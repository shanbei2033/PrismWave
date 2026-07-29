using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class OnlinePlaybackResolver : IOnlinePlaybackResolver
{
    private static readonly OnlinePlaybackExclusions NoExclusions = new();
    private readonly IOnlineProviderService _providerService;

    public OnlinePlaybackResolver()
        : this(new OnlineProviderService())
    {
    }

    public OnlinePlaybackResolver(IOnlineProviderService providerService)
    {
        _providerService = providerService;
    }

    public async Task<OnlinePlaybackResolution?> ResolveAsync(
        TrackModel track,
        CancellationToken cancellationToken = default)
    {
        return await ResolveCoreAsync(
            track,
            NoExclusions,
            attempt: 1,
            cancellationToken);
    }

    public async Task<OnlinePlaybackResolution?> ResolveNextAsync(
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

    public async Task<OnlinePlaybackResolution?> ResolveNextAsync(
        TrackModel track,
        OnlinePlaybackExclusions exclusions,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exclusions);
        return await ResolveCoreAsync(
            track with { PlaybackUrl = null },
            exclusions,
            Math.Max(1, attempt),
            cancellationToken);
    }

    private async Task<OnlinePlaybackResolution?> ResolveCoreAsync(
        TrackModel track,
        OnlinePlaybackExclusions exclusions,
        int attempt,
        CancellationToken cancellationToken)
    {
        var source = track.PlaybackSource.Trim();
        if (IsDirectPlayableUrl(source))
        {
            var direct = new OnlinePlaybackResolution(
                source,
                OnlineProviderService.NormalizeProvider(track.Provider),
                PlaybackHeaders: track.PlaybackHeaders,
                CoverUrl: track.CoverPath,
                DurationSeconds: track.DurationSeconds,
                CandidateKey: OnlinePlaybackCandidateKey.Create(track),
                Attempt: attempt);
            if (!exclusions.Contains(direct))
            {
                return direct;
            }
        }

        var descriptor = OnlineDescriptor.Parse(source, track.Provider);
        var pinnedCandidateKey = string.IsNullOrWhiteSpace(descriptor.ProviderTrackId)
            ? null
            : OnlinePlaybackCandidateKey.Create(
                descriptor.Provider,
                descriptor.ProviderTrackId!,
                playbackUrl: string.Empty);
        if (pinnedCandidateKey is not null
            && !exclusions.ContainsCandidate(pinnedCandidateKey))
        {
            var pinned = await _providerService.ResolveAsync(
                descriptor.Provider,
                descriptor.ProviderTrackId!,
                track.CoverPath,
                track.DurationSeconds,
                cancellationToken,
                track.RequiresVip);
            if (pinned is not null)
            {
                var pinnedWithContext = pinned with
                {
                    CandidateKey = pinned.CandidateKey ?? pinnedCandidateKey,
                    Attempt = attempt
                };
                if (exclusions.Contains(pinnedWithContext))
                {
                    StartupLog.Write(
                        $"online.resolve.pinned.excluded: provider={descriptor.Provider}, candidate={pinnedCandidateKey}, attempt={attempt}");
                }
                else
                {
                    StartupLog.Write(
                        $"online.resolve.pinned.ready: provider={descriptor.Provider}, candidate={pinnedCandidateKey}, attempt={attempt}");
                    return pinnedWithContext;
                }
            }
        }

        var fallback = await _providerService.SearchAndResolveAsync(
            track,
            descriptor.Provider,
            exclusions,
            attempt,
            cancellationToken);
        StartupLog.Write(
            $"online.resolve.fallback.{(fallback is null ? "failed" : "ready")}: preferred={descriptor.Provider}, resolved={fallback?.Provider ?? "none"}, candidate={fallback?.CandidateKey ?? "none"}, attempt={attempt}");
        return fallback is not null && exclusions.Contains(fallback)
            ? null
            : fallback;
    }

    public void InvalidatePlaybackUrl(string playbackUrl)
    {
        _providerService.InvalidatePlaybackUrl(playbackUrl);
    }

    private static bool IsDirectPlayableUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps
                || uri.Scheme == Uri.UriSchemeFile);
    }

    private sealed record OnlineDescriptor(string Provider, string? ProviderTrackId)
    {
        public static OnlineDescriptor Parse(string source, string fallbackProvider)
        {
            var fallback = OnlineProviderService.NormalizeProvider(fallbackProvider);
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
                && (uri.Scheme.Equals("online", StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals("hits", StringComparison.OrdinalIgnoreCase)))
            {
                var descriptorProvider = OnlineProviderService.NormalizeProvider(uri.Host);
                var provider = descriptorProvider == "online" ? fallback : descriptorProvider;
                var providerTrackId = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
                return new OnlineDescriptor(
                    provider,
                    string.IsNullOrWhiteSpace(providerTrackId) ? null : providerTrackId);
            }

            return new OnlineDescriptor(fallback, null);
        }
    }
}
