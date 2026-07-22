using System.Net;
using System.Text;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class CoverServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PrismWaveCoverTests-{Guid.NewGuid():N}");

    public CoverServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task SearchOnlineCoversAsync_RanksExactAppleResultFirstAndDeduplicates()
    {
        const string payload = """
            {
              "results": [
                {
                  "trackId": 1,
                  "trackName": "Song",
                  "artistName": "Artist",
                  "collectionName": "Album",
                  "artworkUrl100": "https://is1-ssl.mzstatic.com/image/thumb/exact/100x100bb.jpg"
                },
                {
                  "trackId": 1,
                  "trackName": "Song",
                  "artistName": "Artist",
                  "collectionName": "Album",
                  "artworkUrl100": "https://is1-ssl.mzstatic.com/image/thumb/exact/100x100bb.jpg"
                },
                {
                  "trackId": 2,
                  "trackName": "Song (Live)",
                  "artistName": "Another Artist",
                  "collectionName": "Other Album",
                  "artworkUrl100": "https://is1-ssl.mzstatic.com/image/thumb/live/100x100bb.jpg"
                }
              ]
            }
            """;
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("itunes.apple.com", request.RequestUri?.Host);
            return JsonResponse(payload);
        });
        var service = CreateService(handler);

        var results = await service.SearchOnlineCoversAsync(CreateTrack(), "Song");

        Assert.Equal(2, results.Count);
        Assert.Equal("apple:1", results[0].Id);
        Assert.True(results[0].Score > results[1].Score);
        Assert.Contains("1200x1200bb", results[0].FullImageUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchOnlineCoversAsync_ReusesFreshDiskCacheWithoutNetwork()
    {
        const string payload = """
            {
              "results": [{
                "trackId": 7,
                "trackName": "Song",
                "artistName": "Artist",
                "collectionName": "Album",
                "artworkUrl100": "https://example.com/100x100bb.jpg"
              }]
            }
            """;
        var firstHandler = new StubHttpMessageHandler(_ => JsonResponse(payload));
        var settings = new FakeSettingsService(CreateSettings());
        var cacheRoot = Path.Combine(_tempDirectory, "cache");
        var first = new CoverService(settings, new HttpClient(firstHandler), cacheRoot);
        var track = CreateTrack();

        var online = await first.SearchOnlineCoversAsync(track, "Song");

        var offlineHandler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Network should not be used."));
        var second = new CoverService(settings, new HttpClient(offlineHandler), cacheRoot);
        var cached = await second.SearchOnlineCoversAsync(track, "Song");

        Assert.NotEmpty(online);
        Assert.Equal(online, cached);
        Assert.Equal(0, offlineHandler.RequestCount);
    }

    [Fact]
    public async Task ApplyOnlineCoverAsync_CachesImagePersistsMappingAndRaisesChange()
    {
        var image = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(image)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
            }
        });
        var settings = new FakeSettingsService(CreateSettings());
        var service = new CoverService(settings, new HttpClient(handler), Path.Combine(_tempDirectory, "cache"));
        var track = CreateTrack();
        CoverChangedEventArgs? changed = null;
        service.CoverChanged += (_, args) => changed = args;

        var path = await service.ApplyOnlineCoverAsync(
            track,
            new CoverSearchResultModel(
                "apple:1",
                "Song",
                "Artist",
                "Album",
                "https://example.com/thumb.png",
                "https://example.com/full.png",
                "apple",
                100));

        Assert.True(File.Exists(path));
        Assert.Equal(image, await File.ReadAllBytesAsync(path));
        Assert.Contains(path, settings.Current.CustomCoverPaths!.Values);
        Assert.DoesNotContain(track.Path, settings.Current.CustomCoverPaths.Keys);
        Assert.Equal(path, service.ResolveCoverPath(track));
        Assert.Equal(path, service.ResolveCoverPath(track with
        {
            Id = "same-song-copy",
            Path = @"D:\Other\Song.mp3"
        }));
        Assert.Null(service.ResolveCoverPath(track with
        {
            Id = "different-artist",
            Path = @"D:\Other\Song.flac",
            Artist = "Another Artist"
        }));
        Assert.Equal(track.Path, changed?.TrackPath);
        var titleProperty = typeof(CoverChangedEventArgs).GetProperty("Title");
        var artistProperty = typeof(CoverChangedEventArgs).GetProperty("Artist");
        Assert.NotNull(titleProperty);
        Assert.NotNull(artistProperty);
        Assert.Equal(track.Title, titleProperty.GetValue(changed));
        Assert.Equal(track.Artist, artistProperty.GetValue(changed));
        Assert.Equal(path, changed?.CoverPath);
        Assert.Equal(1, settings.SaveCount);
    }

    [Fact]
    public void ResolveCoverPath_FallsBackToLegacyPathMapping()
    {
        var legacyCover = Path.Combine(_tempDirectory, "legacy.png");
        File.WriteAllBytes(legacyCover, new byte[] { 1 });
        var track = CreateTrack();
        var snapshot = CreateSettings() with
        {
            CustomCoverPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [track.Path] = legacyCover
            }
        };
        var service = new CoverService(
            new FakeSettingsService(snapshot),
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException())),
            Path.Combine(_tempDirectory, "legacy-cache"));

        Assert.Equal(legacyCover, service.ResolveCoverPath(track));
    }

    [Fact]
    public async Task ApplyOnlineCoverAsync_SecondReplacementWithSameExtensionProducesDifferentPath()
    {
        var firstImage = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
        var secondImage = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7, 6 };
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(url.Contains("first") ? firstImage : secondImage)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
                }
            };
        });
        var settings = new FakeSettingsService(CreateSettings());
        var service = new CoverService(settings, new HttpClient(handler), Path.Combine(_tempDirectory, "cache"));
        var track = CreateTrack();

        var firstPath = await service.ApplyOnlineCoverAsync(
            track,
            new CoverSearchResultModel(
                "apple:1", "Song", "Artist", "Album",
                "https://example.com/first-thumb.png",
                "https://example.com/first-full.png",
                "apple", 100));

        var secondPath = await service.ApplyOnlineCoverAsync(
            track,
            new CoverSearchResultModel(
                "apple:2", "Song", "Artist", "Album",
                "https://example.com/second-thumb.png",
                "https://example.com/second-full.png",
                "apple", 100));

        Assert.NotEqual(firstPath, secondPath);
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.Equal(firstImage, await File.ReadAllBytesAsync(firstPath));
        Assert.Equal(secondImage, await File.ReadAllBytesAsync(secondPath));
        Assert.Equal(secondPath, service.ResolveCoverPath(track));
        Assert.Equal(secondPath, settings.Current.CustomCoverPaths!.Values.Single());
    }

    [Fact]
    public async Task ApplyOnlineCoverAsync_RejectsNonImageResponse()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not an image</html>", Encoding.UTF8, "text/html")
        });
        var settings = new FakeSettingsService(CreateSettings());
        var service = new CoverService(settings, new HttpClient(handler), Path.Combine(_tempDirectory, "cache"));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyOnlineCoverAsync(
            CreateTrack(),
            new CoverSearchResultModel(
                "bad",
                "Song",
                "Artist",
                "Album",
                "https://example.com/not-image",
                "https://example.com/not-image",
                "test",
                0)));

        Assert.Empty(settings.Current.CustomCoverPaths!);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_tempDirectory, "cache"), "*", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private CoverService CreateService(HttpMessageHandler handler)
    {
        return new CoverService(
            new FakeSettingsService(CreateSettings()),
            new HttpClient(handler),
            Path.Combine(_tempDirectory, "cache"));
    }

    private static TrackModel CreateTrack()
    {
        return new TrackModel(
            "track-1",
            @"C:\Music\Song.flac",
            "Song",
            "Artist",
            "Album",
            "02:00",
            null,
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
                new Dictionary<string, object?>()),
            CustomCoverPaths: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static HttpResponseMessage JsonResponse(string payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(respond(request));
        }
    }
}
