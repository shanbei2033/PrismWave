using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Infrastructure.Http;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class OnlineProviderService : IOnlineProviderService
{
    private const string TaiheAppId = "16073360";
    private const string TaiheSignSalt = "0b50b02fd0d73a9c4c8c3a781c30845f";
    private static readonly TimeSpan ProviderSearchTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CandidateResolutionTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ResolutionLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SearchLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PlaybackBudget = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ExpirationSafetyMargin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AccountPreferenceGrace = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FailedResolutionLifetime = TimeSpan.FromSeconds(30);
    private static readonly IReadOnlyList<string> Providers = Array.AsReadOnly(
        new[] { "audius", "netease", "kuwo", "migu", "qq", "kugou", "taihe" });
    private static readonly Regex BracketPattern = new(
        @"\[[^\]]*\]|\([^)]*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FeaturePattern = new(
        @"feat\.?|ft\.?|with|ver\.?|version",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NonAlphaNumericPattern = new(
        @"[^\p{L}\p{Nd}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VariantPattern = new(
        @"\b(remix|cover|edit|live|mashup|bootleg|version|vip|flip|instrumental|karaoke|nightcore|rework|(?:\d{4}\s*)?remaster(?:ed)?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CollaborationSuffixPattern = new(
        @"\s+(?:\+|x|×|with|feat\.?|ft\.?)\s+.+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> NeteaseHeaders = Headers(
        "https://music.163.com/");
    private static readonly IReadOnlyDictionary<string, string> PyncmdHeaders = Headers(
        "https://music.gdstudio.xyz/");
    private static readonly IReadOnlyDictionary<string, string> KuwoHeaders = Headers(
        "https://www.kuwo.cn/");
    private static readonly IReadOnlyDictionary<string, string> MiguHeaders = Headers(
        "https://m.music.migu.cn/");
    private static readonly IReadOnlyDictionary<string, string> QqHeaders = Headers(
        "https://y.qq.com/");
    private static readonly IReadOnlyDictionary<string, string> KugouHeaders = Headers(
        "https://m.kugou.com/");
    private static readonly IReadOnlyDictionary<string, string> TaiheHeaders = Headers(
        "https://music.taihe.com/");
    private static readonly IReadOnlyDictionary<string, string> AudiusHeaders = new Dictionary<string, string>
    {
        ["User-Agent"] = "PrismWave/WinUI (+https://github.com/shanbei2033/PrismWave)",
        ["Accept"] = "application/json"
    };

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyDictionary<string, IOnlineMusicProviderAdapter> _adapters;
    private readonly IReadOnlyList<string> _providerKeys;
    private readonly OnlineProviderHealthTracker _healthTracker;
    private readonly IOnlineAccountService? _accountService;
    private readonly Func<OnlineQualityPreference> _qualityPreference;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CachedResolution> _resolutionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedSearch> _searchCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _kuwoTokenGate = new(1, 1);
    private string? _kuwoToken;
    private DateTimeOffset _kuwoTokenFetchedAt;

    public OnlineProviderService(
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        IEnumerable<IOnlineMusicProviderAdapter>? adapters = null,
        OnlineProviderHealthTracker? healthTracker = null,
        IOnlineAccountService? accountService = null,
        Func<OnlineQualityPreference>? qualityPreference = null)
    {
        _httpClient = SharedHttpClient.Resolve(httpClient);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _healthTracker = healthTracker ?? new OnlineProviderHealthTracker(_timeProvider);
        _accountService = accountService;
        if (_accountService is not null)
        {
            _accountService.AccountChanged += OnAccountChanged;
        }
        _qualityPreference = qualityPreference ?? (() => OnlineQualityPreference.Lossless);
        var configuredAdapters = adapters?.ToList() ?? CreateBuiltInAdapters();
        _adapters = configuredAdapters.ToDictionary(
            adapter => NormalizeProvider(adapter.ProviderKey),
            StringComparer.OrdinalIgnoreCase);
        _providerKeys = Providers.Where(_adapters.ContainsKey)
            .Concat(_adapters.Keys.Where(key => !Providers.Contains(key, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    public IReadOnlyList<string> SearchProviders => _providerKeys;

    public async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return await SearchAsync(query, _providerKeys, cancellationToken);
    }

    public async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
        string query,
        IReadOnlyCollection<string> providers,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return Array.Empty<OnlineProviderTrackModel>();
        }

        var selectedProviders = providers
            .Select(NormalizeProvider)
            .Where(_adapters.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var searches = selectedProviders.Select(provider =>
            SearchProviderSafelyAsync(provider, trimmed, cancellationToken));
        var batches = await Task.WhenAll(searches);
        var results = batches
            .SelectMany(batch => batch)
            .Where(result => !string.IsNullOrWhiteSpace(result.ProviderTrackId))
            .GroupBy(
                result => $"{result.Provider}:{result.ProviderTrackId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        StartupLog.Write(
            $"online.providers.search.complete: query=\"{trimmed}\", total={results.Count}, providers={string.Join(',', selectedProviders)}");
        return results;
    }

    public async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchProviderAsync(
        string query,
        string provider,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return Array.Empty<OnlineProviderTrackModel>();
        }

        return await SearchProviderCoreAsync(
            NormalizeProvider(provider),
            trimmed,
            suppressErrors: false,
            cancellationToken);
    }

    public async Task<OnlinePlaybackResolution?> ResolveAsync(
        string provider,
        string providerTrackId,
        string? coverUrl = null,
        double durationSeconds = 0,
        CancellationToken cancellationToken = default,
        bool requiresVip = false)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedId = providerTrackId.Trim();
        if (!_adapters.TryGetValue(normalizedProvider, out var adapter)
            || normalizedId.Length == 0)
        {
            return null;
        }

        using var candidateBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        candidateBudget.CancelAfter(CandidateResolutionTimeout);
        var operationToken = candidateBudget.Token;
        var quality = _qualityPreference();
        var cacheKey = $"{normalizedProvider}:{normalizedId}:{quality}:anonymous";
        OnlinePlaybackResolution? resolution;
        try
        {
            var session = await GetSessionSafelyAsync(normalizedProvider, operationToken);
            cacheKey = session is null
                ? $"{normalizedProvider}:{normalizedId}:{quality}:anonymous"
                : $"{normalizedProvider}:{normalizedId}:{quality}:account:{session.SessionRevision}";
            if (TryGetCachedResolution(cacheKey, out var cached))
            {
                return cached;
            }

            if (!_healthTracker.CanRequest(normalizedProvider))
            {
                return null;
            }

            resolution = await ResolveWithAdapterAsync(
                adapter,
                new OnlineProviderResolveContext(normalizedId, coverUrl, durationSeconds, quality, session, requiresVip),
                operationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _healthTracker.ReportFailure(normalizedProvider, OnlineProviderFailureKind.NetworkOrProtocol);
            resolution = null;
        }
        catch (Exception exception)
        {
            _healthTracker.ReportFailure(normalizedProvider, OnlineProviderFailureKind.NetworkOrProtocol);
            StartupLog.Write(
                $"online.providers.resolve.error: provider={normalizedProvider}, id={normalizedId}, {OnlineProviderLogSanitizer.Describe(exception)}");
            resolution = null;
        }

        if (resolution is not null)
        {
            _healthTracker.ReportSuccess(normalizedProvider);
            resolution = resolution with
            {
                CandidateKey = resolution.CandidateKey ?? OnlinePlaybackCandidateKey.Create(
                    normalizedProvider,
                    normalizedId,
                    resolution.PlaybackUrl),
                ExpiresAt = resolution.ExpiresAt ?? _timeProvider.GetUtcNow() + ResolutionLifetime,
                Quality = resolution.Quality
            };
        }
        else
        {
            _healthTracker.ReportFailure(normalizedProvider, OnlineProviderFailureKind.TrackUnavailable);
        }

        StoreResolution(cacheKey, resolution);
        StartupLog.Write(
            $"online.providers.resolve.{(resolution is null ? "failed" : "ready")}: provider={normalizedProvider}, id={normalizedId}");
        return resolution;
    }

    public async Task<OnlinePlaybackResolution?> SearchAndResolveAsync(
        TrackModel track,
        string? preferredProvider = null,
        CancellationToken cancellationToken = default)
    {
        return await SearchAndResolveAsync(
            track,
            preferredProvider,
            new OnlinePlaybackExclusions(),
            attempt: 1,
            cancellationToken);
    }

    public async Task<OnlinePlaybackResolution?> SearchAndResolveAsync(
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

    public async Task<OnlinePlaybackResolution?> SearchAndResolveAsync(
        TrackModel track,
        string? preferredProvider,
        OnlinePlaybackExclusions exclusions,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(PlaybackBudget);
        try
        {
            return await SearchAndResolveCoreAsync(
                track,
                preferredProvider,
                exclusions,
                attempt,
                budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<OnlinePlaybackResolution?> SearchAndResolveCoreAsync(
        TrackModel track,
        string? preferredProvider,
        OnlinePlaybackExclusions exclusions,
        int attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exclusions);
        var query = string.Join(
            ' ',
            new[] { track.Title, track.Artist }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (query.Length == 0)
        {
            return null;
        }

        var preferred = NormalizeProvider(preferredProvider ?? track.Provider);
        var cacheKey = $"search:{NormalizeText(track.Title)}:{NormalizeText(track.Artist)}:{preferred}";
        if (TryGetCachedResolution(cacheKey, out var cached)
            && cached is not null
            && !exclusions.Contains(cached))
        {
            return cached with { Attempt = Math.Max(1, attempt) };
        }

        var attemptedCandidates = new HashSet<string>(
            exclusions.CandidateKeys,
            StringComparer.OrdinalIgnoreCase);
        var resolution = await RaceProvidersAsync(
            query,
            track,
            preferred,
            attemptedCandidates,
            exclusions,
            cancellationToken);
        if (resolution is null)
        {
            var fallbackQuery = BuildCollaborationFallbackQuery(track);
            if (!string.Equals(query, fallbackQuery, StringComparison.OrdinalIgnoreCase))
            {
                StartupLog.Write(
                    $"online.providers.search.retry-normalized: title=\"{track.Title}\", query=\"{fallbackQuery}\"");
                resolution = await RaceProvidersAsync(
                    fallbackQuery,
                    track,
                    preferred,
                    attemptedCandidates,
                    exclusions,
                    cancellationToken);
            }
        }

        if (resolution is not null)
        {
            StoreResolution(cacheKey, resolution with { Attempt = 1 });
            resolution = resolution with { Attempt = Math.Max(1, attempt) };
        }
        else
        {
            InvalidateSearchCacheForQuery(query);
        }

        return resolution;
    }

    public void InvalidatePlaybackUrl(string playbackUrl)
    {
        var normalizedUrl = playbackUrl.Trim();
        if (normalizedUrl.Length == 0)
        {
            return;
        }

        int removed;
        lock (_cacheGate)
        {
            var keys = _resolutionCache
                .Where(pair => string.Equals(
                    pair.Value.Resolution?.PlaybackUrl,
                    normalizedUrl,
                    StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in keys)
            {
                _resolutionCache.Remove(key);
            }

            removed = keys.Count;
        }

        StartupLog.Write(
            $"online.providers.cache.invalidated: entries={removed}, candidate={OnlinePlaybackCandidateKey.Create("online", providerTrackId: null, normalizedUrl)}");
    }

    private void OnAccountChanged(object? sender, OnlineAccountSnapshot snapshot)
    {
        var provider = NormalizeProvider(snapshot.ProviderKey);
        lock (_cacheGate)
        {
            foreach (var key in _resolutionCache
                         .Where(pair => pair.Value.Resolution is { IsAuthenticatedSource: true } resolution
                             && NormalizeProvider(resolution.Provider) == provider)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _resolutionCache.Remove(key);
            }
        }
    }

    private async Task<OnlinePlaybackResolution?> RaceProvidersAsync(
        string query,
        TrackModel track,
        string preferredProvider,
        HashSet<string> attemptedCandidates,
        OnlinePlaybackExclusions exclusions,
        CancellationToken cancellationToken)
    {
        using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var authenticatedProviders = _accountService is null
            ? Array.Empty<string>()
            : _providerKeys.Where(provider => provider is "netease" or "qq")
                .Where(provider => _accountService.GetSnapshot(provider).State == OnlineProviderAuthState.Authenticated)
                .ToArray();
        var attemptBudget = new CandidateAttemptBudget(attemptedCandidates, authenticatedProviders);
        var taskProviders = new Dictionary<Task<OnlinePlaybackResolution?>, string>();
        foreach (var provider in _providerKeys)
        {
            var inner = ResolveFromProviderAsync(
                provider,
                query,
                track,
                preferredProvider,
                attemptBudget,
                exclusions,
                raceCancellation.Token);
            var task = authenticatedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase)
                ? CompleteProviderReservationAsync(inner, provider, attemptBudget)
                : inner;
            taskProviders[task] = provider;
        }
        var pending = taskProviders.Keys.ToList();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        OnlinePlaybackResolution? fallback = null;
        Task? grace = null;
        try
        {
            while (pending.Count > 0)
            {
                Task<OnlinePlaybackResolution?> completed;
                if (fallback is not null && grace is not null)
                {
                    var nextProvider = Task.WhenAny(pending);
                    var winner = await Task.WhenAny(nextProvider, grace);
                    if (ReferenceEquals(winner, grace))
                    {
                        return fallback;
                    }

                    completed = await nextProvider;
                }
                else
                {
                    completed = await Task.WhenAny(pending);
                }

                pending.Remove(completed);
                var resolution = await completed;
                if (resolution is null)
                {
                    if (fallback is not null && !HasPendingAuthenticatedAccount(pending, taskProviders))
                    {
                        return fallback;
                    }
                    continue;
                }

                if (_accountService is not null
                    && IsAccountPreferredResolution(resolution))
                {
                    stopwatch.Stop();
                    StartupLog.Write(
                        $"online.providers.race.ready: provider={resolution.Provider}, quality={resolution.Quality}, account=true, elapsed={stopwatch.ElapsedMilliseconds}ms");
                    return resolution;
                }

                if (HasPendingAuthenticatedAccount(pending, taskProviders)
                    && pending.Count > 0)
                {
                    fallback ??= resolution;
                    grace ??= Task.Delay(AccountPreferenceGrace, raceCancellation.Token);
                    continue;
                }

                if (fallback is not null)
                {
                    return fallback;
                }

                stopwatch.Stop();
                StartupLog.Write(
                    $"online.providers.race.ready: provider={resolution.Provider}, elapsed={stopwatch.ElapsedMilliseconds}ms");
                return resolution;
            }

            return fallback;
        }
        finally
        {
            raceCancellation.Cancel();
            _ = ObserveProviderTasksAsync(pending);
        }
    }

    private bool IsAccountPreferredResolution(OnlinePlaybackResolution resolution)
    {
        return resolution.IsAuthenticatedSource && resolution.AccountSessionRevision is not null;
    }

    private bool HasPendingAuthenticatedAccount(
        IReadOnlyCollection<Task<OnlinePlaybackResolution?>> pending,
        IReadOnlyDictionary<Task<OnlinePlaybackResolution?>, string> taskProviders)
    {
        if (_accountService is null)
        {
            return false;
        }

        return pending.Any(task =>
        {
            var provider = taskProviders[task];
            return provider is "netease" or "qq"
                && _accountService.GetSnapshot(provider).State == OnlineProviderAuthState.Authenticated;
        });
    }

    private static string BuildCollaborationFallbackQuery(TrackModel track)
    {
        var normalizedTitle = CollaborationSuffixPattern.Replace(track.Title, string.Empty).Trim();
        return string.Join(
            ' ',
            new[] { normalizedTitle, track.Artist }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private async Task<OnlinePlaybackResolution?> ResolveFromProviderAsync(
        string provider,
        string query,
        TrackModel track,
        string preferredProvider,
        CandidateAttemptBudget attemptBudget,
        OnlinePlaybackExclusions exclusions,
        CancellationToken cancellationToken)
    {
        var candidates = await SearchProviderSafelyAsync(provider, query, cancellationToken);
        var ranked = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = ScoreCandidate(track, candidate)
                    + (candidate.Provider.Equals(preferredProvider, StringComparison.OrdinalIgnoreCase) ? 12 : 0)
                    + (IsDirectPlayableUrl(candidate.DirectAudioUrl) ? 6 : 0)
            })
            .Where(item => item.Score >= 44)
            .OrderByDescending(item => item.Score)
            .DistinctBy(
                item => CandidateKey(item.Candidate),
                StringComparer.OrdinalIgnoreCase)
            .Take(2);

        foreach (var item in ranked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = item.Candidate;
            if (!await attemptBudget.TryAcquireAsync(candidate, cancellationToken))
            {
                continue;
            }

            if (IsDirectPlayableUrl(candidate.DirectAudioUrl))
            {
                var direct = new OnlinePlaybackResolution(
                    candidate.DirectAudioUrl!,
                    candidate.Provider,
                    candidate.ProviderTrackId,
                    PlaybackHeaders(candidate.Provider),
                    candidate.CoverUrl ?? track.CoverPath,
                    candidate.DurationSeconds > 0 ? candidate.DurationSeconds : track.DurationSeconds,
                    CandidateKey(candidate),
                    ExpiresAt: _timeProvider.GetUtcNow() + ResolutionLifetime);
                if (!exclusions.Contains(direct))
                {
                    return direct;
                }

                continue;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CandidateResolutionTimeout);
            OnlinePlaybackResolution? resolved;
            try
            {
                resolved = await ResolveAsync(
                    candidate.Provider,
                    candidate.ProviderTrackId,
                    candidate.CoverUrl ?? track.CoverPath,
                    candidate.DurationSeconds > 0 ? candidate.DurationSeconds : track.DurationSeconds,
                    timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            if (resolved is not null && !exclusions.Contains(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static string CandidateKey(OnlineProviderTrackModel candidate)
    {
        return OnlinePlaybackCandidateKey.Create(
            NormalizeProvider(candidate.Provider),
            candidate.ProviderTrackId,
            candidate.DirectAudioUrl ?? string.Empty);
    }

    private static async Task<OnlinePlaybackResolution?> CompleteProviderReservationAsync(
        Task<OnlinePlaybackResolution?> task,
        string provider,
        CandidateAttemptBudget budget)
    {
        try
        {
            return await task;
        }
        finally
        {
            budget.CompleteAuthenticatedProvider(provider);
        }
    }

    private static async Task ObserveProviderTasksAsync(
        IReadOnlyCollection<Task<OnlinePlaybackResolution?>> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Losing provider requests are cancelled after a playable source wins.
        }
    }

    public static string NormalizeProvider(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "net-ease" or "netease" or "netease cloud music" or "pyncmd" => "netease",
            "酷我" or "kuwo music" or "kuwo" => "kuwo",
            "咪咕" or "migu music" or "migu" => "migu",
            "qqmusic" or "qq music" or "qq" => "qq",
            "酷狗" or "kugou music" or "kugou" => "kugou",
            "百度" or "baidu" or "taihe" => "taihe",
            "audius" => "audius",
            { Length: > 0 } provider => provider,
            _ => "online"
        };
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchProviderSafelyAsync(
        string provider,
        string query,
        CancellationToken cancellationToken) =>
        await SearchProviderCoreAsync(provider, query, suppressErrors: true, cancellationToken);

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchProviderCoreAsync(
        string provider,
        string query,
        bool suppressErrors,
        CancellationToken cancellationToken)
    {
        if (!_adapters.TryGetValue(provider, out var adapter))
        {
            return Array.Empty<OnlineProviderTrackModel>();
        }

        if (!_healthTracker.CanRequest(provider))
        {
            if (suppressErrors)
            {
                return Array.Empty<OnlineProviderTrackModel>();
            }

            throw new InvalidOperationException($"Provider '{provider}' is temporarily unavailable.");
        }

        var cacheKey = $"{provider}:{NormalizeText(query)}";
        if (TryGetCachedSearch(cacheKey, out var cached))
        {
            return cached;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProviderSearchTimeout);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var results = await adapter.SearchAsync(query, timeout.Token);
            _healthTracker.ReportSuccess(provider);
            StoreSearch(cacheKey, results);
            stopwatch.Stop();
            StartupLog.Write(
                $"online.providers.search.provider: provider={provider}, count={results.Count}, elapsed={stopwatch.ElapsedMilliseconds}ms");
            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _healthTracker.ReportFailure(provider, OnlineProviderFailureKind.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            _healthTracker.ReportFailure(provider, OnlineProviderFailureKind.NetworkOrProtocol);
            StartupLog.Write(
                $"online.providers.search.failed: provider={provider}, query=\"{query}\", {OnlineProviderLogSanitizer.Describe(exception)}");
            if (suppressErrors)
            {
                return Array.Empty<OnlineProviderTrackModel>();
            }

            throw;
        }
    }

    private List<IOnlineMusicProviderAdapter> CreateBuiltInAdapters()
    {
        return
        [
            new BuiltInOnlineMusicProviderAdapter(
                "audius",
                SearchAudiusAsync,
                (context, _, _) => Task.FromResult<OnlinePlaybackResolution?>(ResolveAudius(
                    context.ProviderTrackId,
                    context.CoverUrl,
                    context.DurationSeconds))),
            new BuiltInOnlineMusicProviderAdapter(
                "netease",
                SearchNeteaseAsync,
                (context, token, skipOfficial) => ResolveNeteaseAsync(
                    context.ProviderTrackId,
                    context.CoverUrl,
                    context.DurationSeconds,
                    token,
                    skipOfficial)),
            new BuiltInOnlineMusicProviderAdapter(
                "kuwo",
                SearchKuwoAsync,
                (context, token, _) => ResolveKuwoAsync(
                    context.ProviderTrackId,
                    context.CoverUrl,
                    context.DurationSeconds,
                    token)),
            new BuiltInOnlineMusicProviderAdapter(
                "migu",
                SearchMiguAsync,
                (context, token, _) => ResolveMiguAsync(
                    context.ProviderTrackId,
                    context.CoverUrl,
                    context.DurationSeconds,
                    token)),
            new BuiltInOnlineMusicProviderAdapter(
                "qq",
                SearchQqAsync,
                (context, token, _) => ResolveQqAsync(
                    context.ProviderTrackId,
                    context.CoverUrl,
                    context.DurationSeconds,
                    token)),
            new BuiltInOnlineMusicProviderAdapter(
                "kugou",
                SearchKugouAsync,
                (context, token, _) => ResolveKugouAsync(
                    context.ProviderTrackId,
                    context.CoverUrl,
                    context.DurationSeconds,
                    token)),
            new BuiltInOnlineMusicProviderAdapter(
                "taihe",
                SearchTaiheAsync,
                (context, token, _) => ResolveTaiheAsync(
                    context.ProviderTrackId,
                    context.CoverUrl,
                    context.DurationSeconds,
                    token))
        ];
    }

    private async Task<OnlineProviderSession?> GetSessionSafelyAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        if (_accountService is null || (provider != "netease" && provider != "qq"))
        {
            return null;
        }

        try
        {
            return await _accountService.GetSessionAsync(provider, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<OnlinePlaybackResolution?> ResolveWithAdapterAsync(
        IOnlineMusicProviderAdapter adapter,
        OnlineProviderResolveContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            if (context.Session is not null && adapter.ProviderKey.Equals("netease", StringComparison.OrdinalIgnoreCase))
            {
                // For VIP tracks, check if the account has VIP status
                var snapshot = _accountService?.GetSnapshot("netease");
                var accountHasVip = snapshot?.IsVip ?? false;

                if (context.RequiresVip && !accountHasVip)
                {
                    // Account exists but no VIP - skip account resolution, go directly to gdstudio fallback
                    StartupLog.Write($"online.resolve.netease.vip-skipped: track={context.ProviderTrackId}, account-vip={accountHasVip}");
                }
                else
                {
                    var accountResolution = await ResolveNeteaseAccountAsync(context, cancellationToken);
                    if (accountResolution.Resolution is not null)
                    {
                        return accountResolution.Resolution;
                    }

                    if (accountResolution.AuthenticationFailed)
                    {
                        var refreshed = await _accountService!.HandleAuthenticationFailureAsync("netease", cancellationToken);
                        if (refreshed is not null)
                        {
                            accountResolution = await ResolveNeteaseAccountAsync(context with { Session = refreshed }, cancellationToken);
                            if (accountResolution.Resolution is not null)
                            {
                                return accountResolution.Resolution;
                            }
                        }
                    }
                }
            }
            else if (context.Session is not null && adapter.ProviderKey.Equals("qq", StringComparison.OrdinalIgnoreCase))
            {
                var accountResolution = await ResolveQqAccountAsync(context, cancellationToken);
                if (accountResolution.Resolution is not null)
                {
                    return accountResolution.Resolution;
                }

                if (accountResolution.AuthenticationFailed)
                {
                    var refreshed = await _accountService!.HandleAuthenticationFailureAsync("qq", cancellationToken);
                    if (refreshed is not null)
                    {
                        accountResolution = await ResolveQqAccountAsync(context with { Session = refreshed }, cancellationToken);
                        if (accountResolution.Resolution is not null)
                        {
                            return accountResolution.Resolution;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            // Account endpoints are opportunistic. A protocol failure must not block anonymous fallback.
        }

        // For VIP tracks on netease without VIP account, skip the official anonymous endpoint
        // (it will return null URL anyway) and go directly to gdstudio fallback
        var skipOfficialEndpoint = context.RequiresVip
            && adapter.ProviderKey.Equals("netease", StringComparison.OrdinalIgnoreCase);

        return await adapter.ResolveAsync(context with { Session = null }, cancellationToken, skipOfficialEndpoint);
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchGdStudioAsync(
        string source,
        string provider,
        string query,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"https://music-api.gdstudio.xyz/api.php?types=search&source={Uri.EscapeDataString(source)}&name={Uri.EscapeDataString(query)}&count=10&pages=1");
        using var document = await GetJsonAsync(uri, PyncmdHeaders, cancellationToken);
        if (document?.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<OnlineProviderTrackModel>();
        }

        var results = new List<OnlineProviderTrackModel>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = ReadString(item, "url_id") ?? ReadString(item, "id");
            var title = ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var artist = ReadStringArray(item, "artist");
            if (artist.Length == 0)
            {
                artist = ReadString(item, "artist") ?? string.Empty;
            }

            results.Add(new OnlineProviderTrackModel(
                provider,
                id,
                CleanText(title),
                CleanText(artist),
                CleanText(ReadString(item, "album") ?? string.Empty),
                0,
                NormalizeUrl(ReadString(item, "pic")
                    ?? ReadString(item, "pic_url")
                    ?? ReadString(item, "cover"))));
        }

        if (results.Count > 0)
        {
            StartupLog.Write(
                $"online.providers.search.compatibility: provider={provider}, source=gdstudio, count={results.Count}");
        }

        return results;
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> EnrichMissingCoversAsync(
        IReadOnlyList<OnlineProviderTrackModel> tracks,
        string source,
        CancellationToken cancellationToken)
    {
        if (tracks.Count == 0 || tracks.All(track => !string.IsNullOrWhiteSpace(track.CoverUrl)))
        {
            return tracks;
        }

        var enriched = await Task.WhenAll(tracks.Select(async track =>
        {
            if (!string.IsNullOrWhiteSpace(track.CoverUrl))
            {
                return track;
            }

            // Step 1: Try source-specific cover resolution
            string? cover = source.Equals("qq", StringComparison.OrdinalIgnoreCase)
                ? await ResolveQqCoverAsync(track.ProviderTrackId, cancellationToken)
                : await ResolveGdStudioCoverAsync(source, track.ProviderTrackId, cancellationToken);

            // Step 2: If still no cover, try Deezer cross-source cover resolution
            cover ??= await ResolveCoverFromDeezerAsync(track.Title, track.Artist, cancellationToken);

            return string.IsNullOrWhiteSpace(cover) ? track : track with { CoverUrl = cover };
        }));
        return enriched;
    }

    /// <summary>
    /// Resolve cover art by searching Deezer for the same song title + artist.
    /// This is a cross-source fallback for providers that don't return cover URLs.
    /// </summary>
    public async Task<string?> ResolveCoverFromDeezerAsync(
        string title,
        string artist,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        try
        {
            var query = string.IsNullOrWhiteSpace(artist)
                ? title
                : $"{title} {artist}";
            var uri = new Uri(
                $"https://api.deezer.com/search?q={Uri.EscapeDataString(query)}&limit=1");
            using var document = await GetJsonAsync(uri, PyncmdHeaders, cancellationToken);
            if (document is null
                || !document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var firstTrack = data.EnumerateArray().FirstOrDefault();
            if (firstTrack.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!firstTrack.TryGetProperty("album", out var album)
                || album.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return NormalizeUrl(ReadString(album, "cover_xl")
                ?? ReadString(album, "cover_big")
                ?? ReadString(album, "cover_medium"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ResolveQqCoverAsync(
        string songMid,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["comm"] = new Dictionary<string, object> { ["ct"] = 24, ["cv"] = 0 },
                ["songinfo"] = new Dictionary<string, object>
                {
                    ["module"] = "music.pf_song_detail_svr",
                    ["method"] = "get_song_detail_yqq",
                    ["param"] = new Dictionary<string, object>
                    {
                        ["song_mid"] = songMid,
                        ["song_id"] = 0
                    }
                }
            });
            var uri = new Uri(
                "https://u.y.qq.com/cgi-bin/musicu.fcg?format=json&data="
                + Uri.EscapeDataString(payload));
            using var document = await GetJsonAsync(uri, QqHeaders, cancellationToken);
            if (document is null
                || !document.RootElement.TryGetProperty("songinfo", out var songInfo)
                || !songInfo.TryGetProperty("data", out var data)
                || !data.TryGetProperty("track_info", out var trackInfo)
                || !trackInfo.TryGetProperty("album", out var album))
            {
                return null;
            }

            return album.TryGetProperty("mid", out var albumMid)
                ? BuildQqCover(albumMid.GetString())
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            StartupLog.Write($"online.providers.cover.failed: provider=qq, id={songMid}, error={exception.Message}");
            return null;
        }
    }

    private async Task<string?> ResolveKuwoCoverAsync(
        string trackId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Try Kuwo's own music info API to get the cover
            var token = await GetKuwoTokenAsync(cancellationToken) ?? string.Empty;
            var uri = new Uri(
                $"https://www.kuwo.cn/api/www/music/musicInfo?mid={Uri.EscapeDataString(trackId)}&httpsStatus=1");
            using var document = await GetJsonAsync(
                uri,
                AddHeaders(KuwoHeaders, ("csrf", token), ("Cookie", $"kw_token={token}")),
                cancellationToken);
            if (document is null
                || !document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var cover = NormalizeUrl(ReadString(data, "pic")
                ?? ReadString(data, "pic120")
                ?? ReadString(data, "albumpic")
                ?? ReadString(data, "albumPic"));
            if (cover is not null)
            {
                cover = cover.Replace("{size}", "500", StringComparison.OrdinalIgnoreCase);
                return cover;
            }

            // Fallback: construct cover URL from albumId
            var albumId = ReadString(data, "albumid") ?? ReadString(data, "albumId");
            if (!string.IsNullOrWhiteSpace(albumId))
            {
                return $"https://img4.kuwo.cn/star/albumcover/500/{Uri.EscapeDataString(albumId)}.jpg";
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            StartupLog.Write($"online.providers.cover.failed: provider=kuwo, id={trackId}, error={exception.Message}");
            return null;
        }
    }

    private async Task<string?> ResolveGdStudioCoverAsync(
        string source,
        string trackId,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri(
                $"https://music-api.gdstudio.xyz/api.php?types=pic&source={Uri.EscapeDataString(source)}&id={Uri.EscapeDataString(trackId)}");
            using var document = await GetJsonAsync(uri, PyncmdHeaders, cancellationToken);
            if (document is null)
            {
                return null;
            }

            var root = document.RootElement;
            var value = root.ValueKind == JsonValueKind.String
                ? root.GetString()
                : ReadString(root, "url") ?? ReadString(root, "pic") ?? ReadString(root, "cover");
            return NormalizeUrl(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            StartupLog.Write($"online.providers.cover.failed: provider={source}, id={trackId}, error={exception.Message}");
            return null;
        }
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAudiusAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"https://api.audius.co/v1/tracks/search?query={Uri.EscapeDataString(query)}&limit=10");
        using var document = await GetJsonAsync(uri, AudiusHeaders, cancellationToken);
        if (document is null
            || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<OnlineProviderTrackModel>();
        }

        var results = new List<OnlineProviderTrackModel>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var title = ReadString(item, "title");
            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(title)
                || ReadBool(item, "is_streamable") == false
                || ReadBool(item, "is_available") == false)
            {
                continue;
            }

            var artist = string.Empty;
            if (item.TryGetProperty("user", out var user))
            {
                artist = ReadString(user, "name") ?? string.Empty;
            }

            string? cover = null;
            if (item.TryGetProperty("artwork", out var artwork))
            {
                cover = ReadString(artwork, "1000x1000")
                    ?? ReadString(artwork, "480x480")
                    ?? ReadString(artwork, "150x150");
            }

            var duration = ReadDouble(item, "duration");
            results.Add(new OnlineProviderTrackModel(
                "audius",
                id,
                CleanText(title),
                CleanText(artist),
                CleanText(ReadString(item, "album") ?? string.Empty),
                duration,
                cover,
                AudiusStreamUrl(id)));
        }

        return results;
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchNeteaseAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"https://music.163.com/api/cloudsearch/pc?s={Uri.EscapeDataString(query)}&type=1&limit=10");
        using var document = await GetJsonAsync(uri, NeteaseHeaders, cancellationToken);
        if (document is null
            || !document.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("songs", out var songs)
            || songs.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<OnlineProviderTrackModel>();
        }

        return songs.EnumerateArray()
            .Select(ParseNeteaseTrack)
            .Where(track => track is not null)
            .Select(track => track!)
            .Take(10)
            .ToList();
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchKuwoAsync(
        string query,
        CancellationToken cancellationToken)
    {
        // Try Kuwo's native API first — it returns cover URLs in the response.
        // Cap this attempt at 3 seconds so the GdStudio fallback still has budget
        // within the overall provider search timeout.
        try
        {
            using var nativeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            nativeTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            var token = await GetKuwoTokenAsync(nativeTimeout.Token) ?? string.Empty;
            var uri = new Uri(
                $"https://www.kuwo.cn/api/www/search/searchMusicBykeyWord?key={Uri.EscapeDataString(query)}&pn=1&rn=15");
            using var document = await GetJsonAsync(
                uri,
                AddHeaders(KuwoHeaders, ("csrf", token), ("Cookie", $"kw_token={token}")),
                nativeTimeout.Token);
            if (document is not null
                && document.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("list", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                // Log raw field names from the first track for cover debugging
                if (list.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } firstTrack)
                {
                    var fieldNames = string.Join(", ", firstTrack.EnumerateObject().Select(p => p.Name));
                    StartupLog.Write($"online.providers.kuwo.fields: {fieldNames}");
                }

                var results = list.EnumerateArray()
                    .Select(ParseKuwoTrack)
                    .Where(track => track is not null)
                    .Select(track => track!)
                    .Take(10)
                    .ToList();
                if (results.Count > 0)
                {
                    // Return immediately without cover enrichment to avoid timeout.
                    // Covers will be resolved lazily by the UI via ResolveCoverForTrackAsync.
                    return results;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Fall through to GdStudio compatibility search
        }

        // Fallback: GdStudio compatibility search. Return results directly —
        // covers are enriched lazily by the UI layer (SearchViewModel.EnrichCoversAsync)
        // to avoid exhausting the provider search timeout.
        return await SearchGdStudioAsync(
            "kuwo",
            "kuwo",
            query,
            cancellationToken);
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchMiguAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var currentUri = new Uri(
            $"https://app.u.nf.migu.cn/pc/resource/song/item/search/v1.0?text={Uri.EscapeDataString(query)}&pageNo=1&pageSize=15");
        using (var currentDocument = await GetJsonAsync(currentUri, MiguHeaders, cancellationToken))
        {
            if (currentDocument?.RootElement.ValueKind == JsonValueKind.Array)
            {
                var currentResults = currentDocument.RootElement.EnumerateArray()
                    .Select(ParseMiguTrack)
                    .Where(track => track is not null)
                    .Select(track => track!)
                    .Take(10)
                    .ToList();
                if (currentResults.Count > 0)
                {
                    return currentResults;
                }
            }
        }

        var legacyUri = new Uri(
            $"https://m.music.migu.cn/migu/remoting/scr_search_tag?keyword={Uri.EscapeDataString(query)}&type=2&pgc=1&rows=15");
        using var legacyDocument = await GetJsonAsync(legacyUri, MiguHeaders, cancellationToken);
        if (legacyDocument is null
            || !legacyDocument.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("musics", out var songs)
            || songs.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<OnlineProviderTrackModel>();
        }

        return songs.EnumerateArray()
            .Select(ParseMiguTrack)
            .Where(track => track is not null)
            .Select(track => track!)
            .Take(10)
            .ToList();
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchQqAsync(
        string query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OnlineProviderTrackModel> smartboxResults = Array.Empty<OnlineProviderTrackModel>();
        var smartboxUri = new Uri(
            $"https://c.y.qq.com/splcloud/fcgi-bin/smartbox_new.fcg?key={Uri.EscapeDataString(query)}&format=json");
        using (var smartboxDocument = await GetJsonAsync(smartboxUri, QqHeaders, cancellationToken))
        {
            if (smartboxDocument is not null
                && smartboxDocument.RootElement.TryGetProperty("data", out var smartboxData)
                && smartboxData.TryGetProperty("song", out var smartboxSong)
                && smartboxSong.TryGetProperty("itemlist", out var smartboxItems)
                && smartboxItems.ValueKind == JsonValueKind.Array)
            {
                smartboxResults = smartboxItems.EnumerateArray()
                    .Select(ParseQqSmartboxTrack)
                    .Where(track => track is not null)
                    .Select(track => track!)
                    .Take(10)
                    .ToList();
            }
        }

        var legacyUri = new Uri(
            "https://c.y.qq.com/soso/fcgi-bin/client_search_cp"
            + $"?ct=24&qqmusic_ver=1298&new_json=1&remoteplace=txt.yqq.song&searchid=1&t=0&aggr=1&cr=1&catZhida=1&lossless=0&flag_qc=0&p=1&n=10&w={Uri.EscapeDataString(query)}&format=json");
        using var legacyDocument = await GetJsonAsync(legacyUri, QqHeaders, cancellationToken);
        if (legacyDocument is not null
            && legacyDocument.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("song", out var song)
            && song.TryGetProperty("list", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            var results = list.EnumerateArray()
                .Select(ParseQqTrack)
                .Where(track => track is not null)
                .Select(track => track!)
                .Take(10)
                .ToList();
            if (results.Count > 0)
            {
                return await EnrichMissingCoversAsync(results, "qq", cancellationToken);
            }
        }

        return await EnrichMissingCoversAsync(smartboxResults, "qq", cancellationToken);
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchKugouAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var currentUri = new Uri(
            $"https://songsearch.kugou.com/song_search_v2?keyword={Uri.EscapeDataString(query)}&page=1&pagesize=15&platform=WebFilter");
        using (var currentDocument = await GetJsonAsync(currentUri, KugouHeaders, cancellationToken))
        {
            if (currentDocument is not null
                && currentDocument.RootElement.TryGetProperty("data", out var currentData)
                && currentData.TryGetProperty("lists", out var currentList)
                && currentList.ValueKind == JsonValueKind.Array)
            {
                var currentResults = currentList.EnumerateArray()
                    .Select(ParseKugouCurrentTrack)
                    .Where(track => track is not null)
                    .Select(track => track!)
                    .Take(10)
                    .ToList();
                if (currentResults.Count > 0)
                {
                    return currentResults;
                }
            }
        }

        var legacyUri = new Uri(
            $"https://m.kugou.com/api/v3/search/song?keyword={Uri.EscapeDataString(query)}&page=1&pagesize=15");
        using var legacyDocument = await GetJsonAsync(legacyUri, KugouHeaders, cancellationToken);
        if (legacyDocument is null
            || !legacyDocument.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("info", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<OnlineProviderTrackModel>();
        }

        return list.EnumerateArray()
            .Select(ParseKugouTrack)
            .Where(track => track is not null)
            .Select(track => track!)
            .Take(10)
            .ToList();
    }

    private async Task<IReadOnlyList<OnlineProviderTrackModel>> SearchTaiheAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var uri = CreateTaiheUri("/search", new Dictionary<string, string>
        {
            ["word"] = query,
            ["pageNo"] = "1",
            ["type"] = "1"
        });
        using var document = await GetJsonAsync(uri, TaiheHeaders, cancellationToken);
        if (document is null)
        {
            return Array.Empty<OnlineProviderTrackModel>();
        }

        return EnumerateTaiheTracks(document.RootElement)
            .Select(ParseTaiheTrack)
            .Where(track => track is not null)
            .Select(track => track!)
            .Take(10)
            .ToList();
    }

    private static OnlinePlaybackResolution ResolveAudius(
        string trackId,
        string? coverUrl,
        double durationSeconds)
    {
        return new OnlinePlaybackResolution(
            AudiusStreamUrl(trackId),
            "audius",
            trackId,
            CoverUrl: coverUrl,
            DurationSeconds: durationSeconds);
    }

    private async Task<OnlinePlaybackResolution?> ResolveNeteaseAsync(
        string trackId,
        string? coverUrl,
        double durationSeconds,
        CancellationToken cancellationToken,
        bool skipOfficialEndpoint = false)
    {
        // Skip the official anonymous endpoint for VIP tracks without VIP account
        // (it will return null URL anyway, wasting a network round-trip)
        if (!skipOfficialEndpoint)
        {
            var officialUri = new Uri(
                $"https://music.163.com/api/song/enhance/player/url?id={Uri.EscapeDataString(trackId)}&ids=%5B{Uri.EscapeDataString(trackId)}%5D&br=320000");
            using (var document = await GetJsonAsync(officialUri, NeteaseHeaders, cancellationToken))
            {
                if (document is not null
                    && document.RootElement.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Array)
                {
                    var item = data.EnumerateArray().FirstOrDefault();
                    var url = NormalizeUrl(ReadString(item, "url"));
                    if (IsDirectPlayableUrl(url))
                    {
                        return new OnlinePlaybackResolution(
                            url!,
                            "netease",
                            trackId,
                            PlaybackHeaders("netease"),
                            coverUrl,
                            durationSeconds);
                    }
                }
            }
        }

        foreach (var bitrate in new[] { "999", "320" })
        {
            var fallbackUri = new Uri(
                $"https://music-api.gdstudio.xyz/api.php?types=url&source=netease&id={Uri.EscapeDataString(trackId)}&br={bitrate}");
            using var document = await GetJsonAsync(fallbackUri, PyncmdHeaders, cancellationToken);
            var url = document is null
                ? null
                : NormalizeUrl(WebUtility.HtmlDecode(ReadString(document.RootElement, "url") ?? string.Empty));
            if (IsDirectPlayableUrl(url))
            {
                return new OnlinePlaybackResolution(
                    url!,
                    "pyncmd",
                    trackId,
                    PlaybackHeaders("netease"),
                    coverUrl,
                    durationSeconds);
            }
        }

        return null;
    }

    private async Task<OnlinePlaybackResolution?> ResolveKuwoAsync(
        string trackId,
        string? coverUrl,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var token = await GetKuwoTokenAsync(cancellationToken) ?? string.Empty;
        var uri = new Uri(
            $"https://www.kuwo.cn/api/v1/www/music/playUrl?mid={Uri.EscapeDataString(trackId)}&type=music");
        using var document = await GetJsonAsync(
            uri,
            AddHeaders(KuwoHeaders, ("csrf", token), ("Cookie", $"kw_token={token}")),
            cancellationToken);
        if (document is not null
            && document.RootElement.TryGetProperty("data", out var data))
        {
            var url = NormalizeUrl(ReadString(data, "url"));
            if (IsDirectPlayableUrl(url))
            {
                return new OnlinePlaybackResolution(
                    url!,
                    "kuwo",
                    trackId,
                    PlaybackHeaders("kuwo"),
                    coverUrl,
                    durationSeconds);
            }
        }

        return await ResolveGdStudioAsync(
            "kuwo",
            "kuwo",
            trackId,
            coverUrl,
            durationSeconds,
            cancellationToken);
    }

    private async Task<OnlinePlaybackResolution?> ResolveMiguAsync(
        string trackId,
        string? coverUrl,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"https://m.music.migu.cn/migu/remoting/cms_audio_play?copyrightId={Uri.EscapeDataString(trackId)}");
        using var document = await GetJsonAsync(uri, MiguHeaders, cancellationToken);
        if (document is not null
            && document.RootElement.TryGetProperty("data", out var data))
        {
            var url = NormalizeUrl(ReadString(data, "playUrl"));
            if (IsDirectPlayableUrl(url))
            {
                return new OnlinePlaybackResolution(
                    url!,
                    "migu",
                    trackId,
                    PlaybackHeaders("migu"),
                    coverUrl,
                    durationSeconds);
            }
        }

        return await ResolveGdStudioAsync(
            "migu",
            "migu",
            trackId,
            coverUrl,
            durationSeconds,
            cancellationToken);
    }

    private async Task<OnlinePlaybackResolution?> ResolveQqAsync(
        string trackId,
        string? coverUrl,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["req_0"] = new Dictionary<string, object>
            {
                ["module"] = "vkey.GetVkeyServer",
                ["method"] = "CgiGetVkey",
                ["param"] = new Dictionary<string, object>
                {
                    ["guid"] = "0",
                    ["songmid"] = new[] { trackId },
                    ["songtype"] = new[] { 0 },
                    ["uin"] = "0",
                    ["loginflag"] = 0,
                    ["platform"] = "20"
                }
            }
        });
        var uri = new Uri(
            $"https://u.y.qq.com/cgi-bin/musicu.fcg?format=json&data={Uri.EscapeDataString(payload)}");
        using var document = await GetJsonAsync(uri, QqHeaders, cancellationToken);
        if (document is null
            || !document.RootElement.TryGetProperty("req_0", out var request)
            || !request.TryGetProperty("data", out var data)
            || !data.TryGetProperty("midurlinfo", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entry = entries.EnumerateArray().FirstOrDefault();
        var purl = ReadString(entry, "purl");
        if (string.IsNullOrWhiteSpace(purl))
        {
            return null;
        }

        var url = IsDirectPlayableUrl(purl)
            ? purl
            : $"http://ws.stream.qqmusic.qq.com/{purl.TrimStart('/')}";
        return new OnlinePlaybackResolution(
            url,
            "qq",
            trackId,
            PlaybackHeaders("qq"),
            coverUrl,
            durationSeconds);
    }

    private async Task<OnlinePlaybackResolution?> ResolveKugouAsync(
        string trackId,
        string? coverUrl,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"https://m.kugou.com/app/i/getSongInfo.php?cmd=playInfo&hash={Uri.EscapeDataString(trackId)}");
        using var document = await GetJsonAsync(uri, KugouHeaders, cancellationToken);
        var url = document is null ? null : NormalizeUrl(ReadString(document.RootElement, "url"));
        if (IsDirectPlayableUrl(url))
        {
            return new OnlinePlaybackResolution(
                url!,
                "kugou",
                trackId,
                PlaybackHeaders("kugou"),
                coverUrl,
                durationSeconds);
        }

        return await ResolveGdStudioAsync(
            "kugou",
            "kugou",
            trackId,
            coverUrl,
            durationSeconds,
            cancellationToken);
    }

    private async Task<OnlinePlaybackResolution?> ResolveTaiheAsync(
        string trackId,
        string? coverUrl,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var uri = CreateTaiheUri("/song/tracklink", new Dictionary<string, string>
        {
            ["TSID"] = trackId
        });
        using var document = await GetJsonAsync(uri, TaiheHeaders, cancellationToken);
        if (document is not null
            && TryGetTaiheTrackLink(document.RootElement, out var data))
        {
            var url = NormalizeUrl(ReadString(data, "path"));
            if (IsDirectPlayableUrl(url))
            {
                return new OnlinePlaybackResolution(
                    url!,
                    "taihe",
                    trackId,
                    PlaybackHeaders("taihe"),
                    coverUrl ?? NormalizeUrl(ReadString(data, "pic")),
                    durationSeconds);
            }
        }

        return await ResolveGdStudioAsync(
            "taihe",
            "taihe",
            trackId,
            coverUrl,
            durationSeconds,
            cancellationToken);
    }

    private async Task<OnlinePlaybackResolution?> ResolveGdStudioAsync(
        string source,
        string provider,
        string trackId,
        string? coverUrl,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        foreach (var bitrate in new[] { "999", "320" })
        {
            var uri = new Uri(
                $"https://music-api.gdstudio.xyz/api.php?types=url&source={Uri.EscapeDataString(source)}&id={Uri.EscapeDataString(trackId)}&br={bitrate}");
            using var document = await GetJsonAsync(uri, PyncmdHeaders, cancellationToken);
            var url = document is null
                ? null
                : NormalizeUrl(WebUtility.HtmlDecode(
                    ReadString(document.RootElement, "url") ?? string.Empty));
            if (!IsDirectPlayableUrl(url))
            {
                continue;
            }

            StartupLog.Write(
                $"online.providers.resolve.compatibility: provider={provider}, source=gdstudio, id={trackId}");
            return new OnlinePlaybackResolution(
                url!,
                provider,
                trackId,
                PlaybackHeaders(provider),
                coverUrl,
                durationSeconds);
        }

        return null;
    }

    private async Task<string?> GetKuwoTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_kuwoToken)
            && _timeProvider.GetUtcNow() - _kuwoTokenFetchedAt < TimeSpan.FromMinutes(12))
        {
            return _kuwoToken;
        }

        await _kuwoTokenGate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_kuwoToken)
                && _timeProvider.GetUtcNow() - _kuwoTokenFetchedAt < TimeSpan.FromMinutes(12))
            {
                return _kuwoToken;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.kuwo.cn/");
            ApplyHeaders(request, KuwoHeaders);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return _kuwoToken;
            }

            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var token = cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .FirstOrDefault(part => part.StartsWith("kw_token=", StringComparison.OrdinalIgnoreCase));
                    if (token is not null)
                    {
                        _kuwoToken = token["kw_token=".Length..];
                        _kuwoTokenFetchedAt = _timeProvider.GetUtcNow();
                        break;
                    }
                }
            }

            return _kuwoToken;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return _kuwoToken;
        }
        finally
        {
            _kuwoTokenGate.Release();
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyHeaders(request, headers);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                StartupLog.Write(
                    $"online.providers.http.status: host={uri.Host}, path={uri.AbsolutePath}, status={(int)response.StatusCode}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var encoding = response.Content.Headers.ContentEncoding.LastOrDefault()?.ToLowerInvariant();
            if (encoding == "gzip")
            {
                await using var decoded = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: false);
                return await JsonDocument.ParseAsync(decoded, cancellationToken: timeout.Token);
            }

            if (encoding == "deflate")
            {
                await using var decoded = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: false);
                return await JsonDocument.ParseAsync(decoded, cancellationToken: timeout.Token);
            }

            if (encoding == "br")
            {
                await using var decoded = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: false);
                return await JsonDocument.ParseAsync(decoded, cancellationToken: timeout.Token);
            }

            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException exception)
        {
            StartupLog.Write(
                $"online.providers.http.json: host={uri.Host}, path={uri.AbsolutePath}, {OnlineProviderLogSanitizer.Describe(exception)}");
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            StartupLog.Write(
                $"online.providers.http.error: host={uri.Host}, path={uri.AbsolutePath}, {OnlineProviderLogSanitizer.Describe(exception)}");
            return null;
        }
    }

    private Uri CreateTaiheUri(string path, IReadOnlyDictionary<string, string> parameters)
    {
        var signed = new Dictionary<string, string>(parameters, StringComparer.Ordinal)
        {
            ["timestamp"] = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(),
            ["appid"] = TaiheAppId
        };
        var canonical = BuildQuery(signed);
        var signSource = Uri.UnescapeDataString(canonical) + TaiheSignSalt;
        signed["sign"] = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(signSource))).ToLowerInvariant();
        return new Uri($"https://music.taihe.com/v1{path}?{BuildQuery(signed)}");
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string> values)
    {
        return string.Join(
            '&',
            values.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value)}"));
    }

    private static OnlineProviderTrackModel? ParseNeteaseTrack(JsonElement item)
    {
        var id = ReadString(item, "id");
        var title = ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var artist = ReadArtistNames(item, "ar");
        if (artist.Length == 0)
        {
            artist = ReadArtistNames(item, "artists");
        }

        var album = string.Empty;
        string? cover = null;
        if ((item.TryGetProperty("al", out var albumElement)
                || item.TryGetProperty("album", out albumElement))
            && albumElement.ValueKind == JsonValueKind.Object)
        {
            album = ReadString(albumElement, "name") ?? string.Empty;
            cover = NormalizeUrl(ReadString(albumElement, "picUrl"));
        }

        var durationMs = ReadDouble(item, "dt");
        if (durationMs <= 0)
        {
            durationMs = ReadDouble(item, "duration");
        }

        // fee: 1 = VIP-only, 4 = paid album; both require a VIP/purchase to stream officially.
        var fee = (int)ReadDouble(item, "fee");
        var requiresVip = fee is 1 or 4;

        return new OnlineProviderTrackModel(
            "netease",
            id,
            CleanText(title),
            CleanText(artist),
            CleanText(album),
            durationMs / 1000d,
            UpgradeNeteaseCover(cover),
            RequiresVip: requiresVip);
    }

    private static OnlineProviderTrackModel? ParseKuwoTrack(JsonElement item)
    {
        var id = ReadString(item, "rid") ?? ReadString(item, "musicrid");
        var title = ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // Clean musicrid prefix (e.g., "MUSIC_12345" -> "12345")
        if (id.StartsWith("MUSIC_", StringComparison.OrdinalIgnoreCase))
        {
            id = id["MUSIC_".Length..];
        }

        var cover = NormalizeUrl(ReadString(item, "pic")
            ?? ReadString(item, "pic120")
            ?? ReadString(item, "pic240")
            ?? ReadString(item, "pic500")
            ?? ReadString(item, "albumpic")
            ?? ReadString(item, "albumPic")
            ?? ReadString(item, "albumpic300")
            ?? ReadString(item, "web_albumpic_short"));
        if (cover is not null)
        {
            cover = cover.Replace("{size}", "500", StringComparison.OrdinalIgnoreCase);
            // Handle relative paths from web_albumpic_short
            if (cover.StartsWith("/", StringComparison.Ordinal))
            {
                cover = $"https://img4.kuwo.cn{cover}";
            }
        }
        else
        {
            var albumId = ReadString(item, "albumid")
                ?? ReadString(item, "albumId")
                ?? ReadString(item, "albumidstr");
            if (!string.IsNullOrWhiteSpace(albumId))
            {
                cover = $"https://img4.kuwo.cn/star/albumcover/500/{Uri.EscapeDataString(albumId)}.jpg";
            }
        }

        return new OnlineProviderTrackModel(
            "kuwo",
            id,
            CleanText(title),
            CleanText(ReadString(item, "artist") ?? string.Empty),
            CleanText(ReadString(item, "album") ?? string.Empty),
            ReadDouble(item, "duration"),
            cover);
    }

    private static OnlineProviderTrackModel? ParseMiguTrack(JsonElement item)
    {
        var id = ReadString(item, "copyrightId") ?? ReadString(item, "id");
        var title = ReadString(item, "songName") ?? ReadString(item, "title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var artist = ReadString(item, "singerName") ?? ReadString(item, "artist") ?? string.Empty;
        if (artist.Length == 0)
        {
            artist = ReadArtistNames(item, "singerList");
        }

        return new OnlineProviderTrackModel(
            "migu",
            id,
            CleanText(title),
            CleanText(artist),
            CleanText(ReadString(item, "albumName") ?? ReadString(item, "album") ?? string.Empty),
            ParseDuration(ReadString(item, "duration")),
            NormalizeUrl(ReadString(item, "cover")
                ?? ReadString(item, "picUrl")
                ?? ReadString(item, "img1")
                ?? ReadString(item, "img2")));
    }

    private static OnlineProviderTrackModel? ParseQqSmartboxTrack(JsonElement item)
    {
        var id = ReadString(item, "mid");
        var title = ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var albumMid = ReadString(item, "album_mid")
            ?? ReadString(item, "albummid")
            ?? ReadString(item, "albumMid");
        var cover = BuildQqCover(albumMid);
        return new OnlineProviderTrackModel(
            "qq",
            id,
            CleanText(title),
            CleanText(ReadString(item, "singer") ?? string.Empty),
            CleanText(ReadString(item, "albumname") ?? ReadString(item, "album") ?? string.Empty),
            ReadDouble(item, "interval"),
            cover);
    }

    private static OnlineProviderTrackModel? ParseQqTrack(JsonElement item)
    {
        var id = ReadString(item, "mid");
        var title = ReadString(item, "title") ?? ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var artist = ReadArtistNames(item, "singer");
        var album = string.Empty;
        string? cover = null;
        if (item.TryGetProperty("album", out var rawAlbum) && rawAlbum.ValueKind == JsonValueKind.Object)
        {
            album = ReadString(rawAlbum, "name") ?? string.Empty;
            cover = NormalizeUrl(ReadString(rawAlbum, "picUrl"));
            if (cover is null)
            {
                var albumMid = ReadString(rawAlbum, "mid");
                if (!string.IsNullOrWhiteSpace(albumMid))
                {
                    cover = BuildQqCover(albumMid);
                }
            }
        }

        return new OnlineProviderTrackModel(
            "qq",
            id,
            CleanText(title),
            CleanText(artist),
            CleanText(album),
            ReadDouble(item, "interval"),
            cover);
    }

    private static string? BuildQqCover(string? albumMid)
    {
        return string.IsNullOrWhiteSpace(albumMid)
            ? null
            : $"https://y.gtimg.cn/music/photo_new/T002R500x500M000{Uri.EscapeDataString(albumMid)}.jpg";
    }

    private static OnlineProviderTrackModel? ParseKugouTrack(JsonElement item)
    {
        var id = ReadString(item, "hash");
        var title = ReadString(item, "songname") ?? ReadString(item, "filename");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new OnlineProviderTrackModel(
            "kugou",
            id,
            CleanText(title),
            CleanText(ReadString(item, "singername") ?? string.Empty),
            CleanText(ReadString(item, "album_name") ?? ReadString(item, "albumName") ?? string.Empty),
            ReadDouble(item, "duration"),
            NormalizeUrl(ReadString(item, "imgUrl")));
    }

    private static OnlineProviderTrackModel? ParseKugouCurrentTrack(JsonElement item)
    {
        var id = ReadString(item, "FileHash") ?? ReadString(item, "HQFileHash");
        var title = ReadString(item, "SongName") ?? ReadString(item, "FileName");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var cover = NormalizeUrl(ReadString(item, "Image"));
        if (cover is not null)
        {
            cover = cover.Replace("{size}", "400", StringComparison.OrdinalIgnoreCase);
        }

        return new OnlineProviderTrackModel(
            "kugou",
            id,
            CleanText(title),
            CleanText(ReadString(item, "SingerName") ?? string.Empty),
            CleanText(ReadString(item, "AlbumName") ?? string.Empty),
            ReadDouble(item, "Duration"),
            cover);
    }

    private static OnlineProviderTrackModel? ParseTaiheTrack(JsonElement item)
    {
        var id = ReadString(item, "TSID")
            ?? ReadString(item, "id")
            ?? ReadString(item, "assetId");
        var title = ReadString(item, "title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new OnlineProviderTrackModel(
            "taihe",
            id,
            CleanText(title),
            CleanText(ReadArtistNames(item, "artist")),
            CleanText(ReadString(item, "albumTitle")
                ?? ReadString(item, "album")
                ?? ReadString(item, "albumName")
                ?? string.Empty),
            ReadDouble(item, "duration"),
            NormalizeUrl(ReadString(item, "pic")));
    }

    private static IEnumerable<JsonElement> EnumerateTaiheTracks(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var track in EnumerateTaiheTracks(item))
                {
                    yield return track;
                }
            }

            yield break;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (element.TryGetProperty("data", out var data))
        {
            foreach (var track in EnumerateTaiheTracks(data))
            {
                yield return track;
            }

            yield break;
        }

        if (element.TryGetProperty("typeTrack", out var list))
        {
            foreach (var track in EnumerateTaiheTracks(list))
            {
                yield return track;
            }

            yield break;
        }

        yield return element;
    }

    private static bool TryGetTaiheTrackLink(JsonElement element, out JsonElement data)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (!string.IsNullOrWhiteSpace(ReadString(element, "path")))
            {
                data = element;
                return true;
            }

            if (element.TryGetProperty("data", out var nested)
                && TryGetTaiheTrackLink(nested, out data))
            {
                return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryGetTaiheTrackLink(item, out data))
                {
                    return true;
                }
            }
        }

        data = default;
        return false;
    }

    private static int ScoreCandidate(TrackModel requested, OnlineProviderTrackModel candidate)
    {
        var requestedTitle = CanonicalTitleKey(requested.Title);
        var requestedArtist = PrimaryArtistKey(requested.Artist);
        var candidateTitle = CanonicalTitleKey(candidate.Title);
        var candidateArtist = PrimaryArtistKey(candidate.Artist);
        if (requestedTitle.Length == 0
            || requestedArtist.Length == 0
            || requestedTitle != candidateTitle
            || requestedArtist != candidateArtist)
        {
            return int.MinValue / 2;
        }

        var requestedVariants = VariantKinds(requested.Title);
        var candidateVariants = VariantKinds(candidate.Title);
        if (candidateVariants.Except(requestedVariants).Any())
        {
            return int.MinValue / 2;
        }

        var score = MatchScore(requestedTitle, candidateTitle, 56, 26)
            + MatchScore(requestedArtist, candidateArtist, 32, 14);
        if (requested.DurationSeconds > 0 && candidate.DurationSeconds > 0)
        {
            var difference = Math.Abs(requested.DurationSeconds - candidate.DurationSeconds);
            if (difference > 8)
            {
                return int.MinValue / 2;
            }

            score += difference switch
            {
                <= 3 => 14,
                _ => 8
            };
        }

        return score;
    }

    private static string PrimaryArtistKey(string artist)
    {
        var primary = Regex.Split(
                artist,
                @"\s*(?:,|&|/|\+|\bx\b|\bwith\b|\bfeat\.?\b|\bft\.?\b)\s*",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .FirstOrDefault() ?? artist;
        return NormalizeText(primary);
    }

    private static string CanonicalTitleKey(string title)
    {
        var value = CollaborationSuffixPattern.Replace(title, string.Empty);
        value = VariantPattern.Replace(value, " ");
        value = BracketPattern.Replace(value, " ");
        return NormalizeText(value);
    }

    private static HashSet<string> VariantKinds(string title)
    {
        return VariantPattern.Matches(title)
            .Select(match => NormalizeVariantKind(match.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeVariantKind(string value)
    {
        return value.Contains("remaster", StringComparison.OrdinalIgnoreCase)
            ? "remaster"
            : NormalizeText(value);
    }

    private static int MatchScore(string expected, string actual, int exact, int partial)
    {
        if (expected.Length == 0 || actual.Length == 0)
        {
            return 0;
        }

        if (expected.Equals(actual, StringComparison.Ordinal))
        {
            return exact;
        }

        return expected.Contains(actual, StringComparison.Ordinal)
            || actual.Contains(expected, StringComparison.Ordinal)
            ? partial
            : 0;
    }

    private static string NormalizeText(string input)
    {
        var value = BracketPattern.Replace(input.ToLowerInvariant(), string.Empty);
        value = FeaturePattern.Replace(value, string.Empty);
        return NonAlphaNumericPattern.Replace(value, string.Empty);
    }

    private static string CleanText(string value)
    {
        return WebUtility.HtmlDecode(value).Trim();
    }

    private static string ReadArtistNames(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var artists)
            || artists.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(", ", artists.EnumerateArray()
            .Select(artist => ReadString(artist, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name)));
    }

    private static string ReadStringArray(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(", ", values.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static double ReadDouble(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), out var number) => number,
            _ => 0
        };
    }

    private static bool? ReadBool(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static double ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var parts = value.Split(':');
        if (parts.Length == 2
            && double.TryParse(parts[0], out var minutes)
            && double.TryParse(parts[1], out var seconds))
        {
            return (minutes * 60) + seconds;
        }

        return double.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static string AudiusStreamUrl(string trackId)
    {
        return $"https://api.audius.co/v1/tracks/{Uri.EscapeDataString(trackId)}/stream";
    }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return $"https:{trimmed}";
        }

        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? $"https://{trimmed[7..]}"
            : trimmed;
    }

    private static string? UpgradeNeteaseCover(string? value)
    {
        var url = NormalizeUrl(value);
        if (url is null || url.Contains("param=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return $"{url}{(url.Contains('?') ? '&' : '?')}param=512y512";
    }

    private static bool IsDirectPlayableUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps
                || uri.Scheme == Uri.UriSchemeFile);
    }

    private static IReadOnlyDictionary<string, string> PlaybackHeaders(string provider)
    {
        var referer = NormalizeProvider(provider) switch
        {
            "netease" => "https://music.163.com/",
            "kuwo" => "https://www.kuwo.cn/",
            "migu" => "https://m.music.migu.cn/",
            "qq" => "https://y.qq.com/",
            "kugou" => "https://m.kugou.com/",
            "taihe" => "https://music.taihe.com/",
            _ => string.Empty
        };
        return referer.Length == 0
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["Referer"] = referer };
    }

    private static IReadOnlyDictionary<string, string> Headers(string referer)
    {
        return new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 PrismWave/1.0.0",
            ["Referer"] = referer,
            ["Accept"] = "application/json"
        };
    }

    private static IReadOnlyDictionary<string, string> AddHeaders(
        IReadOnlyDictionary<string, string> source,
        params (string Key, string Value)[] values)
    {
        var result = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            result[key] = value;
        }

        return result;
    }

    private static void ApplyHeaders(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            if (key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.UserAgent.ParseAdd(value);
            }
            else if (key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Referrer = new Uri(value);
            }
            else if (key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Accept.ParseAdd(value);
            }
            else
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }
    }

    private bool TryGetCachedResolution(string key, out OnlinePlaybackResolution? resolution)
    {
        lock (_cacheGate)
        {
            if (_resolutionCache.TryGetValue(key, out var cached))
            {
                if (cached.ExpiresAt > _timeProvider.GetUtcNow())
                {
                    resolution = cached.Resolution;
                    return true;
                }

                _resolutionCache.Remove(key);
            }
        }

        resolution = null;
        return false;
    }

    private void StoreResolution(string key, OnlinePlaybackResolution? resolution)
    {
        if (resolution is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var expiresAt = MinExpiration(now + ResolutionLifetime, resolution.ExpiresAt - ExpirationSafetyMargin);
        if (expiresAt <= now)
        {
            return;
        }

        lock (_cacheGate)
        {
            _resolutionCache[key] = new CachedResolution(
                resolution,
                expiresAt);
        }
    }

    private static DateTimeOffset MinExpiration(DateTimeOffset maximum, DateTimeOffset? providerExpiration)
    {
        return providerExpiration is { } expiration && expiration < maximum ? expiration : maximum;
    }

    private bool TryGetCachedSearch(string key, out IReadOnlyList<OnlineProviderTrackModel> results)
    {
        lock (_cacheGate)
        {
            if (_searchCache.TryGetValue(key, out var cached))
            {
                if (cached.ExpiresAt > _timeProvider.GetUtcNow())
                {
                    results = cached.Results;
                    return true;
                }

                _searchCache.Remove(key);
            }
        }

        results = Array.Empty<OnlineProviderTrackModel>();
        return false;
    }

    private void StoreSearch(string key, IReadOnlyList<OnlineProviderTrackModel> results)
    {
        lock (_cacheGate)
        {
            _searchCache[key] = new CachedSearch(results.ToList(), _timeProvider.GetUtcNow() + SearchLifetime);
        }
    }

    private void InvalidateSearchCacheForQuery(string query)
    {
        var suffix = $":{NormalizeText(query)}";
        lock (_cacheGate)
        {
            foreach (var key in _searchCache.Keys
                         .Where(key => key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                _searchCache.Remove(key);
            }
        }
    }

    private async Task<AccountResolutionResult> ResolveNeteaseAccountAsync(
        OnlineProviderResolveContext context,
        CancellationToken cancellationToken)
    {
        if (context.Session is null)
        {
            return new AccountResolutionResult(null, AuthenticationFailed: false);
        }

        foreach (var (quality, bitrate) in QualityLevels(context.QualityPreference)
                     .Select(level => (level, level switch
                     {
                         OnlineQualityPreference.Lossless => 999000,
                         OnlineQualityPreference.High => 320000,
                         _ => 128000
                     })))
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://interface.music.163.com/api/song/enhance/player/url")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["ids"] = $"[{context.ProviderTrackId}]",
                    ["br"] = bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture)
                })
            };
            ApplyHeaders(request, AddHeaders(NeteaseHeaders, ("Cookie", context.Session.CookieHeader)));
            var response = await SendAccountJsonAsync(request, cancellationToken);
            using var document = response.Document;
            if (response.AuthenticationFailed || document is null)
            {
                return new AccountResolutionResult(null, response.AuthenticationFailed);
            }

            var rootCode = (int)ReadDouble(document.RootElement, "code");
            if (IsAuthenticationCode(rootCode))
            {
                return new AccountResolutionResult(null, AuthenticationFailed: true);
            }

            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var item = data.EnumerateArray().FirstOrDefault();
            var itemCode = (int)ReadDouble(item, "code");
            if (IsAuthenticationCode(itemCode))
            {
                return new AccountResolutionResult(null, AuthenticationFailed: true);
            }

            var url = NormalizeUrl(ReadString(item, "url"));
            if (!IsDirectPlayableUrl(url))
            {
                continue;
            }

            var actualBitrate = (int)ReadDouble(item, "br");
            var actualQuality = actualBitrate >= 900000
                || string.Equals(ReadString(item, "type"), "flac", StringComparison.OrdinalIgnoreCase)
                    ? OnlineQualityPreference.Lossless
                    : actualBitrate >= 256000
                        ? OnlineQualityPreference.High
                        : OnlineQualityPreference.Standard;
            var expiresIn = ReadDouble(item, "expi");
            var expiresAt = expiresIn > 0
                ? _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(expiresIn)
                : _timeProvider.GetUtcNow() + ResolutionLifetime;
            return new AccountResolutionResult(
                new OnlinePlaybackResolution(
                    url!,
                    "netease",
                    context.ProviderTrackId,
                    AuthenticatedPlaybackHeaders("netease", context.Session),
                    context.CoverUrl,
                    context.DurationSeconds,
                    Quality: actualQuality,
                    ExpiresAt: expiresAt,
                    IsAuthenticatedSource: true,
                    AccountSessionRevision: context.Session.SessionRevision),
                AuthenticationFailed: false);
        }

        return new AccountResolutionResult(null, AuthenticationFailed: false);
    }

    private async Task<AccountResolutionResult> ResolveQqAccountAsync(
        OnlineProviderResolveContext context,
        CancellationToken cancellationToken)
    {
        if (context.Session is null
            || !context.Session.Cookies.TryGetValue("uin", out var uin)
            || string.IsNullOrWhiteSpace(uin))
        {
            return new AccountResolutionResult(null, AuthenticationFailed: false);
        }

        foreach (var quality in QualityLevels(context.QualityPreference))
        {
            var filename = quality switch
            {
                OnlineQualityPreference.Lossless => $"F000{context.ProviderTrackId}{context.ProviderTrackId}.flac",
                OnlineQualityPreference.High => $"M800{context.ProviderTrackId}{context.ProviderTrackId}.mp3",
                _ => $"M500{context.ProviderTrackId}{context.ProviderTrackId}.mp3"
            };
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["comm"] = new Dictionary<string, object>
                {
                    ["ct"] = 24,
                    ["cv"] = 0,
                    ["uin"] = uin
                },
                ["req_0"] = new Dictionary<string, object>
                {
                    ["module"] = "vkey.GetVkeyServer",
                    ["method"] = "CgiGetVkey",
                    ["param"] = new Dictionary<string, object>
                    {
                        ["guid"] = "0",
                        ["songmid"] = new[] { context.ProviderTrackId },
                        ["songtype"] = new[] { 0 },
                        ["filename"] = new[] { filename },
                        ["uin"] = uin,
                        ["loginflag"] = 1,
                        ["platform"] = "20"
                    }
                }
            });
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://u.y.qq.com/cgi-bin/musicu.fcg")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            ApplyHeaders(request, AddHeaders(QqHeaders, ("Cookie", context.Session.CookieHeader)));
            var response = await SendAccountJsonAsync(request, cancellationToken);
            using var document = response.Document;
            if (response.AuthenticationFailed || document is null)
            {
                return new AccountResolutionResult(null, response.AuthenticationFailed);
            }

            var rootCode = (int)ReadDouble(document.RootElement, "code");
            if (!document.RootElement.TryGetProperty("req_0", out var requestResult))
            {
                if (IsAuthenticationCode(rootCode))
                {
                    return new AccountResolutionResult(null, AuthenticationFailed: true);
                }

                continue;
            }

            var requestCode = (int)ReadDouble(requestResult, "code");
            if (IsAuthenticationCode(rootCode) || IsAuthenticationCode(requestCode))
            {
                return new AccountResolutionResult(null, AuthenticationFailed: true);
            }

            if (!requestResult.TryGetProperty("data", out var data)
                || !data.TryGetProperty("midurlinfo", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var entry = entries.EnumerateArray().FirstOrDefault();
            var purl = ReadString(entry, "purl");
            if (string.IsNullOrWhiteSpace(purl))
            {
                continue;
            }

            var baseUrl = data.TryGetProperty("sip", out var sip)
                && sip.ValueKind == JsonValueKind.Array
                ? sip.EnumerateArray().Select(static item => item.GetString()).FirstOrDefault(IsDirectPlayableUrl)
                : null;
            var url = IsDirectPlayableUrl(purl)
                ? purl
                : string.IsNullOrWhiteSpace(baseUrl)
                    ? null
                    : $"{baseUrl.TrimEnd('/')}/{purl.TrimStart('/')}";
            if (!IsDirectPlayableUrl(url))
            {
                continue;
            }

            var actualFilename = ReadString(entry, "filename") ?? filename;
            var actualQuality = actualFilename.StartsWith("F000", StringComparison.OrdinalIgnoreCase)
                ? OnlineQualityPreference.Lossless
                : actualFilename.StartsWith("M800", StringComparison.OrdinalIgnoreCase)
                    ? OnlineQualityPreference.High
                    : OnlineQualityPreference.Standard;

            return new AccountResolutionResult(
                new OnlinePlaybackResolution(
                    url!,
                    "qq",
                    context.ProviderTrackId,
                    AuthenticatedPlaybackHeaders("qq", context.Session),
                    context.CoverUrl,
                    context.DurationSeconds,
                    Quality: actualQuality,
                    ExpiresAt: _timeProvider.GetUtcNow() + ResolutionLifetime,
                    IsAuthenticatedSource: true,
                    AccountSessionRevision: context.Session.SessionRevision),
                AuthenticationFailed: false);
        }

        return new AccountResolutionResult(null, AuthenticationFailed: false);
    }

    private async Task<AccountJsonResponse> SendAccountJsonAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new AccountJsonResponse(null, AuthenticationFailed: true);
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        return new AccountJsonResponse(
            await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token),
            AuthenticationFailed: false);
    }

    private static IReadOnlyList<OnlineQualityPreference> QualityLevels(OnlineQualityPreference preference) =>
        preference switch
        {
            OnlineQualityPreference.Lossless =>
                [OnlineQualityPreference.Lossless, OnlineQualityPreference.High, OnlineQualityPreference.Standard],
            OnlineQualityPreference.High =>
                [OnlineQualityPreference.High, OnlineQualityPreference.Standard],
            _ => [OnlineQualityPreference.Standard]
        };

    private static bool IsAuthenticationCode(int code) =>
        code is 301 or 302 or 1000 or 1001 or 2000 or 2001 or 4000 or -460;

    private static IReadOnlyDictionary<string, string> AuthenticatedPlaybackHeaders(
        string provider,
        OnlineProviderSession session) =>
        AddHeaders(PlaybackHeaders(provider), ("Cookie", session.CookieHeader));

    private sealed record CachedResolution(
        OnlinePlaybackResolution? Resolution,
        DateTimeOffset ExpiresAt);

    private sealed record CachedSearch(
        IReadOnlyList<OnlineProviderTrackModel> Results,
        DateTimeOffset ExpiresAt);

    private sealed record AccountResolutionResult(
        OnlinePlaybackResolution? Resolution,
        bool AuthenticationFailed);

    private sealed record AccountJsonResponse(
        JsonDocument? Document,
        bool AuthenticationFailed);

    private sealed class CandidateAttemptBudget
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _attempted;
        private readonly HashSet<string> _pendingAuthenticatedProviders;
        private TaskCompletionSource _changed = NewSignal();

        public CandidateAttemptBudget(
            HashSet<string> attempted,
            IEnumerable<string> authenticatedProviders)
        {
            _attempted = attempted;
            _pendingAuthenticatedProviders = new HashSet<string>(
                authenticatedProviders,
                StringComparer.OrdinalIgnoreCase);
        }

        public async Task<bool> TryAcquireAsync(
            OnlineProviderTrackModel candidate,
            CancellationToken cancellationToken)
        {
            var key = CandidateKey(candidate);
            while (true)
            {
                Task wait;
                lock (_gate)
                {
                    if (_attempted.Contains(key))
                    {
                        return false;
                    }

                    var authenticatedCandidate = _pendingAuthenticatedProviders.Contains(
                        NormalizeProvider(candidate.Provider));
                    var limit = authenticatedCandidate
                        ? 3
                        : 3 - _pendingAuthenticatedProviders.Count;
                    if (_attempted.Count < Math.Max(0, limit))
                    {
                        _attempted.Add(key);
                        return true;
                    }

                    if (_pendingAuthenticatedProviders.Count == 0 || _attempted.Count >= 3)
                    {
                        return false;
                    }

                    wait = _changed.Task;
                }

                await wait.WaitAsync(cancellationToken);
            }
        }

        public void CompleteAuthenticatedProvider(string provider)
        {
            lock (_gate)
            {
                if (!_pendingAuthenticatedProviders.Remove(NormalizeProvider(provider)))
                {
                    return;
                }

                var previous = _changed;
                _changed = NewSignal();
                previous.TrySetResult();
            }
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}
