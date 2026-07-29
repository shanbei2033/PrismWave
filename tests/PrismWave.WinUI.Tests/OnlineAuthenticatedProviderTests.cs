using System.Net;
using System.Text;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlineAuthenticatedProviderTests
{
    [Fact]
    public async Task AccountRevisionChange_NeverReusesPreviousAccountUrlOrCookie()
    {
        var handler = new AccountPlaybackHandler { ServeLossless = true };
        var account = FakeAccount.ForNetease("music-a", revision: 1);
        var service = new OnlineProviderService(new HttpClient(handler), accountService: account);

        var first = await service.ResolveAsync("netease", "123");
        account.SwitchNetease("music-b", revision: 2);
        var second = await service.ResolveAsync("netease", "123");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Contains("MUSIC_U=music-a", first.PlaybackHeaders!["Cookie"], StringComparison.Ordinal);
        Assert.Contains("MUSIC_U=music-b", second.PlaybackHeaders!["Cookie"], StringComparison.Ordinal);
        Assert.Equal("https://audio.test/netease-b.flac", second.PlaybackUrl);
        Assert.Equal(2, handler.NeteaseBitrates.Count);
    }

    [Fact]
    public async Task NeteaseAccount_FallsFromLosslessToHigh_AndCarriesRequiredHeaders()
    {
        var handler = new AccountPlaybackHandler();
        var account = FakeAccount.ForNetease("music-secret");
        var service = new OnlineProviderService(
            new HttpClient(handler),
            accountService: account,
            qualityPreference: () => OnlineQualityPreference.Lossless);

        var resolution = await service.ResolveAsync("netease", "123");

        Assert.NotNull(resolution);
        Assert.Equal(OnlineQualityPreference.High, resolution.Quality);
        Assert.Equal("https://audio.test/netease-high.mp3", resolution.PlaybackUrl);
        Assert.Equal(new[] { 999000, 320000 }, handler.NeteaseBitrates);
        Assert.Contains("MUSIC_U=music-secret", handler.RequestCookies[0], StringComparison.Ordinal);
        Assert.Contains("MUSIC_U=music-secret", resolution.PlaybackHeaders!["Cookie"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task NeteaseAccount_MapsActual128KResponseToStandard()
    {
        var handler = new AccountPlaybackHandler { ServeLossless = true, LosslessActualBitrate = 128000 };
        var service = new OnlineProviderService(
            new HttpClient(handler),
            accountService: FakeAccount.ForNetease("music-secret"));

        var resolution = await service.ResolveAsync("netease", "123");

        Assert.NotNull(resolution);
        Assert.Equal(OnlineQualityPreference.Standard, resolution.Quality);
    }

    [Fact]
    public async Task QqAccount_FallsAcrossFlacHighAndStandard_InRequestedOrder()
    {
        var handler = new AccountPlaybackHandler();
        var account = FakeAccount.ForQq("10001", "qq-secret");
        var service = new OnlineProviderService(
            new HttpClient(handler),
            accountService: account,
            qualityPreference: () => OnlineQualityPreference.Lossless);

        var resolution = await service.ResolveAsync("qq", "song-mid");

        Assert.NotNull(resolution);
        Assert.Equal(OnlineQualityPreference.Standard, resolution.Quality);
        Assert.Equal("https://stream.test/qq-standard.mp3", resolution.PlaybackUrl);
        Assert.Equal(
            new[] { "F000song-midsong-mid.flac", "M800song-midsong-mid.mp3", "M500song-midsong-mid.mp3" },
            handler.QqFilenames);
        Assert.All(handler.QqBodies, body =>
        {
            Assert.Contains("\"uin\":\"10001\"", body, StringComparison.Ordinal);
            Assert.Contains("\"loginflag\":1", body, StringComparison.Ordinal);
        });
        Assert.Contains("qqmusic_key=qq-secret", resolution.PlaybackHeaders!["Cookie"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountAuthenticationFailure_IsHandledOnlyOnce_ThenAnonymousFallbackContinues()
    {
        var handler = new AccountPlaybackHandler { RejectNeteaseAuthentication = true };
        var account = FakeAccount.ForNetease("expired-secret");
        var anonymous = new SuccessfulAdapter("netease");
        var service = new OnlineProviderService(
            new HttpClient(handler),
            adapters: [anonymous],
            accountService: account);

        var resolution = await service.ResolveAsync("netease", "123");

        Assert.NotNull(resolution);
        Assert.Equal("https://audio.test/anonymous.mp3", resolution.PlaybackUrl);
        Assert.Equal(1, account.AuthenticationFailureCalls);
        Assert.Equal(2, handler.NeteaseBitrates.Count);
    }

    [Fact]
    public async Task AccountLoadFailure_DoesNotBlockAnonymousAdapter()
    {
        var anonymous = new SuccessfulAdapter("qq");
        var account = new FakeAccount { ThrowOnGetSession = true };
        var service = new OnlineProviderService(adapters: [anonymous], accountService: account);

        var resolution = await service.ResolveAsync("qq", "song-mid");

        Assert.NotNull(resolution);
        Assert.Equal("https://audio.test/anonymous.mp3", resolution.PlaybackUrl);
        Assert.Equal(1, anonymous.ResolveCalls);
    }

    [Fact]
    public async Task ProviderExpiration_IsInvalidatedThirtySecondsEarly()
    {
        var clock = new TestTimeProvider();
        var handler = new AccountPlaybackHandler { NeteaseExpirySeconds = 40, ServeLossless = true };
        var service = new OnlineProviderService(
            new HttpClient(handler),
            clock,
            accountService: FakeAccount.ForNetease("music-secret"));

        await service.ResolveAsync("netease", "123");
        clock.Advance(TimeSpan.FromSeconds(9));
        await service.ResolveAsync("netease", "123");
        Assert.Single(handler.NeteaseBitrates);

        clock.Advance(TimeSpan.FromSeconds(2));
        await service.ResolveAsync("netease", "123");
        Assert.Equal(2, handler.NeteaseBitrates.Count);
    }

    [Fact]
    public async Task ProviderLogs_DoNotContainCookiesTokensOrUin()
    {
        var lines = new List<string>();
        EventHandler<string> capture = (_, line) => lines.Add(line);
        StartupLog.LineWritten += capture;
        try
        {
            var handler = new AccountPlaybackHandler { ServeLossless = true };
            var service = new OnlineProviderService(
                new HttpClient(handler),
                accountService: FakeAccount.ForNetease("never-log-this-token"));

            await service.ResolveAsync("netease", "123");
        }
        finally
        {
            StartupLog.LineWritten -= capture;
        }

        Assert.DoesNotContain(lines, line => line.Contains("never-log-this-token", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("Cookie", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class AccountPlaybackHandler : HttpMessageHandler
    {
        public List<int> NeteaseBitrates { get; } = [];
        public List<string> QqFilenames { get; } = [];
        public List<string> QqBodies { get; } = [];
        public List<string> RequestCookies { get; } = [];
        public bool RejectNeteaseAuthentication { get; init; }
        public bool ServeLossless { get; init; }
        public int NeteaseExpirySeconds { get; init; } = 120;
        public int LosslessActualBitrate { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestCookies.Add(request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join(";", values)
                : string.Empty);

            if (request.Method == HttpMethod.Post && request.RequestUri!.Host == "interface.music.163.com")
            {
                var bitrate = body.Contains("999000", StringComparison.Ordinal) ? 999000
                    : body.Contains("320000", StringComparison.Ordinal) ? 320000
                    : 128000;
                NeteaseBitrates.Add(bitrate);
                if (RejectNeteaseAuthentication)
                {
                    return Json("{\"code\":301}");
                }

                var url = ServeLossless && bitrate == 999000
                    ? (RequestCookies[^1].Contains("music-b", StringComparison.Ordinal)
                        ? "https://audio.test/netease-b.flac"
                        : "https://audio.test/netease-lossless.flac")
                    : bitrate == 320000
                        ? "https://audio.test/netease-high.mp3"
                        : null;
                var reportedBitrate = bitrate == 999000 && LosslessActualBitrate > 0
                    ? LosslessActualBitrate
                    : bitrate;
                return Json(
                    "{\"code\":200,\"data\":[{\"url\":"
                    + (url is null ? "null" : $"\"{url}\"")
                    + $",\"br\":{reportedBitrate},\"type\":\"{(reportedBitrate >= 900000 ? "flac" : "mp3")}\",\"expi\":{NeteaseExpirySeconds}}}]}}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri!.Host == "u.y.qq.com")
            {
                QqBodies.Add(body);
                var filename = new[]
                {
                    "F000song-midsong-mid.flac",
                    "M800song-midsong-mid.mp3",
                    "M500song-midsong-mid.mp3"
                }.First(body.Contains);
                QqFilenames.Add(filename);
                var purl = filename.StartsWith("M500", StringComparison.Ordinal)
                    ? "qq-standard.mp3"
                    : string.Empty;
                return Json(
                    "{\"code\":0,\"req_0\":{\"code\":0,\"data\":{\"sip\":[\"https://stream.test/\"],\"midurlinfo\":[{"
                    + $"\"filename\":\"{filename}\",\"purl\":\"{purl}\""
                    + "}]}}}");
            }

            return Json("{}");
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class SuccessfulAdapter(string provider) : IOnlineMusicProviderAdapter
    {
        public string ProviderKey { get; } = provider;
        public int ResolveCalls { get; private set; }

        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OnlineProviderTrackModel>>([]);

        public Task<OnlinePlaybackResolution?> ResolveAsync(
            OnlineProviderResolveContext context,
            CancellationToken cancellationToken,
            bool skipOfficialEndpoint = false)
        {
            ResolveCalls++;
            return Task.FromResult<OnlinePlaybackResolution?>(new(
                "https://audio.test/anonymous.mp3",
                ProviderKey,
                context.ProviderTrackId,
                Quality: OnlineQualityPreference.Standard));
        }
    }

    private sealed class FakeAccount : IOnlineAccountService
    {
        private readonly Dictionary<string, OnlineProviderSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
        public bool ThrowOnGetSession { get; init; }
        public int AuthenticationFailureCalls { get; private set; }
        public event EventHandler<OnlineAccountSnapshot>? AccountChanged;

        public static FakeAccount ForNetease(string musicU, long revision = 1)
        {
            var result = new FakeAccount();
            result._sessions["netease"] = new OnlineProviderSession(
                "netease",
                new Dictionary<string, string> { ["MUSIC_U"] = musicU },
                revision);
            return result;
        }

        public void SwitchNetease(string musicU, long revision)
        {
            _sessions["netease"] = new OnlineProviderSession(
                "netease",
                new Dictionary<string, string> { ["MUSIC_U"] = musicU },
                revision);
            AccountChanged?.Invoke(this, new OnlineAccountSnapshot("netease", OnlineProviderAuthState.Authenticated));
        }

        public static FakeAccount ForQq(string uin, string key)
        {
            var result = new FakeAccount();
            result._sessions["qq"] = new OnlineProviderSession(
                "qq",
                new Dictionary<string, string>
                {
                    ["uin"] = uin,
                    ["qqmusic_key"] = key,
                    ["qm_keyst"] = key
                });
            return result;
        }

        public Task<OnlineProviderSession?> GetSessionAsync(string providerKey, CancellationToken cancellationToken)
        {
            if (ThrowOnGetSession)
            {
                throw new HttpRequestException("account unavailable");
            }

            return Task.FromResult<OnlineProviderSession?>(_sessions.GetValueOrDefault(providerKey));
        }

        public Task<OnlineProviderSession?> HandleAuthenticationFailureAsync(string providerKey, CancellationToken cancellationToken)
        {
            AuthenticationFailureCalls++;
            return Task.FromResult<OnlineProviderSession?>(_sessions.GetValueOrDefault(providerKey));
        }

        public OnlineAccountSnapshot GetSnapshot(string providerKey) => new(
            providerKey,
            _sessions.ContainsKey(providerKey) ? OnlineProviderAuthState.Authenticated : OnlineProviderAuthState.Disconnected);
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
