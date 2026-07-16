using System.Net;
using System.Text;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using PrismWave_WinUI.ViewModels.Hits;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class HitsServiceTests : IDisposable
{
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), $"PrismWaveHitsTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RefreshAsync_ParsesScheduleAndResolvesCurrentTrack()
    {
        var handler = new HitsHttpMessageHandler(CreateManifest(), CreateSchedule());
        var service = new HitsService(
            new HttpClient(handler),
            new Uri("https://hits.test/latest.json"),
            _cacheDirectory);
        var now = DateTimeOffset.Parse("2026-07-10T10:01:00Z");

        await service.RefreshAsync(now);

        Assert.Equal(HitsStatusKind.Ready, service.Current.Status);
        Assert.Equal("Current song", service.Current.CurrentTrack!.Track.Title);
        Assert.Equal("Next song", service.Current.NextTrack!.Track.Title);
        Assert.Equal(60, service.Current.PlaybackOffsetSeconds);
        Assert.Equal("https://audio.test/current.flac", service.Current.CurrentTrack.Track.PlaybackUrl);
        Assert.False(service.Current.UsingCache);
    }

    [Theory]
    [InlineData("2026-07-10T10:05:30Z", HitsStatusKind.OffAir)]
    [InlineData("2026-07-10T12:00:00Z", HitsStatusKind.Standby)]
    public async Task UpdatePosition_MapsOffAirAndStandby(string timestamp, HitsStatusKind expected)
    {
        var service = new HitsService(
            new HttpClient(new HitsHttpMessageHandler(CreateManifest(), CreateSchedule())),
            new Uri("https://hits.test/latest.json"),
            _cacheDirectory);
        await service.RefreshAsync(DateTimeOffset.Parse("2026-07-10T10:01:00Z"));

        service.UpdatePosition(DateTimeOffset.Parse(timestamp));

        Assert.Equal(expected, service.Current.Status);
        Assert.Null(service.Current.CurrentTrack);
    }

    [Fact]
    public async Task RefreshAsync_UsesCachedBundleWhenRemoteFails()
    {
        var now = DateTimeOffset.Parse("2026-07-10T10:01:00Z");
        var warm = new HitsService(
            new HttpClient(new HitsHttpMessageHandler(CreateManifest(), CreateSchedule())),
            new Uri("https://hits.test/latest.json"),
            _cacheDirectory);
        await warm.RefreshAsync(now);
        var offline = new HitsService(
            new HttpClient(new FailingHttpMessageHandler()),
            new Uri("https://hits.test/latest.json"),
            _cacheDirectory);

        await offline.RefreshAsync(now);

        Assert.True(offline.Current.UsingCache);
        Assert.Equal(HitsStatusKind.Ready, offline.Current.Status);
        Assert.Equal("Current song", offline.Current.CurrentTrack!.Track.Title);
    }

    [Fact]
    public async Task RefreshAsync_UsesJsDelivrMirrorWhenRawGithubFails()
    {
        var manifest = CreateManifest().Replace(
            "https://hits.test/2026-07-10.json",
            "https://raw.githubusercontent.com/owner/repo/main/schedules/2026-07-10.json",
            StringComparison.Ordinal);
        var handler = new GithubMirrorHttpMessageHandler(manifest, CreateSchedule());
        var service = new HitsService(
            new HttpClient(handler),
            new Uri("https://raw.githubusercontent.com/owner/repo/main/latest.json"),
            _cacheDirectory);

        await service.RefreshAsync(DateTimeOffset.Parse("2026-07-10T10:01:00Z"));

        Assert.Equal(HitsStatusKind.Ready, service.Current.Status);
        Assert.Contains(handler.Hosts, host => host == "cdn.jsdelivr.net");
    }

    [Fact]
    public async Task PrepareSession_UsesWasapiSharedAndStopsWhenOffAir()
    {
        var item = new HitsScheduleItemModel(
            1,
            "current",
            "Morning",
            DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-10T10:04:00Z"),
            new TrackModel(
                "current",
                "hits://direct/current",
                "Current song",
                "Artist",
                "Album",
                "04:00",
                null,
                true,
                "direct",
                "https://audio.test/current.flac",
                DurationSeconds: 240));
        var hits = new FakeHitsService(new HitsStateSnapshot(
            HitsStatusKind.Ready,
            "On air",
            "2026-07-10",
            DateTimeOffset.Parse("2026-07-10T10:01:00Z"),
            new[] { item },
            item,
            null,
            60,
            false,
            false));
        var playback = new FakeHitsPlaybackService();
        var settings = new FakeHitsSettingsService();
        var viewModel = new HitsStatusViewModel(hits, playback, settings);

        await viewModel.PrepareHitsSessionCommand.ExecuteAsync(null);

        Assert.Equal("wasapi_shared", settings.Current.AudioOutputMode);
        Assert.Equal(item.Track, playback.PlayedTrack);
        Assert.Equal(60, playback.SeekSeconds);

        hits.SetState(hits.Current with
        {
            Status = HitsStatusKind.OffAir,
            CurrentTrack = null,
            PlaybackOffsetSeconds = 0
        });

        Assert.True(playback.StopCalled);
    }

    [Fact]
    public async Task PrepareSession_RetriesLiveSeekUntilPlaybackAcceptsIt()
    {
        var item = new HitsScheduleItemModel(
            1,
            "current",
            "Morning",
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow.AddMinutes(2),
            new TrackModel(
                "current",
                "hits://audius/current",
                "Current song",
                "Artist",
                "Album",
                "04:00",
                null,
                true,
                "audius",
                "https://audio.test/current.flac",
                DurationSeconds: 240));
        var hits = new FakeHitsService(new HitsStateSnapshot(
            HitsStatusKind.Ready,
            "On air",
            "2026-07-10",
            DateTimeOffset.UtcNow,
            new[] { item },
            item,
            null,
            120,
            false,
            false));
        var playback = new FakeHitsPlaybackService { IgnoreSeekAttempts = 1 };
        var viewModel = new HitsStatusViewModel(hits, playback, new FakeHitsSettingsService());

        await viewModel.PrepareHitsSessionCommand.ExecuteAsync(null);
        await Task.Delay(350);
        playback.RaiseStateChanged();

        Assert.True(playback.SeekCallCount >= 2);
        Assert.Equal(120, playback.PositionSeconds);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    private static string CreateManifest()
    {
        return """
            {
              "schema_version": 1,
              "station_id": "prismwave-hits",
              "timezone": "UTC",
              "generated_at": "2026-07-10T09:50:00Z",
              "active_edition_date": "2026-07-10",
              "schedule_url": "https://hits.test/2026-07-10.json"
            }
            """;
    }

    private static string CreateSchedule()
    {
        return """
            {
              "schema_version": 1,
              "station_id": "prismwave-hits",
              "edition_date": "2026-07-10",
              "timezone": "UTC",
              "generated_at": "2026-07-10T09:50:00Z",
              "service_windows": [
                {"label":"Morning","start_at":"2026-07-10T10:00:00Z","end_at":"2026-07-10T10:10:00Z"}
              ],
              "off_air_windows": [
                {"label":"Break","start_at":"2026-07-10T10:05:00Z","end_at":"2026-07-10T10:06:00Z"}
              ],
              "tracks": [
                {
                  "slot":1,
                  "station_track_id":"current",
                  "window":"Morning",
                  "start_at":"2026-07-10T10:00:00Z",
                  "end_at":"2026-07-10T10:04:00Z",
                  "duration_ms":240000,
                  "title":"Current song",
                  "artist":"Current artist",
                  "album":"Current album",
                  "audio_url":"https://audio.test/current.flac",
                  "audio_provider":"direct",
                  "provider_track_id":"current",
                  "cover_url":"https://image.test/current.jpg"
                },
                {
                  "slot":2,
                  "station_track_id":"next",
                  "window":"Morning",
                  "start_at":"2026-07-10T10:06:00Z",
                  "end_at":"2026-07-10T10:09:00Z",
                  "duration_ms":180000,
                  "title":"Next song",
                  "artist":"Next artist",
                  "album":"Next album",
                  "audio_url":"https://audio.test/next.flac",
                  "audio_provider":"direct",
                  "provider_track_id":"next"
                }
              ]
            }
            """;
    }

    private sealed class HitsHttpMessageHandler(string manifest, string schedule) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal)
                ? manifest
                : schedule;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("offline");
        }
    }

    private sealed class GithubMirrorHttpMessageHandler(string manifest, string schedule) : HttpMessageHandler
    {
        public List<string> Hosts { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Hosts.Add(uri.Host);
            if (uri.Host == "raw.githubusercontent.com")
            {
                throw new HttpRequestException("raw unavailable");
            }

            var body = uri.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal) ? manifest : schedule;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeHitsService(HitsStateSnapshot current) : IHitsService
    {
        public HitsStateSnapshot Current { get; private set; } = current;
        public event EventHandler? StateChanged;
        public Task RefreshAsync(DateTimeOffset? nowUtc = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void UpdatePosition(DateTimeOffset nowUtc) { }

        public void SetState(HitsStateSnapshot snapshot)
        {
            Current = snapshot;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeHitsPlaybackService : IPlaybackService
    {
        private double _positionSeconds;
        public TrackModel? PlayedTrack { get; private set; }
        public double SeekSeconds { get; private set; }
        public int SeekCallCount { get; private set; }
        public int IgnoreSeekAttempts { get; set; }
        public bool StopCalled { get; private set; }
        public TrackModel? CurrentTrack => PlayedTrack;
        public IReadOnlyList<TrackModel> Queue => PlayedTrack is null ? Array.Empty<TrackModel>() : new[] { PlayedTrack };
        public PlaybackMode Mode => PlaybackMode.Loop;
        public PlaybackStatus Status => PlaybackStatus.Paused;
        public double Volume => 0.8;
        public double PositionSeconds => _positionSeconds;
        public double DurationSeconds => PlayedTrack?.DurationSeconds ?? 0;
        public bool IsLoading => false;
        public bool IsPlaying => PlayedTrack is not null && !StopCalled;
        public string? Error => null;
        public IReadOnlyList<WindowsDsdDeviceModel> WindowsDsdDevices => Array.Empty<WindowsDsdDeviceModel>();
        public bool WindowsDsdAvailable => false;
        public string? WindowsDsdOutputModeLabel => null;
        public string? WindowsDsdActiveDeviceName => null;
        public string? WindowsDsdFallbackReason => null;
        public event EventHandler? StateChanged;
        public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
        public void Play(TrackModel track, IReadOnlyList<TrackModel>? queue = null) { PlayedTrack = track; StopCalled = false; StateChanged?.Invoke(this, EventArgs.Empty); }
        public void Stop() { StopCalled = true; }
        public void TogglePlayPause() { }
        public void Next() { }
        public void Previous() { }
        public void CycleMode() { }
        public void SetVolume(double volume) { }
        public void Seek(double seconds)
        {
            SeekSeconds = seconds;
            SeekCallCount++;
            if (SeekCallCount > IgnoreSeekAttempts)
            {
                _positionSeconds = seconds;
            }
        }
        public void PlayFromQueue(TrackModel track) { }
        public void ReorderQueue(IReadOnlyList<TrackModel> tracks) { }
        public void RemoveFromQueue(TrackModel track) { }
        public void ClearQueue() { }
        public Task RefreshWindowsDsdDevicesAsync() => Task.CompletedTask;
    }

    private sealed class FakeHitsSettingsService : ISettingsService
    {
        public SettingsSnapshot Current { get; private set; } = new(
            "zh-CN",
            true,
            true,
            false,
            "wasapi_exclusive",
            "auto",
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
        public event EventHandler? SettingsChanged;

        public Task SaveAsync(SettingsSnapshot snapshot)
        {
            Current = snapshot;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}
