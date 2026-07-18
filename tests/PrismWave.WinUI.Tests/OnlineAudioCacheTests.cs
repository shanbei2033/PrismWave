using System.Net;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlineAudioCacheTests
{
    [Fact]
    public async Task ClearAsync_DeletesOnlyPrismWaveOwnedCacheFiles()
    {
        using var directory = new TemporaryDirectory();
        var unrelated = Path.Combine(directory.Path, "my-recording.mp3");
        var owned = Path.Combine(directory.Path, $"{new string('a', 64)}.mp3");
        await File.WriteAllBytesAsync(unrelated, [1, 2, 3]);
        await File.WriteAllBytesAsync(owned, [4, 5, 6]);
        using var cache = CreateCache(directory.Path, 1024);

        await cache.ClearAsync();

        Assert.True(File.Exists(unrelated));
        Assert.False(File.Exists(owned));
        Assert.Equal(0, cache.Status.FileCount);
    }

    [Fact]
    public async Task ConcurrentDownloads_ReserveCapacityBeforeWriting()
    {
        using var directory = new TemporaryDirectory();
        using var client = new HttpClient(new FixedContentHandler(new byte[60]));
        using var cache = CreateCache(directory.Path, 100, client);
        var first = CreateRemoteTrack("first");
        var second = CreateRemoteTrack("second");

        await Task.WhenAll(
            cache.CacheAsync(first, new OnlinePlaybackResolution("https://example.test/first.mp3", "test")),
            cache.CacheAsync(second, new OnlinePlaybackResolution("https://example.test/second.mp3", "test")));

        Assert.Equal(1, cache.Status.FileCount);
        Assert.Equal(60, cache.Status.CurrentBytes);
    }

    [Fact]
    public async Task Refresh_IgnoresFilesNotOwnedByPrismWave()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "unrelated.flac"), new byte[30]);
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, $"{new string('b', 64)}.flac"),
            new byte[20]);
        using var cache = CreateCache(directory.Path, 1024);

        cache.Refresh();

        Assert.Equal(1, cache.Status.FileCount);
        Assert.Equal(20, cache.Status.CurrentBytes);
    }

    private static OnlineAudioCache CreateCache(string directory, long maximum, HttpClient? client = null) =>
        new(new FakeSettingsService(CreateSettings(directory, maximum)), client);

    private static TrackModel CreateRemoteTrack(string id) => new(
        id,
        $"online://test/{id}",
        id,
        "artist",
        "album",
        "01:00",
        null,
        IsRemote: true,
        Provider: "test",
        DurationSeconds: 60);

    private static SettingsSnapshot CreateSettings(string directory, long maximum) => new(
        "zh-CN",
        false,
        true,
        false,
        "wasapi_shared",
        "auto",
        "auto",
        true,
        220,
        [],
        [],
        [],
        [],
        [],
        [],
        new FlutterPreferencesMigrationResult("", false, 0, DateTimeOffset.MinValue, new Dictionary<string, object?>()),
        OnlineCacheMaximumBytes: maximum,
        OnlineCacheDirectory: directory);

    private sealed class FakeSettingsService(SettingsSnapshot current) : ISettingsService
    {
        public SettingsSnapshot Current { get; private set; } = current;
        public event EventHandler? SettingsChanged;

        public Task SaveAsync(SettingsSnapshot snapshot)
        {
            Current = snapshot;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedContentHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PrismWave.CacheTests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
