using PrismWave_WinUI.Models;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlineProviderArchitectureTests
{
    [Fact]
    public async Task InjectedAdapter_SeparatesMetadataSearchFromUrlResolution()
    {
        var adapter = new FakeAdapter("fake");
        var service = new OnlineProviderService(adapters: [adapter]);

        var searched = await service.SearchAsync("Song Artist");

        var track = Assert.Single(searched);
        Assert.Null(track.DirectAudioUrl);
        Assert.Equal(1, adapter.SearchCalls);
        Assert.Equal(0, adapter.ResolveCalls);

        var resolved = await service.ResolveAsync("fake", track.ProviderTrackId);

        Assert.NotNull(resolved);
        Assert.Equal("https://audio.test/fake.mp3", resolved.PlaybackUrl);
        Assert.Equal(1, adapter.ResolveCalls);
    }

    [Fact]
    public async Task SearchCache_ExpiresAfterFiveMinutes()
    {
        var clock = new TestTimeProvider();
        var adapter = new FakeAdapter("fake");
        var service = new OnlineProviderService(timeProvider: clock, adapters: [adapter]);

        await service.SearchAsync("Song Artist");
        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        await service.SearchAsync("Song Artist");
        Assert.Equal(1, adapter.SearchCalls);

        clock.Advance(TimeSpan.FromSeconds(2));
        await service.SearchAsync("Song Artist");
        Assert.Equal(2, adapter.SearchCalls);
    }

    [Fact]
    public void HealthTracker_CoolsAfterThreeProtocolFailures_ThenRecovers()
    {
        var clock = new TestTimeProvider();
        var health = new OnlineProviderHealthTracker(clock);

        health.ReportFailure("fake", OnlineProviderFailureKind.NetworkOrProtocol);
        health.ReportFailure("fake", OnlineProviderFailureKind.NetworkOrProtocol);
        Assert.True(health.CanRequest("fake"));

        health.ReportFailure("fake", OnlineProviderFailureKind.NetworkOrProtocol);
        Assert.False(health.CanRequest("fake"));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(health.CanRequest("fake"));

        health.ReportFailure("fake", OnlineProviderFailureKind.NetworkOrProtocol);
        Assert.True(health.CanRequest("fake"));

        health.ReportSuccess("fake");
    }

    [Fact]
    public void HealthTracker_DoesNotCountUnavailableTracksOrCancellation()
    {
        var health = new OnlineProviderHealthTracker(new TestTimeProvider());
        for (var index = 0; index < 5; index++)
        {
            health.ReportFailure("fake", OnlineProviderFailureKind.TrackUnavailable);
            health.ReportFailure("fake", OnlineProviderFailureKind.Cancelled);
        }

        Assert.True(health.CanRequest("fake"));
    }

    [Fact]
    public async Task CoolingProvider_IsSkippedWithoutBlockingHealthyAdapter()
    {
        var clock = new TestTimeProvider();
        var health = new OnlineProviderHealthTracker(clock);
        health.ReportFailure("broken", OnlineProviderFailureKind.NetworkOrProtocol);
        health.ReportFailure("broken", OnlineProviderFailureKind.NetworkOrProtocol);
        health.ReportFailure("broken", OnlineProviderFailureKind.NetworkOrProtocol);
        var broken = new FakeAdapter("broken");
        var healthy = new FakeAdapter("healthy");
        var service = new OnlineProviderService(
            timeProvider: clock,
            adapters: [broken, healthy],
            healthTracker: health);

        var results = await service.SearchAsync("Song Artist");

        Assert.Equal(0, broken.SearchCalls);
        Assert.Equal(1, healthy.SearchCalls);
        Assert.Single(results);
    }

    [Fact]
    public async Task ProviderScheduler_AttemptsAtMostThreeDistinctCandidates()
    {
        var adapters = Enumerable.Range(0, 5)
            .Select(index => new NullResolutionAdapter($"fake-{index}"))
            .ToList();
        var service = new OnlineProviderService(adapters: adapters);
        var track = new TrackModel(
            "requested",
            "online://online/requested",
            "Song",
            "Artist",
            "Album",
            "03:00",
            null,
            true,
            "Online",
            "online://online/requested",
            DurationSeconds: 180);

        var result = await service.SearchAndResolveAsync(track);

        Assert.Null(result);
        Assert.Equal(3, adapters.Sum(adapter => adapter.ResolveCalls));
    }

    [Fact]
    public async Task RepeatedAdapterProtocolErrors_OpenHealthCircuitAfterThirdFailure()
    {
        var adapter = new ThrowingAdapter();
        var service = new OnlineProviderService(adapters: [adapter]);

        await service.SearchAsync("one");
        await service.SearchAsync("two");
        await service.SearchAsync("three");
        await service.SearchAsync("four");

        Assert.Equal(3, adapter.SearchCalls);
    }

    [Fact]
    public async Task ProviderExceptionLog_NeverIncludesMessageUriQueryOrCredentials()
    {
        var lines = new List<string>();
        EventHandler<string> capture = (_, line) => lines.Add(line);
        StartupLog.LineWritten += capture;
        try
        {
            var service = new OnlineProviderService(adapters: [new SecretThrowingAdapter()]);
            await service.ResolveAsync("secret", "track");
        }
        finally
        {
            StartupLog.LineWritten -= capture;
        }

        var log = string.Join("\n", lines);
        Assert.DoesNotContain("token-value", log, StringComparison.Ordinal);
        Assert.DoesNotContain("uin=10001", log, StringComparison.Ordinal);
        Assert.DoesNotContain("https://signed.test/play?", log, StringComparison.Ordinal);
        Assert.Contains("type=HttpRequestException", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoggedInCapableLosslessProvider_GetsFiniteGraceOverFasterAnonymousStandard()
    {
        var fast = new DelayedResolutionAdapter("audius", TimeSpan.Zero, OnlineQualityPreference.Standard, false);
        var account = new DisconnectedAccountService();
        var lossless = new DelayedResolutionAdapter("netease", TimeSpan.FromMilliseconds(80), OnlineQualityPreference.Lossless, true);
        var service = new OnlineProviderService(adapters: [fast, lossless], accountService: account);
        var track = new TrackModel(
            "id", "online://online/id", "Song", "Artist", "Album", "03:00", null,
            true, "Online", "online://online/id", DurationSeconds: 180);

        var result = await service.SearchAndResolveAsync(track);

        Assert.NotNull(result);
        Assert.Equal("netease", result.Provider);
        Assert.Equal(OnlineQualityPreference.Lossless, result.Quality);
        Assert.True(result.IsAuthenticatedSource);
    }

    [Fact]
    public async Task NoAuthenticatedAccount_ReturnsFirstAnonymousResultWithoutGraceDelay()
    {
        var fast = new DelayedResolutionAdapter("audius", TimeSpan.Zero, OnlineQualityPreference.Standard, false);
        var slow = new DelayedResolutionAdapter("netease", TimeSpan.FromMilliseconds(500), OnlineQualityPreference.Lossless, false);
        var service = new OnlineProviderService(adapters: [fast, slow], accountService: new DisconnectedAccountService(authenticated: false));
        var track = new TrackModel("id", "online://online/id", "Song", "Artist", "Album", "03:00", null,
            true, "Online", "online://online/id", DurationSeconds: 180);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await service.SearchAndResolveAsync(track);

        stopwatch.Stop();
        Assert.Equal("audius", result?.Provider);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(150), $"Unexpected grace: {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task AccountEvent_ClearsAuthenticatedSearchResolutionCache()
    {
        var adapter = new MutableAuthenticatedAdapter();
        var account = new MutableAuthenticatedAccount();
        var service = new OnlineProviderService(adapters: [adapter], accountService: account);
        var track = new TrackModel("id", "online://online/id", "Song", "Artist", "Album", "03:00", null,
            true, "Online", "online://online/id", DurationSeconds: 180);

        var first = await service.SearchAndResolveAsync(track);
        adapter.Version = "b";
        account.RaiseChanged();
        var second = await service.SearchAndResolveAsync(track);

        Assert.Equal("https://audio.test/a.mp3", first?.PlaybackUrl);
        Assert.Equal("https://audio.test/b.mp3", second?.PlaybackUrl);
        Assert.Equal(2, adapter.ResolveCalls);
    }

    private sealed class FakeAdapter(string providerKey) : IOnlineMusicProviderAdapter
    {
        public string ProviderKey { get; } = providerKey;
        public int SearchCalls { get; private set; }
        public int ResolveCalls { get; private set; }

        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            SearchCalls++;
            return Task.FromResult<IReadOnlyList<OnlineProviderTrackModel>>(
            [
                new OnlineProviderTrackModel(
                    ProviderKey,
                    "track-1",
                    "Song",
                    "Artist",
                    "Album",
                    180,
                    null)
            ]);
        }

        public Task<OnlinePlaybackResolution?> ResolveAsync(
            OnlineProviderResolveContext context,
            CancellationToken cancellationToken)
        {
            ResolveCalls++;
            return Task.FromResult<OnlinePlaybackResolution?>(new(
                "https://audio.test/fake.mp3",
                ProviderKey,
                context.ProviderTrackId,
                CoverUrl: context.CoverUrl,
                DurationSeconds: context.DurationSeconds,
                Quality: context.QualityPreference));
        }
    }

    private sealed class NullResolutionAdapter(string providerKey) : IOnlineMusicProviderAdapter
    {
        public string ProviderKey { get; } = providerKey;
        public int ResolveCalls { get; private set; }

        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OnlineProviderTrackModel>>(
            [
                new(ProviderKey, "track", "Song", "Artist", "Album", 180, null)
            ]);

        public Task<OnlinePlaybackResolution?> ResolveAsync(
            OnlineProviderResolveContext context,
            CancellationToken cancellationToken)
        {
            ResolveCalls++;
            return Task.FromResult<OnlinePlaybackResolution?>(null);
        }
    }

    private sealed class ThrowingAdapter : IOnlineMusicProviderAdapter
    {
        public string ProviderKey => "broken";
        public int SearchCalls { get; private set; }
        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            SearchCalls++;
            throw new HttpRequestException("protocol failure");
        }

        public Task<OnlinePlaybackResolution?> ResolveAsync(OnlineProviderResolveContext context, CancellationToken cancellationToken) =>
            Task.FromResult<OnlinePlaybackResolution?>(null);
    }

    private sealed class SecretThrowingAdapter : IOnlineMusicProviderAdapter
    {
        public string ProviderKey => "secret";
        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OnlineProviderTrackModel>>([]);
        public Task<OnlinePlaybackResolution?> ResolveAsync(OnlineProviderResolveContext context, CancellationToken cancellationToken) =>
            throw new HttpRequestException(
                "Cookie=MUSIC_U=token-value uin=10001 https://signed.test/play?token=token-value");
    }

    private sealed class DelayedResolutionAdapter(
        string provider,
        TimeSpan delay,
        OnlineQualityPreference quality,
        bool authenticated) : IOnlineMusicProviderAdapter
    {
        public string ProviderKey { get; } = provider;
        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OnlineProviderTrackModel>>([new(ProviderKey, "candidate", "Song", "Artist", "Album", 180, null)]);
        public async Task<OnlinePlaybackResolution?> ResolveAsync(OnlineProviderResolveContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new OnlinePlaybackResolution(
                $"https://audio.test/{ProviderKey}.mp3", ProviderKey, context.ProviderTrackId,
                Quality: quality, IsAuthenticatedSource: authenticated,
                AccountSessionRevision: authenticated ? 1 : null);
        }
    }

    private sealed class DisconnectedAccountService(bool authenticated = true) : IOnlineAccountService
    {
        public event EventHandler<OnlineAccountSnapshot>? AccountChanged;
        public Task<OnlineProviderSession?> GetSessionAsync(string providerKey, CancellationToken cancellationToken) => Task.FromResult<OnlineProviderSession?>(null);
        public Task<OnlineProviderSession?> HandleAuthenticationFailureAsync(string providerKey, CancellationToken cancellationToken) => Task.FromResult<OnlineProviderSession?>(null);
        public OnlineAccountSnapshot GetSnapshot(string providerKey) => new(
            providerKey,
            authenticated && providerKey == "netease" ? OnlineProviderAuthState.Authenticated : OnlineProviderAuthState.Disconnected);
        public Task<OnlineLoginChallenge> CreateChallengeAsync(string providerKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlineAccountSnapshot> PollAsync(string providerKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateSessionAsync(string providerKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SignOutAsync(string providerKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MutableAuthenticatedAdapter : IOnlineMusicProviderAdapter
    {
        public string ProviderKey => "netease";
        public string Version { get; set; } = "a";
        public int ResolveCalls { get; private set; }
        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OnlineProviderTrackModel>>([new("netease", "candidate", "Song", "Artist", "Album", 180, null)]);
        public Task<OnlinePlaybackResolution?> ResolveAsync(OnlineProviderResolveContext context, CancellationToken cancellationToken)
        {
            ResolveCalls++;
            return Task.FromResult<OnlinePlaybackResolution?>(new(
                $"https://audio.test/{Version}.mp3", "netease", context.ProviderTrackId,
                IsAuthenticatedSource: true, AccountSessionRevision: 1));
        }
    }

    private sealed class MutableAuthenticatedAccount : IOnlineAccountService
    {
        public event EventHandler<OnlineAccountSnapshot>? AccountChanged;
        public void RaiseChanged() => AccountChanged?.Invoke(this, GetSnapshot("netease"));
        public OnlineAccountSnapshot GetSnapshot(string providerKey) => new(providerKey, OnlineProviderAuthState.Authenticated);
        public Task<OnlineProviderSession?> GetSessionAsync(string providerKey, CancellationToken cancellationToken) => Task.FromResult<OnlineProviderSession?>(null);
        public Task<OnlineProviderSession?> HandleAuthenticationFailureAsync(string providerKey, CancellationToken cancellationToken) => Task.FromResult<OnlineProviderSession?>(null);
        public Task<OnlineLoginChallenge> CreateChallengeAsync(string providerKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlineAccountSnapshot> PollAsync(string providerKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateSessionAsync(string providerKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SignOutAsync(string providerKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
