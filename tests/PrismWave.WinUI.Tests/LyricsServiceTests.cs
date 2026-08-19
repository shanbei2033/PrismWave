using System.IO.Compression;
using System.Net;
using System.Diagnostics;
using System.Text;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class LyricsServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"PrismWaveLyricsTests-{Guid.NewGuid():N}");

    public LyricsServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task LoadLyricsDocumentAsync_PrefersMatchingSidecarForLocalTrack()
    {
        var audioPath = Path.Combine(_tempDirectory, "Song.mp3");
        await File.WriteAllBytesAsync(audioPath, Array.Empty<byte>());
        await File.WriteAllTextAsync(Path.ChangeExtension(audioPath, ".lrc"), "[00:01.25]Local line");
        var settings = new FakeSettingsService(CreateSettings());
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Network should not be used."));
        var service = new LyricsService(settings, new HttpClient(handler), Path.Combine(_tempDirectory, "cache"));

        var document = await service.LoadLyricsDocumentAsync(CreateTrack(audioPath));

        Assert.Equal("local", document.Source);
        Assert.Equal("sidecar", document.Provider);
        Assert.Equal("Local line", Assert.Single(document.Lines).Text);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task LoadLyricsDocumentAsync_ForceOnlineLoadsLrclibAndReusesCache()
    {
        const string response = """
            {
              "id": 42,
              "trackName": "Song",
              "artistName": "Artist",
              "albumName": "Album",
              "duration": 120,
              "instrumental": false,
              "syncedLyrics": "[00:02.00]Online line",
              "plainLyrics": "Online line"
            }
            """;
        var settings = new FakeSettingsService(CreateSettings());
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        });
        var cache = Path.Combine(_tempDirectory, "cache");
        var track = CreateTrack("remote://42", isRemote: true);
        var service = new LyricsService(settings, new HttpClient(handler), cache);

        var online = await service.LoadLyricsDocumentAsync(track, forceOnline: true);
        var cached = await service.LoadLyricsDocumentAsync(
            track,
            sourceOverride: "online",
            forceOnline: false);

        Assert.Equal("online", online.Source);
        Assert.Equal("lrclib", online.Provider);
        Assert.Equal("Online line", Assert.Single(online.Lines).Text);
        Assert.Equal("Online line", Assert.Single(cached.Lines).Text);
        Assert.True(handler.RequestCount >= 2);
        Assert.Single(Directory.EnumerateFiles(cache, "*.json"));
    }

    [Fact]
    public async Task SetOffsetSecondsAsync_RoundsToTenthsAndRemovesZero()
    {
        var settings = new FakeSettingsService(CreateSettings());
        var service = new LyricsService(settings, new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), Path.Combine(_tempDirectory, "cache"));
        var track = CreateTrack("C:\\Music\\Song.flac");

        await service.SetOffsetSecondsAsync(track, 0.26);

        Assert.Equal(0.3, service.GetOffsetSeconds(track));
        Assert.Equal(0.3, settings.Current.LyricsOffsets![track.Path]);

        await service.SetOffsetSecondsAsync(track, 0);

        Assert.Equal(0, service.GetOffsetSeconds(track));
        Assert.DoesNotContain(track.Path, settings.Current.LyricsOffsets!);
        Assert.Equal(2, settings.SaveCount);
    }

    [Fact]
    public void DefaultRequestTimeout_AllowsSlowLyricsProviders()
    {
        Assert.True(LyricsService.DefaultRequestTimeout >= TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task LoadLyricsDocumentAsync_UsesFirstUsableConcurrentLineFallbackAfterWordSearch()
    {
        const string exactResponse = """
            {"id":1,"trackName":"Song","artistName":"Artist","duration":120,"instrumental":false,"syncedLyrics":"[00:01]Exact"}
            """;
        const string searchResponse = """
            [{"id":2,"trackName":"Song","artistName":"Artist","duration":120,"instrumental":false,"syncedLyrics":"[00:01]Search"}]
            """;
        var handler = new RacingHttpMessageHandler(exactResponse, searchResponse);
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "race-cache"));
        var stopwatch = Stopwatch.StartNew();

        var document = await service.LoadLyricsDocumentAsync(
            CreateTrack("remote://race", isRemote: true),
            forceOnline: true);

        stopwatch.Stop();
        Assert.Equal("Search", Assert.Single(document.Lines).Text);
        Assert.True(handler.RequestCount >= 2);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Online fallback took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task LoadLyricsDocumentAsync_PrefersQrcBeforeLineSyncedFallback()
    {
        const string encrypted = "28308A27A460E589D0583A2625C428682F393E78386138F7";
        var handler = new WordFirstHttpMessageHandler(encrypted);
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "word-first-cache"));

        var document = await service.LoadLyricsDocumentAsync(
            CreateTrack("remote://word-first", isRemote: true),
            forceOnline: true);

        Assert.Equal("qqmusic-qrc", document.Provider);
        Assert.True(document.HasTimedSegments);
        Assert.Equal("Hi", Assert.Single(document.Lines).Text);
        Assert.Contains(handler.Paths, path => path.Contains("musicu", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Paths, path => path.StartsWith("lrclib.net/api/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchOnlineLyricsAsync_MergesProvidersAndRanksQrcFirst()
    {
        const string encrypted = "28308A27A460E589D0583A2625C428682F393E78386138F7";
        var handler = new WordFirstHttpMessageHandler(encrypted);
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "word-search-cache"));

        var results = await service.SearchOnlineLyricsAsync(
            CreateTrack("remote://word-search", isRemote: true),
            "Song Artist");

        Assert.True(results.Count >= 2);
        Assert.Equal(LyricsSyncKind.WordSynced, results[0].LyricsKind);
        Assert.Equal("qqmusic-qrc", results[0].Provider);
        Assert.Contains(results, result => result.Provider == "lrclib");
    }

    [Fact]
    public async Task SearchOnlineLyricsAsync_FallsBackToQqWhenLrclibIsEmpty()
    {
        const string suggestions = """
            {"code":0,"data":{"song":{"itemlist":[{"id":"2151366","mid":"000n1aKC2HX4eh","name":"Song","singer":"Artist"}]}}}
            """;
        const string qqLyrics = """
            {"retcode":0,"lyric":"[00:01.00]QQ line"}
            """;
        var handler = new ProviderFallbackHttpMessageHandler(suggestions, qqLyrics);
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "qq-cache"));

        var results = await service.SearchOnlineLyricsAsync(CreateTrack(@"C:\Music\Song.flac"), "Song Artist");

        var result = Assert.Single(results);
        Assert.Equal("qqmusic", result.Provider);
        Assert.Equal("[00:01.00]QQ line", result.SyncedLyrics);
        Assert.Contains(handler.Requests, request => request.Host == "lrclib.net" && request.Path.EndsWith("/search", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Host == "c.y.qq.com" && request.Path.Contains("smartbox", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Host == "c.y.qq.com" && request.Path.Contains("fcg_query_lyric", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchOnlineLyricsAsync_UsesQqQrcWhenAvailable()
    {
        const string encrypted = "28308A27A460E589D0583A2625C428682F393E78386138F7";
        var handler = new QqQrcHttpMessageHandler(encrypted);
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "qrc-cache"));

        var results = await service.SearchOnlineLyricsAsync(CreateTrack(@"C:\Music\Song.flac"), "Song Artist");

        var result = Assert.Single(results);
        Assert.Equal("[0,1000]Hi(0,1000)", result.SyncedLyrics);
        var document = LyricsParser.Parse(result.SyncedLyrics!, provider: result.Provider);
        Assert.True(Assert.Single(document.Lines).HasTimedSegments);
        Assert.Contains(handler.Paths, path => path.Contains("musicu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryLoadWordSyncedLyricsDocumentAsync_ReturnsOnlyExactQrcResult()
    {
        const string encrypted = "28308A27A460E589D0583A2625C428682F393E78386138F7";
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(new QqQrcHttpMessageHandler(encrypted)),
            Path.Combine(_tempDirectory, "word-upgrade-cache"));
        var method = typeof(LyricsService).GetMethod("TryLoadWordSyncedLyricsDocumentAsync");

        Assert.NotNull(method);
        var operation = Assert.IsAssignableFrom<Task<LyricsDocumentModel?>>(
            method.Invoke(service, new object[] { CreateTrack(@"C:\Music\Song.flac"), CancellationToken.None }));
        var document = await operation;

        Assert.NotNull(document);
        Assert.Equal("qqmusic-qrc", document.Provider);
        Assert.True(Assert.Single(document.Lines).HasTimedSegments);
    }

    [Fact]
    public async Task LoadSearchResultAsync_MarksSelectedLyricsAsManual()
    {
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            Path.Combine(_tempDirectory, "manual-cache"));
        var result = new LyricsSearchResultModel(
            "manual",
            "Song",
            "Artist",
            "Album",
            120,
            "[00:01]Selected",
            null,
            "lrclib");

        var document = await service.LoadSearchResultAsync(CreateTrack(@"C:\Music\Song.flac"), result);
        var selectionProperty = typeof(LyricsDocumentModel).GetProperty("SelectionKind");

        Assert.NotNull(selectionProperty);
        Assert.Equal("Manual", selectionProperty.GetValue(document)?.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SearchOnlineLyricsAsync_IncludesKugouKrcWordSyncedResult()
    {
        const string krcPlain = "[offset:0]\n[0,3000]你<0,300,0>好<300,400,0>";
        var handler = new StubHttpMessageHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host == "lyrics.kugou.com" && uri.AbsolutePath == "/search")
            {
                return Json("""{"candidates":[{"id":"9001","accesskey":"ak","duration":120000,"song":"Song","singer":"Artist"}]}""");
            }

            if (uri.Host == "lyrics.kugou.com" && uri.AbsolutePath == "/download")
            {
                return Json($"{{\"status\":200,\"content\":\"{EncodeKrc(krcPlain)}\"}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "cache"));

        var results = await service.SearchOnlineLyricsAsync(CreateTrack("C:\\song.mp3"), "Song Artist");

        var kugou = results.SingleOrDefault(result => result.Provider == "kugou-krc");
        Assert.NotNull(kugou);
        Assert.Equal(LyricsSyncKind.WordSynced, kugou!.LyricsKind);
        Assert.Equal("Song", kugou.TrackName);
        Assert.Equal("Artist", kugou.ArtistName);
    }

    [Fact]
    public async Task SearchOnlineLyricsAsync_ReturnsMiguQrcResultForExactIdentity()
    {
        const string qrc = "[0,2000]你(0,500)好(500,500)";
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.Host == "c.musicapp.migu.cn")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"<QrcInfos><QrcHeadInfo Ti=\"Song\" Ar=\"Artist\" Al=\"Album\" offset=\"0\"/><LyricInfo LyricCount=\"1\"><Lyric_1 LyricType=\"1\"><content><![CDATA[{qrc}]]></content></Lyric_1></LyricInfo></QrcInfos>",
                        Encoding.UTF8,
                        "text/xml")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "cache"));

        var results = await service.SearchOnlineLyricsAsync(CreateTrack("C:\\song.mp3"), "Song Artist");

        var migu = results.SingleOrDefault(result => result.Provider == "migu-qrc");
        Assert.NotNull(migu);
        Assert.Equal(LyricsSyncKind.WordSynced, migu!.LyricsKind);
        Assert.Equal("Song", migu.TrackName);
    }

    [Fact]
    public async Task SearchOnlineLyricsAsync_DropsMiguResultWhenIdentityMismatches()
    {
        const string qrc = "[0,2000]你(0,500)好(500,500)";
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.Host == "c.musicapp.migu.cn")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"<QrcInfos><QrcHeadInfo Ti=\"Another Song\" Ar=\"Someone Else\" offset=\"0\"/><LyricInfo LyricCount=\"1\"><Lyric_1 LyricType=\"1\"><content><![CDATA[{qrc}]]></content></Lyric_1></LyricInfo></QrcInfos>",
                        Encoding.UTF8,
                        "text/xml")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "cache"));

        var results = await service.SearchOnlineLyricsAsync(CreateTrack("C:\\song.mp3"), "Song Artist");

        Assert.DoesNotContain(results, result => result.Provider == "migu-qrc");
    }

    [Fact]
    public async Task LoadLyricsDocumentAsync_FallsBackToKugouKrcWhenQqAndNeteaseFail()
    {
        const string krcPlain = "[offset:0]\n[0,3000]你<0,300,0>好<300,400,0>";
        var handler = new StubHttpMessageHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host == "lyrics.kugou.com" && uri.AbsolutePath == "/search")
            {
                return Json("""{"candidates":[{"id":"9001","accesskey":"ak","duration":120000,"song":"Song","singer":"Artist"}]}""");
            }

            if (uri.Host == "lyrics.kugou.com" && uri.AbsolutePath == "/download")
            {
                return Json($"{{\"status\":200,\"content\":\"{EncodeKrc(krcPlain)}\"}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "cache"));

        var document = await service.LoadLyricsDocumentAsync(CreateTrack("remote://1", isRemote: true), forceOnline: true);

        Assert.Equal("online", document.Source);
        Assert.Equal("kugou-krc", document.Provider);
        Assert.True(document.HasTimedSegments);
        Assert.Equal("你好", document.Lines[0].Text);
    }

    [Fact]
    public async Task LoadLyricsDocumentAsync_PrefersSyllableSidecarAndAttachesCompanions()
    {
        var audioPath = Path.Combine(_tempDirectory, "Track.mp3");
        await File.WriteAllBytesAsync(audioPath, Array.Empty<byte>());
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "Track.syl.lrc"),
            "[00:00.00]<00:00.00>Is <00:00.51>this <00:00.86>the <00:01.27>real <00:02.11>life?");
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "Track.lrc"),
            "[00:00.00]Is this the real life?");
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "Track.trans.lrc"),
            "[00:00.00]这是真实的生活吗");
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            Path.Combine(_tempDirectory, "cache"));

        var document = await service.LoadLyricsDocumentAsync(CreateTrack(audioPath));

        Assert.Equal("local", document.Source);
        Assert.Equal("sidecar", document.Provider);
        var line = Assert.Single(document.Lines);
        Assert.True(line.HasTimedSegments);
        Assert.Equal("这是真实的生活吗", Assert.Single(line.CompanionLines!).Text);
    }

    [Fact]
    public async Task LoadLyricsDocumentAsync_SkipsCompanionWhenTimestampOutOfRange()
    {
        var audioPath = Path.Combine(_tempDirectory, "Track2.mp3");
        await File.WriteAllBytesAsync(audioPath, Array.Empty<byte>());
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "Track2.lrc"), "[00:01.00]Line one");
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "Track2.roma.lrc"), "[00:05.00]roma line");
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            Path.Combine(_tempDirectory, "cache"));

        var document = await service.LoadLyricsDocumentAsync(CreateTrack(audioPath));

        var line = Assert.Single(document.Lines);
        Assert.Equal("Line one", line.Text);
        Assert.Null(line.CompanionLines);
    }

    [Fact]
    public async Task LoadLyricsDocumentAsync_MergeSidecarSplitsBilingualLines()
    {
        var audioPath = Path.Combine(_tempDirectory, "Track3.mp3");
        await File.WriteAllBytesAsync(audioPath, Array.Empty<byte>());
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "Track3.merge.lrc"),
            "[00:02.91]インターネット・エンジェルという現象は\n[00:02.91]intaanetto enjeru to iu genshou wa\n[00:05.40]仮定された有機交流電燈の\n[00:05.40]katei sareta yuuki kouryuu dentou no");
        var service = new LyricsService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            Path.Combine(_tempDirectory, "cache"));

        var document = await service.LoadLyricsDocumentAsync(CreateTrack(audioPath));

        Assert.Equal("sidecar", document.Provider);
        Assert.Equal(2, document.Lines.Count);
        Assert.Equal("インターネット・エンジェルという現象は", document.Lines[0].Text);
        Assert.Equal("intaanetto enjeru to iu genshou wa", Assert.Single(document.Lines[0].CompanionLines!).Text);
    }

    private static string EncodeKrc(string plain)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.UTF8.GetBytes(plain));
        }

        var key = "@Gaw]GtVKn@jRW!An"u8.ToArray();
        var payload = compressed.ToArray();
        var encrypted = new byte[4 + payload.Length];
        encrypted[0] = (byte)'k';
        encrypted[1] = (byte)'r';
        encrypted[2] = (byte)'c';
        encrypted[3] = (byte)'1';
        for (var index = 0; index < payload.Length; index++)
        {
            encrypted[4 + index] = (byte)(payload[index] ^ key[index % key.Length]);
        }

        return Convert.ToBase64String(encrypted);
    }

    private static HttpResponseMessage Json(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static TrackModel CreateTrack(string path, bool isRemote = false)
    {
        return new TrackModel(
            path,
            path,
            "Song",
            "Artist",
            "Album",
            "02:00",
            null,
            isRemote,
            isRemote ? "NetEase" : "Local",
            DurationSeconds: 120);
    }

    private static SettingsSnapshot CreateSettings()
    {
        return new SettingsSnapshot(
            "zh-CN",
            true,
            true,
            "wasapi_shared",
            "auto",
            true,
            220,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new FlutterPreferencesMigrationResult(
                string.Empty,
                false,
                0,
                DateTimeOffset.MinValue,
                new Dictionary<string, object?>()));
    }

    private sealed class FakeSettingsService(SettingsSnapshot current) : ISettingsService
    {
        public SettingsSnapshot Current { get; private set; } = current;
        public int SaveCount { get; private set; }
        public event EventHandler? SettingsChanged;

        public Task SaveAsync(SettingsSnapshot snapshot)
        {
            Current = snapshot;
            SaveCount++;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class RacingHttpMessageHandler(string exactResponse, string searchResponse) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            var isSearch = request.RequestUri?.AbsolutePath.EndsWith("/search", StringComparison.Ordinal) == true;
            await Task.Delay(isSearch ? 25 : 800, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(isSearch ? searchResponse : exactResponse, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ProviderFallbackHttpMessageHandler(string suggestions, string qqLyrics) : HttpMessageHandler
    {
        public List<(string Host, string Path)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add((uri.Host, uri.AbsolutePath));
            var content = uri.Host switch
            {
                "lrclib.net" => "[]",
                "c.y.qq.com" when uri.AbsolutePath.Contains("smartbox", StringComparison.Ordinal) => suggestions,
                "c.y.qq.com" when uri.AbsolutePath.Contains("fcg_query_lyric", StringComparison.Ordinal) => qqLyrics,
                _ => "{}"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class QqQrcHttpMessageHandler(string encrypted) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Paths.Add(uri.AbsolutePath);
            if (uri.Host == "lrclib.net")
            {
                return Json("[]");
            }

            if (uri.AbsolutePath.Contains("smartbox", StringComparison.Ordinal))
            {
                return Json("{\"data\":{\"song\":{\"itemlist\":[{\"id\":\"1\",\"mid\":\"mid\",\"name\":\"Song\",\"singer\":\"Artist\"}]}}}");
            }

            if (uri.AbsolutePath.Contains("musicu", StringComparison.Ordinal))
            {
                return Json($"{{\"req\":{{\"code\":0,\"data\":{{\"qrc\":1,\"crypt\":1,\"lyric\":\"{encrypted}\"}}}}}}");
            }

            if (uri.AbsolutePath.Contains("lyric_download", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"<content><![CDATA[{encrypted}]]></content>", Encoding.UTF8, "text/xml")
                };
            }

            await Task.Delay(150, cancellationToken);
            return Json("{\"lyric\":\"[00:01]Line fallback\"}");
        }

        private static HttpResponseMessage Json(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class WordFirstHttpMessageHandler(string encrypted) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Paths.Add($"{uri.Host}{uri.AbsolutePath}");
            if (uri.Host == "lrclib.net")
            {
                return Json("""
                    [{"id":2,"trackName":"Song","artistName":"Artist","duration":120,"instrumental":false,"syncedLyrics":"[00:01]Line fallback"}]
                    """);
            }

            if (uri.AbsolutePath.Contains("smartbox", StringComparison.Ordinal))
            {
                return Json("{\"data\":{\"song\":{\"itemlist\":[{\"id\":\"1\",\"mid\":\"mid\",\"name\":\"Song\",\"singer\":\"Artist\"}]}}}");
            }

            if (uri.AbsolutePath.Contains("musicu", StringComparison.Ordinal))
            {
                return Json($"{{\"req\":{{\"code\":0,\"data\":{{\"qrc\":1,\"crypt\":1,\"lyric\":\"{encrypted}\"}}}}}}");
            }

            if (uri.AbsolutePath.Contains("client_search", StringComparison.Ordinal))
            {
                return Json("{\"data\":{\"song\":{\"list\":[]}}}");
            }

            if (uri.AbsolutePath.Contains("lyric_download", StringComparison.Ordinal))
            {
                await Task.Delay(80, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"<content><![CDATA[{encrypted}]]></content>", Encoding.UTF8, "text/xml")
                };
            }

            return Json("{\"lyric\":\"[00:01]Line fallback\"}");
        }

        private static HttpResponseMessage Json(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }
}
