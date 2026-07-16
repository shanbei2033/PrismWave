using System.Net;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlineProviderServiceTests
{
    [Fact]
    public void PlaybackCandidateScore_RejectsExactTitleFromWrongArtist()
    {
        var method = typeof(OnlineProviderService).GetMethod(
            "ScoreCandidate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var requested = CreateGenericOnlineTrack();
        var wrongArtist = new OnlineProviderTrackModel(
            "netease",
            "wrong",
            "Song",
            "Another Artist",
            "Album",
            120,
            "https://cover.test/wrong.jpg");

        Assert.NotNull(method);
        var score = Assert.IsType<int>(method.Invoke(null, new object[] { requested, wrongArtist }));
        Assert.True(score < 44, $"Wrong-artist candidate unexpectedly passed with score {score}.");
    }

    [Fact]
    public void PlaybackCandidateScore_RejectsUnrequestedLiveVariant()
    {
        var method = typeof(OnlineProviderService).GetMethod(
            "ScoreCandidate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var requested = CreateGenericOnlineTrack();
        var live = new OnlineProviderTrackModel(
            "netease",
            "live",
            "Song (Live)",
            "Artist",
            "Live Album",
            120,
            "https://cover.test/live.jpg");

        Assert.NotNull(method);
        var score = Assert.IsType<int>(method.Invoke(null, new object[] { requested, live }));
        Assert.True(score < 44, $"Unrequested live candidate unexpectedly passed with score {score}.");
    }

    [Fact]
    public void PlaybackCandidateScore_RejectsDurationDifferenceAboveEightSeconds()
    {
        var method = typeof(OnlineProviderService).GetMethod(
            "ScoreCandidate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var requested = CreateGenericOnlineTrack() with { DurationSeconds = 120 };
        var differentDuration = new OnlineProviderTrackModel(
            "netease", "different", "Song", "Artist", "Album", 129, null);

        Assert.NotNull(method);
        var score = Assert.IsType<int>(method.Invoke(null, new object[] { requested, differentDuration }));
        Assert.True(score < 44, $"Duration-mismatched candidate unexpectedly passed with score {score}.");
    }

    [Fact]
    public void PlaybackCandidateScore_AcceptsEquivalentRemasterFormatting()
    {
        var method = typeof(OnlineProviderService).GetMethod(
            "ScoreCandidate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var requested = CreateGenericOnlineTrack() with { Title = "Dreams - 2004 Remaster" };
        var candidate = new OnlineProviderTrackModel(
            "netease",
            "remaster",
            "Dreams (2004 Remastered)",
            "Artist",
            "Album",
            120,
            "https://cover.test/remaster.jpg");

        Assert.NotNull(method);
        var score = Assert.IsType<int>(method.Invoke(null, new object[] { requested, candidate }));
        Assert.True(score >= 44, $"Equivalent remaster candidate was rejected with score {score}.");
    }
    [Fact]
    public async Task SearchAsync_MergesSevenMusicProvidersWithoutVideoSources()
    {
        var handler = new ProviderStubHandler();
        var service = new OnlineProviderService(new HttpClient(handler));

        var results = await service.SearchAsync("Song Artist");

        var providers = results
            .Select(result => result.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(provider => provider, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            new[] { "audius", "kugou", "kuwo", "migu", "netease", "qq", "taihe" },
            providers);
        Assert.All(results, result => Assert.StartsWith($"online://{result.Provider}/", result.Descriptor));
        Assert.DoesNotContain(handler.Requests, request =>
            request.Host.Contains("youtube", StringComparison.OrdinalIgnoreCase)
            || request.Host.Contains("bilibili", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Requests, request =>
            request.Host == "music.163.com"
            && request.Path == "/api/cloudsearch/pc");
        Assert.Contains(results, result => result.Provider == "netease" && result.DirectAudioUrl is null);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNeteaseMetadataWithoutResolvingCandidates()
    {
        var handler = new NeteaseCandidateResolutionHandler(blockResolutions: true);
        var service = new OnlineProviderService(new HttpClient(handler));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var results = await service.SearchAsync("Song Artist", cancellation.Token);

        Assert.Equal(4, results.Count);
        Assert.All(results, result =>
        {
            Assert.Equal("netease", result.Provider);
            Assert.Null(result.DirectAudioUrl);
        });
        Assert.Equal(0, handler.ResolutionRequestCount);
    }

    [Fact]
    public async Task SearchAndResolveAsync_ResolvesOnlySelectedNeteaseCandidateOnce()
    {
        var handler = new NeteaseCandidateResolutionHandler(blockResolutions: false);
        var service = new OnlineProviderService(new HttpClient(handler));

        var resolved = await service.SearchAndResolveAsync(CreateGenericOnlineTrack());

        Assert.NotNull(resolved);
        Assert.Equal("netease", resolved.Provider);
        Assert.Equal("123", resolved.ProviderTrackId);
        Assert.Equal("https://audio.test/netease-123.mp3", resolved.PlaybackUrl);
        Assert.Equal(1, handler.ResolutionRequestCount);
    }

    [Fact]
    public async Task SearchAsync_AcceptsTaiheArrayRootPayload()
    {
        var service = new OnlineProviderService(new HttpClient(new TaiheArrayRootHandler()));

        var results = await service.SearchAsync("Song Artist");

        var result = Assert.Single(results);
        Assert.Equal("taihe", result.Provider);
        Assert.Equal("th-array", result.ProviderTrackId);
        Assert.Equal("Song", result.Title);
        Assert.Equal("Artist", result.Artist);
    }

    [Fact]
    public async Task ResolveAsync_AcceptsTaiheArrayRootPayload()
    {
        var service = new OnlineProviderService(new HttpClient(new TaiheArrayRootHandler()));

        var resolved = await service.ResolveAsync("taihe", "th-array");

        Assert.NotNull(resolved);
        Assert.Equal("https://audio.test/taihe-array.mp3", resolved.PlaybackUrl);
        Assert.Equal("th-array", resolved.ProviderTrackId);
    }

    [Fact]
    public async Task SearchAsync_IgnoresMiguHtmlWithoutBlockingOtherProviders()
    {
        var service = new OnlineProviderService(new HttpClient(new MiguHtmlHandler()));
        var stopwatch = Stopwatch.StartNew();

        var results = await service.SearchAsync("Song Artist");

        stopwatch.Stop();
        var result = Assert.Single(results);
        Assert.Equal("audius", result.Provider);
        Assert.DoesNotContain(results, item => item.Provider == "migu");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SearchAndResolveAsync_DoesNotResolveDuplicateProviderRowsTwiceAfterTimeout()
    {
        var handler = new DuplicateCandidateAttemptHandler(
            duplicateRows: true,
            collaborationTitle: false,
            blockResolutions: true);
        var service = new OnlineProviderService(new HttpClient(handler));

        var resolved = await service.SearchAndResolveAsync(CreateGenericOnlineTrack());

        Assert.Null(resolved);
        Assert.Equal(1, handler.ResolutionRequestCount);
    }

    [Fact]
    public async Task SearchAndResolveAsync_DoesNotRetryNullCandidateInCollaborationFallback()
    {
        var timeProvider = new ManualTimeProvider();
        var handler = new DuplicateCandidateAttemptHandler(
            duplicateRows: false,
            collaborationTitle: true,
            blockResolutions: false,
            timeProvider);
        var service = new OnlineProviderService(new HttpClient(handler), timeProvider);
        var track = CreateGenericOnlineTrack() with { Title = "Song + Guest" };

        var resolved = await service.SearchAndResolveAsync(track);

        Assert.Null(resolved);
        Assert.Equal(2, handler.SearchRequestCount);
        Assert.Equal(1, handler.ResolutionRequestCount);
    }

    [Fact]
    public async Task SearchAndResolveAsync_DoesNotRetryTimedOutCandidateInCollaborationFallback()
    {
        var handler = new DuplicateCandidateAttemptHandler(
            duplicateRows: false,
            collaborationTitle: true,
            blockResolutions: true);
        var service = new OnlineProviderService(new HttpClient(handler));
        var track = CreateGenericOnlineTrack() with { Title = "Song + Guest" };

        var resolved = await service.SearchAndResolveAsync(track);

        Assert.Null(resolved);
        Assert.Equal(2, handler.SearchRequestCount);
        Assert.Equal(1, handler.ResolutionRequestCount);
    }

    [Theory]
    [InlineData("audius", "aud-1", "https://api.audius.co/v1/tracks/aud-1/stream")]
    [InlineData("netease", "123", "https://audio.test/netease.mp3")]
    [InlineData("kuwo", "456", "https://audio.test/kuwo.mp3")]
    [InlineData("migu", "mg-1", "https://audio.test/migu.mp3")]
    [InlineData("qq", "qq-1", "http://ws.stream.qqmusic.qq.com/C400qq-1.m4a")]
    [InlineData("kugou", "kg-1", "https://audio.test/kugou.mp3")]
    [InlineData("taihe", "th-1", "https://audio.test/taihe.mp3")]
    public async Task ResolveAsync_RoutesPinnedProviderDescriptor(
        string provider,
        string id,
        string expectedUrl)
    {
        var service = new OnlineProviderService(new HttpClient(new ProviderStubHandler()));
        var resolver = new OnlinePlaybackResolver(service);
        var descriptor = $"online://{provider}/{Uri.EscapeDataString(id)}";
        var track = new TrackModel(
            descriptor,
            descriptor,
            "Song",
            "Artist",
            "Album",
            "02:00",
            "https://cover.test/song.jpg",
            true,
            provider,
            descriptor,
            DurationSeconds: 120);

        var resolved = await resolver.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(provider, resolved.Provider);
        Assert.Equal(expectedUrl, resolved.PlaybackUrl);
        Assert.Equal(id, resolved.ProviderTrackId);
        if (provider is "migu" or "qq" or "kugou" or "taihe")
        {
            Assert.NotNull(resolved.PlaybackHeaders);
            Assert.Contains("Referer", resolved.PlaybackHeaders.Keys, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ResolveAsync_FallsBackAcrossProvidersWhenPinnedSourceIsUnavailable()
    {
        var handler = new ProviderStubHandler(qqPlayable: false, onlyAudiusSearch: true);
        var service = new OnlineProviderService(new HttpClient(handler));
        var resolver = new OnlinePlaybackResolver(service);
        const string descriptor = "online://qq/missing";
        var track = new TrackModel(
            descriptor,
            descriptor,
            "Song",
            "Artist",
            "Album",
            "02:00",
            null,
            true,
            "qq",
            descriptor,
            DurationSeconds: 120);

        var resolved = await resolver.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal("audius", resolved.Provider);
        Assert.Equal("aud-1", resolved.ProviderTrackId);
        Assert.Equal("https://api.audius.co/v1/tracks/aud-1/stream", resolved.PlaybackUrl);
    }

    [Fact]
    public async Task ResolveNextAsync_UsesOriginalDescriptorAndExcludesFailedCandidateAcrossBothQueryRaces()
    {
        var handler = new ExcludedCandidateFallbackHandler();
        var service = new OnlineProviderService(new HttpClient(handler));
        var resolver = new OnlinePlaybackResolver(service);
        const string descriptor = "online://netease/excluded";
        var track = new TrackModel(
            descriptor,
            descriptor,
            "Song + Guest",
            "Artist",
            "Album",
            "02:00",
            null,
            true,
            "netease",
            "https://failed.test/song.mp3?token=do-not-log",
            DurationSeconds: 120);
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "netease:excluded"
        };

        var resolved = await resolver.ResolveNextAsync(track, exclusions, attempt: 2);

        Assert.NotNull(resolved);
        Assert.Equal("netease:alternative", resolved.CandidateKey);
        Assert.Equal("alternative", resolved.ProviderTrackId);
        Assert.Equal("https://audio.test/alternative.mp3", resolved.PlaybackUrl);
        Assert.Equal(2, resolved.Attempt);
        Assert.Equal(OnlineQualityPreference.Lossless, resolved.Quality);
        Assert.NotNull(resolved.ExpiresAt);
        Assert.Equal(2, handler.SearchRequestCount);
        Assert.Equal(new[] { "alternative" }, handler.ResolvedProviderTrackIds);
    }

    [Fact]
    public async Task ResolveNextAsync_ExcludedHttpPathContinuesWithProviderSearch()
    {
        var service = new OnlineProviderService(new HttpClient(new ProviderStubHandler()));
        var resolver = new OnlinePlaybackResolver(service);
        const string failedUrl = "https://failed.test/direct.mp3?token=private";
        var track = new TrackModel(
            failedUrl,
            failedUrl,
            "Song",
            "Artist",
            "Album",
            "02:00",
            null,
            IsRemote: true,
            Provider: "online",
            PlaybackUrl: failedUrl);
        var exclusions = new OnlinePlaybackExclusions(
            new[] { OnlinePlaybackCandidateKey.Create(track) },
            new[] { failedUrl });

        var resolved = await resolver.ResolveNextAsync(track, exclusions, attempt: 2);

        Assert.NotNull(resolved);
        Assert.NotEqual(failedUrl, resolved.PlaybackUrl);
        Assert.False(exclusions.Contains(resolved));
    }

    [Fact]
    public async Task ResolveNextAsync_PinnedCandidateWithExcludedUrlContinuesWithProviderFallback()
    {
        var handler = new ExcludedCandidateFallbackHandler();
        var service = new OnlineProviderService(new HttpClient(handler));
        var resolver = new OnlinePlaybackResolver(service);
        const string descriptor = "online://netease/pinned";
        var track = new TrackModel(
            descriptor,
            descriptor,
            "Song + Guest",
            "Artist",
            "Album",
            "02:00",
            null,
            IsRemote: true,
            Provider: "netease");
        var exclusions = new OnlinePlaybackExclusions(
            playbackUrls: new[] { "https://audio.test/pinned.mp3" });

        var resolved = await resolver.ResolveNextAsync(track, exclusions, attempt: 2);

        Assert.NotNull(resolved);
        Assert.Equal("excluded", resolved.ProviderTrackId);
        Assert.Equal("https://audio.test/excluded.mp3", resolved.PlaybackUrl);
        Assert.False(exclusions.Contains(resolved));
        Assert.Equal(new[] { "pinned", "excluded" }, handler.ResolvedProviderTrackIds);
    }

    [Fact]
    public async Task SearchAndResolveAsync_RejectsDifferentCandidateResolvingToFailedUrl()
    {
        var handler = new DuplicateUrlAcrossCandidatesHandler();
        var service = new OnlineProviderService(new HttpClient(handler));
        var exclusions = new OnlinePlaybackExclusions(
            new[] { "qq:previous-id" },
            new[] { "https://audio.test/shared.mp3?token=secret" });

        var resolved = await service.SearchAndResolveAsync(
            CreateGenericOnlineTrack(),
            "netease",
            exclusions,
            attempt: 2);

        Assert.NotNull(resolved);
        Assert.Equal("netease:alternative", resolved.CandidateKey);
        Assert.Equal("https://audio.test/alternative.mp3", resolved.PlaybackUrl);
        Assert.Equal(new[] { "duplicate-url", "alternative" }, handler.ResolvedProviderTrackIds);
        Assert.False(exclusions.Contains(resolved));
    }

    [Fact]
    public async Task SearchAndResolveAsync_ReturnsFirstPlayableProviderWithoutWaitingForSlowProviders()
    {
        var handler = new RacingProviderStubHandler();
        var service = new OnlineProviderService(new HttpClient(handler));
        var track = CreateGenericOnlineTrack();
        var stopwatch = Stopwatch.StartNew();

        var resolved = await service.SearchAndResolveAsync(track);

        stopwatch.Stop();
        Assert.NotNull(resolved);
        Assert.Equal("audius", resolved.Provider);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"A playable provider was ready immediately, but resolution took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task SearchAsync_StopsProviderSearchesAfterFourSeconds()
    {
        var service = new OnlineProviderService(new HttpClient(new BlockingProviderStubHandler()));
        var stopwatch = Stopwatch.StartNew();

        var results = await service.SearchAsync("Song Artist");

        stopwatch.Stop();
        Assert.Empty(results);
        Assert.InRange(
            stopwatch.Elapsed,
            TimeSpan.FromSeconds(3.5),
            TimeSpan.FromSeconds(5.5));
    }

    [Fact]
    public async Task SearchAndResolveAsync_StopsCandidateResolutionAfterFourSeconds()
    {
        var handler = new NeteaseCandidateResolutionHandler(blockResolutions: true);
        var service = new OnlineProviderService(new HttpClient(handler));
        var stopwatch = Stopwatch.StartNew();

        var resolved = await service.SearchAndResolveAsync(CreateGenericOnlineTrack());

        stopwatch.Stop();
        Assert.Null(resolved);
        Assert.Equal(1, handler.ResolutionRequestCount);
        Assert.InRange(
            stopwatch.Elapsed,
            TimeSpan.FromSeconds(3.5),
            TimeSpan.FromSeconds(5.5));
    }

    [Fact]
    public async Task SearchAndResolveAsync_CancelsOutstandingProviderRequests()
    {
        var handler = new BlockingProviderStubHandler();
        var service = new OnlineProviderService(new HttpClient(handler));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SearchAndResolveAsync(CreateGenericOnlineTrack(), cancellationToken: cancellation.Token));

        await handler.AllRequestsCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SearchAndResolveAsync_RetriesOnceWithCollaborationSuffixRemoved()
    {
        var handler = new CollaborationFallbackStubHandler();
        var service = new OnlineProviderService(new HttpClient(handler));
        var track = CreateGenericOnlineTrack() with
        {
            Title = "Stateside + Zara Larsson",
            Artist = "PinkPantheress",
            Duration = "02:56",
            DurationSeconds = 176
        };

        var resolved = await service.SearchAndResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal("audius", resolved.Provider);
        Assert.Equal(2, handler.AudiusSearchCount);
    }

    [Fact]
    public async Task SearchAndResolveAsync_ExplicitRetryDoesNotReuseFailedSearchResult()
    {
        var handler = new RecoveringProviderStubHandler();
        var service = new OnlineProviderService(new HttpClient(handler));
        var track = CreateGenericOnlineTrack();

        var first = await service.SearchAndResolveAsync(track);
        var second = await service.SearchAndResolveAsync(track);

        Assert.Null(first);
        Assert.NotNull(second);
        Assert.Equal("audius", second.Provider);
        Assert.Equal(2, handler.AudiusSearchCount);
    }

    [Fact]
    public async Task InvalidatePlaybackUrl_RemovesCachedTemporaryResolution()
    {
        var handler = new RotatingNeteaseResolutionHandler();
        var service = new OnlineProviderService(new HttpClient(handler));

        var first = await service.ResolveAsync("netease", "123");
        var cached = await service.ResolveAsync("netease", "123");

        Assert.NotNull(first);
        Assert.Equal(first.PlaybackUrl, cached?.PlaybackUrl);
        Assert.Equal(1, handler.ResolveCount);

        var invalidate = typeof(OnlineProviderService).GetMethod("InvalidatePlaybackUrl");
        Assert.NotNull(invalidate);
        invalidate.Invoke(service, new object[] { first.PlaybackUrl });

        var refreshed = await service.ResolveAsync("netease", "123");

        Assert.NotNull(refreshed);
        Assert.NotEqual(first.PlaybackUrl, refreshed.PlaybackUrl);
        Assert.Equal(2, handler.ResolveCount);
    }

    private static TrackModel CreateGenericOnlineTrack()
    {
        const string descriptor = "online://online/song";
        return new TrackModel(
            descriptor,
            descriptor,
            "Song",
            "Artist",
            "Album",
            "02:00",
            null,
            true,
            "Online",
            descriptor,
            DurationSeconds: 120);
    }

    private sealed class ProviderStubHandler(
        bool qqPlayable = true,
        bool onlyAudiusSearch = false) : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<RequestedEndpoint> _requests = new();

        public IReadOnlyList<RequestedEndpoint> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToList();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            lock (_gate)
            {
                _requests.Add(new RequestedEndpoint(uri.Host, uri.AbsolutePath));
            }

            var response = Route(request, uri);
            return Task.FromResult(response);
        }

        private HttpResponseMessage Route(HttpRequestMessage request, Uri uri)
        {
            if (uri.Host == "api.audius.co" && uri.AbsolutePath.EndsWith("/tracks/search", StringComparison.Ordinal))
            {
                return Json("""
                    {"data":[{"id":"aud-1","title":"Song","duration":120,"is_streamable":true,"is_available":true,"user":{"name":"Artist"},"artwork":{"480x480":"https://cover.test/audius.jpg"}}]}
                    """);
            }

            if (onlyAudiusSearch && IsSearchRequest(uri))
            {
                return Json(EmptySearchPayload(uri));
            }

            if (uri.Host == "music.163.com" && uri.AbsolutePath == "/api/cloudsearch/pc")
            {
                return Json("""
                    {"result":{"songs":[{"id":123,"name":"Song","duration":120000,"artists":[{"name":"Artist"}],"album":{"name":"Album","picUrl":"https://cover.test/netease.jpg"}}]}}
                    """);
            }

            if (uri.Host == "music.163.com" && uri.AbsolutePath == "/api/song/enhance/player/url")
            {
                Assert.Equal(new Uri("https://music.163.com/"), request.Headers.Referrer);
                return Json("""{"data":[{"url":"https://audio.test/netease.mp3","code":200,"br":320000}]}""");
            }

            if (uri.Host == "www.kuwo.cn" && uri.AbsolutePath == "/")
            {
                var response = Json("{}");
                response.Headers.TryAddWithoutValidation("Set-Cookie", "kw_token=test-token; Path=/");
                return response;
            }

            if (uri.Host == "www.kuwo.cn" && uri.AbsolutePath.Contains("searchMusicBykeyWord", StringComparison.Ordinal))
            {
                Assert.True(request.Headers.Contains("csrf"));
                return Json("""
                    {"data":{"list":[{"rid":456,"name":"Song","artist":"Artist","album":"Album","duration":120,"pic":"https://cover.test/kuwo.jpg"}]}}
                    """);
            }

            if (uri.Host == "www.kuwo.cn" && uri.AbsolutePath.Contains("music/playUrl", StringComparison.Ordinal))
            {
                Assert.True(request.Headers.Contains("csrf"));
                return Json("""{"data":{"url":"https://audio.test/kuwo.mp3"}}""");
            }

            if (uri.Host == "app.u.nf.migu.cn" && uri.AbsolutePath.Contains("song/item/search", StringComparison.Ordinal))
            {
                return Json("""
                    [{"copyrightId":"mg-1","contentId":"content-1","songName":"Song","singerList":[{"name":"Artist"}],"album":"Album","duration":120,"img1":"https://cover.test/migu.jpg"}]
                    """);
            }

            if (uri.Host == "m.music.migu.cn" && uri.AbsolutePath.Contains("cms_audio_play", StringComparison.Ordinal))
            {
                return Json("""{"data":{"playUrl":"https://audio.test/migu.mp3"}}""");
            }

            if (uri.Host == "c.y.qq.com" && uri.AbsolutePath.Contains("smartbox_new.fcg", StringComparison.Ordinal))
            {
                return Json("""
                    {"code":0,"data":{"song":{"itemlist":[{"id":"1","mid":"qq-1","name":"Song","singer":"Artist"}]}}}
                    """);
            }

            if (uri.Host == "u.y.qq.com" && uri.AbsolutePath.Contains("musicu.fcg", StringComparison.Ordinal))
            {
                var purl = qqPlayable ? "C400qq-1.m4a" : string.Empty;
                return Json("{\"req_0\":{\"data\":{\"midurlinfo\":[{\"purl\":\"" + purl + "\"}]}}}");
            }

            if (uri.Host == "songsearch.kugou.com" && uri.AbsolutePath.Contains("song_search_v2", StringComparison.Ordinal))
            {
                return Json("""
                    {"status":1,"data":{"lists":[{"FileHash":"kg-1","SongName":"Song","SingerName":"Artist","AlbumName":"Album","Duration":120,"Image":"https://cover.test/kugou/{size}.jpg"}]}}
                    """);
            }

            if (uri.Host == "m.kugou.com" && uri.AbsolutePath.Contains("getSongInfo.php", StringComparison.Ordinal))
            {
                return Json("""{"url":"https://audio.test/kugou.mp3"}""");
            }

            if (uri.Host == "music.taihe.com" && uri.AbsolutePath == "/v1/search")
            {
                Assert.Contains("sign=", uri.Query, StringComparison.Ordinal);
                return GzipJson("""
                    {"data":{"typeTrack":[{"TSID":"th-1","title":"Song","artist":[{"name":"Artist"}],"albumTitle":"Album","duration":120,"pic":"https://cover.test/taihe.jpg"}]}}
                    """);
            }

            if (uri.Host == "music.taihe.com" && uri.AbsolutePath == "/v1/song/tracklink")
            {
                Assert.Contains("sign=", uri.Query, StringComparison.Ordinal);
                return Json("""{"data":{"path":"https://audio.test/taihe.mp3","pic":"https://cover.test/taihe.jpg"}}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static bool IsSearchRequest(Uri uri)
        {
            return uri.AbsolutePath.Contains("search", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.Contains("song/item/search", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.Contains("smartbox", StringComparison.OrdinalIgnoreCase);
        }

        private static string EmptySearchPayload(Uri uri)
        {
            return uri.Host switch
            {
                "music.163.com" => "{\"result\":{\"songs\":[]}}",
                "www.kuwo.cn" => "{\"data\":{\"list\":[]}}",
                "app.u.nf.migu.cn" => "[]",
                "c.y.qq.com" => "{\"data\":{\"song\":{\"itemlist\":[]}}}",
                "songsearch.kugou.com" => "{\"data\":{\"lists\":[]}}",
                "music.taihe.com" => "{\"data\":{\"typeTrack\":[]}}",
                _ => "{}"
            };
        }

        private static HttpResponseMessage Json(string payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage GzipJson(string payload)
        {
            using var buffer = new MemoryStream();
            using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                gzip.Write(bytes, 0, bytes.Length);
            }

            var content = new ByteArrayContent(buffer.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add("gzip");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }

    private sealed record RequestedEndpoint(string Host, string Path);

    private sealed class NeteaseCandidateResolutionHandler(bool blockResolutions) : HttpMessageHandler
    {
        private int _resolutionRequestCount;

        public int ResolutionRequestCount => Volatile.Read(ref _resolutionRequestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host != "music.163.com")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (uri.AbsolutePath == "/api/cloudsearch/pc")
            {
                return Json("""
                    {"result":{"songs":[
                        {"id":123,"name":"Song","duration":120000,"artists":[{"name":"Artist"}],"album":{"name":"Album"}},
                        {"id":124,"name":"Different Song","duration":120000,"artists":[{"name":"Artist"}],"album":{"name":"Album"}},
                        {"id":125,"name":"Song","duration":120000,"artists":[{"name":"Different Artist"}],"album":{"name":"Album"}},
                        {"id":126,"name":"Song (Live)","duration":120000,"artists":[{"name":"Artist"}],"album":{"name":"Album"}}
                    ]}}
                    """);
            }

            if (uri.AbsolutePath == "/api/song/enhance/player/url")
            {
                Interlocked.Increment(ref _resolutionRequestCount);
                if (blockResolutions)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                var id = GetQueryValue(uri, "id") ?? "missing";
                return Json($"{{\"data\":[{{\"url\":\"https://audio.test/netease-{id}.mp3\"}}]}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string? GetQueryValue(Uri uri, string name)
        {
            return uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .FirstOrDefault(parts => parts[0].Equals(name, StringComparison.OrdinalIgnoreCase))?[1];
        }

        private static HttpResponseMessage Json(string payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TaiheArrayRootHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host != "music.taihe.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var payload = uri.AbsolutePath switch
            {
                "/v1/search" => """
                    [{"TSID":"th-array","title":"Song","artist":[{"name":"Artist"}],"albumTitle":"Album","duration":120}]
                    """,
                "/v1/song/tracklink" => """
                    [{"path":"https://audio.test/taihe-array.mp3","pic":"https://cover.test/taihe-array.jpg"}]
                    """,
                _ => null
            };
            return Task.FromResult(payload is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(payload));
        }

        private static HttpResponseMessage Json(string payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class MiguHtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host == "api.audius.co")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"data\":[{\"id\":\"aud-html-fallback\",\"title\":\"Song\",\"duration\":120,\"is_streamable\":true,\"is_available\":true,\"user\":{\"name\":\"Artist\"}}]}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            if (uri.Host is "app.u.nf.migu.cn" or "m.music.migu.cn")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>upstream error</html>", Encoding.UTF8, "text/html")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class DuplicateCandidateAttemptHandler(
        bool duplicateRows,
        bool collaborationTitle,
        bool blockResolutions,
        ManualTimeProvider? timeProvider = null) : HttpMessageHandler
    {
        private int _searchRequestCount;
        private int _resolutionRequestCount;

        public int SearchRequestCount => Volatile.Read(ref _searchRequestCount);

        public int ResolutionRequestCount => Volatile.Read(ref _resolutionRequestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host != "music.163.com")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (uri.AbsolutePath == "/api/cloudsearch/pc")
            {
                if (Interlocked.Increment(ref _searchRequestCount) == 2)
                {
                    timeProvider?.Advance(TimeSpan.FromSeconds(31));
                }

                var title = collaborationTitle ? "Song + Guest" : "Song";
                var row = $"{{\"id\":\"duplicate-id\",\"name\":\"{title}\",\"duration\":120000,\"artists\":[{{\"name\":\"Artist\"}}],\"album\":{{\"name\":\"Album\"}}}}";
                var rows = duplicateRows ? $"{row},{row}" : row;
                return Json($"{{\"result\":{{\"songs\":[{rows}]}}}}");
            }

            if (uri.AbsolutePath == "/api/song/enhance/player/url")
            {
                Interlocked.Increment(ref _resolutionRequestCount);
                if (blockResolutions)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return Json("{\"data\":[{\"url\":null}]}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _utcNow = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        public void Advance(TimeSpan duration)
        {
            lock (_gate)
            {
                _utcNow += duration;
            }
        }
    }

    private sealed class RacingProviderStubHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host == "api.audius.co")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"data":[{"id":"aud-fast","title":"Song","duration":120,"is_streamable":true,"is_available":true,"user":{"name":"Artist"}}]}
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class BlockingProviderStubHandler : HttpMessageHandler
    {
        private const int ExpectedProviderRequests = 7;
        private int _cancelledRequestCount;

        public TaskCompletionSource AllRequestsCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref _cancelledRequestCount) >= ExpectedProviderRequests)
                {
                    AllRequestsCancelled.TrySetResult();
                }

                throw;
            }
        }
    }

    private sealed class CollaborationFallbackStubHandler : HttpMessageHandler
    {
        public int AudiusSearchCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host != "api.audius.co")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            AudiusSearchCount++;
            var query = Uri.UnescapeDataString(uri.Query);
            var payload = query.Contains("Zara Larsson", StringComparison.OrdinalIgnoreCase)
                ? "{\"data\":[]}"
                : "{\"data\":[{\"id\":\"stateside\",\"title\":\"Stateside\",\"duration\":176,\"is_streamable\":true,\"is_available\":true,\"user\":{\"name\":\"PinkPantheress\"}}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RecoveringProviderStubHandler : HttpMessageHandler
    {
        public int AudiusSearchCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host != "api.audius.co")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            AudiusSearchCount++;
            var payload = AudiusSearchCount == 1
                ? "{\"data\":[]}"
                : "{\"data\":[{\"id\":\"aud-recovered\",\"title\":\"Song\",\"duration\":120,\"is_streamable\":true,\"is_available\":true,\"user\":{\"name\":\"Artist\"}}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RotatingNeteaseResolutionHandler : HttpMessageHandler
    {
        public int ResolveCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host == "music.163.com" && uri.AbsolutePath == "/api/song/enhance/player/url")
            {
                ResolveCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"data\":[{{\"url\":\"https://audio.test/netease-{ResolveCount}.mp3\"}}]}}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class ExcludedCandidateFallbackHandler : HttpMessageHandler
    {
        private readonly List<string> _resolvedProviderTrackIds = new();

        public int SearchRequestCount { get; private set; }

        public IReadOnlyList<string> ResolvedProviderTrackIds => _resolvedProviderTrackIds;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host != "music.163.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (uri.AbsolutePath == "/api/cloudsearch/pc")
            {
                SearchRequestCount++;
                var alternative = SearchRequestCount > 1
                    ? ",{\"id\":\"alternative\",\"name\":\"Song + Guest\",\"duration\":120000,\"artists\":[{\"name\":\"Artist\"}],\"album\":{\"name\":\"Album\"}}"
                    : string.Empty;
                return Task.FromResult(Json(
                    "{\"result\":{\"songs\":[" +
                    "{\"id\":\"excluded\",\"name\":\"Song + Guest\",\"duration\":120000,\"artists\":[{\"name\":\"Artist\"}],\"album\":{\"name\":\"Album\"}}" +
                    alternative +
                    "]}}"));
            }

            if (uri.AbsolutePath == "/api/song/enhance/player/url")
            {
                var id = uri.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Split('=', 2))
                    .First(parts => parts[0].Equals("id", StringComparison.OrdinalIgnoreCase))[1];
                _resolvedProviderTrackIds.Add(id);
                return Task.FromResult(Json(
                    $"{{\"data\":[{{\"url\":\"https://audio.test/{id}.mp3\"}}]}}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class DuplicateUrlAcrossCandidatesHandler : HttpMessageHandler
    {
        private readonly List<string> _resolvedProviderTrackIds = new();

        public IReadOnlyList<string> ResolvedProviderTrackIds => _resolvedProviderTrackIds;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
            if (uri.Host != "music.163.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (uri.AbsolutePath == "/api/cloudsearch/pc")
            {
                return Task.FromResult(Json("""
                    {"result":{"songs":[
                        {"id":"duplicate-url","name":"Song","duration":120000,"artists":[{"name":"Artist"}],"album":{"name":"Album"}},
                        {"id":"alternative","name":"Song","duration":120000,"artists":[{"name":"Artist"}],"album":{"name":"Album"}}
                    ]}}
                    """));
            }

            if (uri.AbsolutePath == "/api/song/enhance/player/url")
            {
                var id = uri.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Split('=', 2))
                    .First(parts => parts[0].Equals("id", StringComparison.OrdinalIgnoreCase))[1];
                _resolvedProviderTrackIds.Add(id);
                var playbackUrl = id == "duplicate-url"
                    ? "https://audio.test/shared.mp3?token=secret"
                    : "https://audio.test/alternative.mp3";
                return Task.FromResult(Json(
                    $"{{\"data\":[{{\"url\":\"{playbackUrl}\"}}]}}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
