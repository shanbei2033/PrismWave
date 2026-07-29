using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Infrastructure.Http;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class HitsService : IHitsService
{
    private static readonly Uri DefaultLatestManifestUri = new(
        "https://raw.githubusercontent.com/shanbei2033/prismwave-hits/main/latest.json");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient _httpClient;
    private readonly Uri _latestManifestUri;
    private readonly string _cacheDirectory;
    private HitsScheduleData? _schedule;

    public HitsService()
        : this(
            SharedHttpClient.Resolve(null),
            DefaultLatestManifestUri,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrismWave",
                "hits_manifest_cache"))
    {
    }

    public HitsService(HttpClient httpClient, Uri latestManifestUri, string cacheDirectory)
    {
        _httpClient = httpClient;
        _latestManifestUri = latestManifestUri;
        _cacheDirectory = cacheDirectory;
    }

    public HitsStateSnapshot Current { get; private set; } = HitsStateSnapshot.Idle;
    public event EventHandler? StateChanged;

    public async Task RefreshAsync(
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        Publish(Current with
        {
            Status = HitsStatusKind.Loading,
            Description = "Loading the HITS schedule...",
            CurrentUtcTime = now,
            IsRefreshing = true,
            Error = null
        });

        HitsBundle? bundle = null;
        HitsServiceException? remoteError = null;
        try
        {
            bundle = await LoadRemoteBundleAsync(now, cancellationToken);
            await SaveCacheAsync(bundle, cancellationToken);
        }
        catch (HitsServiceException exception)
        {
            remoteError = exception;
            bundle = await LoadCachedBundleAsync(now, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            remoteError = new HitsServiceException(HitsStatusKind.Unavailable, exception.Message);
            bundle = await LoadCachedBundleAsync(now, cancellationToken);
        }

        if (bundle is null)
        {
            var status = remoteError?.Status ?? HitsStatusKind.Unavailable;
            Publish(new HitsStateSnapshot(
                status,
                DescribeFailure(status),
                string.Empty,
                now,
                Array.Empty<HitsScheduleItemModel>(),
                null,
                null,
                0,
                false,
                false,
                remoteError?.Message));
            StartupLog.Write($"hits.refresh.failed: status={status}, error={remoteError?.Message}");
            return;
        }

        _schedule = bundle.Schedule;
        ApplyPosition(now, bundle.UsingCache);
        StartupLog.Write($"hits.refresh.ready: edition={_schedule.EditionDate}, tracks={_schedule.Tracks.Count}, cache={bundle.UsingCache}");
    }

    public void UpdatePosition(DateTimeOffset nowUtc)
    {
        if (_schedule is null)
        {
            return;
        }

        ApplyPosition(nowUtc.ToUniversalTime(), Current.UsingCache);
    }

    private async Task<HitsBundle> LoadRemoteBundleAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var manifestText = await FetchBestTextAsync(WithCacheBust(_latestManifestUri), cancellationToken);
        var manifest = ParseManifest(manifestText);
        var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var candidates = new List<Uri>();
        if (!string.Equals(today, manifest.ActiveEditionDate, StringComparison.Ordinal))
        {
            candidates.Add(ReplaceFileName(manifest.ScheduleUri, $"{today}.json"));
        }

        candidates.Add(manifest.ScheduleUri);
        HitsServiceException? lastError = null;
        foreach (var candidate in candidates.Distinct())
        {
            try
            {
                var scheduleText = await FetchBestTextAsync(WithCacheBust(candidate), cancellationToken);
                var schedule = ParseSchedule(scheduleText);
                return new HitsBundle(manifestText, scheduleText, manifest, schedule, false);
            }
            catch (HitsServiceException exception)
            {
                lastError = exception;
            }
        }

        throw lastError ?? new HitsServiceException(HitsStatusKind.Unavailable, "No HITS schedule was available.");
    }

    private async Task<HitsBundle?> LoadCachedBundleAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(_cacheDirectory, "latest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var manifestText = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = ParseManifest(manifestText);
            var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var edition in new[] { today, manifest.ActiveEditionDate }.Distinct(StringComparer.Ordinal))
            {
                var schedulePath = Path.Combine(_cacheDirectory, $"{edition}.json");
                if (!File.Exists(schedulePath))
                {
                    continue;
                }

                var scheduleText = await File.ReadAllTextAsync(schedulePath, cancellationToken);
                return new HitsBundle(
                    manifestText,
                    scheduleText,
                    manifest,
                    ParseSchedule(scheduleText),
                    true);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or FormatException)
        {
            StartupLog.Write($"hits.cache.readFailed: {exception.Message}");
        }

        return null;
    }

    private async Task SaveCacheAsync(HitsBundle bundle, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            await WriteAtomicAsync(
                Path.Combine(_cacheDirectory, "latest.json"),
                bundle.ManifestText,
                cancellationToken);
            await WriteAtomicAsync(
                Path.Combine(_cacheDirectory, $"{bundle.Schedule.EditionDate}.json"),
                bundle.ScheduleText,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StartupLog.Write($"hits.cache.writeFailed: {exception.Message}");
        }
    }

    private async Task<string> FetchTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        StartupLog.Write($"hits.http.start: {uri.Host}{uri.AbsolutePath}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("PrismWave/HITS (+https://github.com/shanbei2033/PrismWave)");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new HitsServiceException(HitsStatusKind.Unavailable, $"HITS resource not found: {uri}");
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new HitsServiceException(HitsStatusKind.CloudTimeout, $"HITS returned {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HitsServiceException(HitsStatusKind.Unavailable, $"HITS returned {(int)response.StatusCode}.");
            }

            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            StartupLog.Write($"hits.http.success: host={uri.Host}, elapsed={stopwatch.ElapsedMilliseconds}ms, bytes={Encoding.UTF8.GetByteCount(content)}");
            return content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StartupLog.Write($"hits.http.timeout: host={uri.Host}, elapsed={stopwatch.ElapsedMilliseconds}ms");
            throw new HitsServiceException(HitsStatusKind.CloudTimeout, "The HITS service timed out.");
        }
        catch (HttpRequestException exception)
        {
            StartupLog.Write($"hits.http.network: host={uri.Host}, elapsed={stopwatch.ElapsedMilliseconds}ms, error={exception.Message}");
            throw new HitsServiceException(HitsStatusKind.NoNetwork, exception.Message);
        }
    }

    private async Task<string> FetchBestTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        var mirror = TryCreateJsDelivrMirror(uri);
        if (mirror is null)
        {
            return await FetchTextAsync(uri, cancellationToken);
        }

        using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<string>>
        {
            FetchTextAsync(uri, raceCancellation.Token),
            FetchTextAsync(mirror, raceCancellation.Token)
        };
        HitsServiceException? lastError = null;
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            try
            {
                var content = await completed;
                raceCancellation.Cancel();
                foreach (var remaining in pending)
                {
                    _ = ObserveAsync(remaining);
                }

                return content;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HitsServiceException exception)
            {
                lastError = exception;
            }
        }

        throw lastError ?? new HitsServiceException(HitsStatusKind.Unavailable, "All HITS mirrors failed.");
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static Uri? TryCreateJsDelivrMirror(Uri uri)
    {
        if (!uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
        {
            return null;
        }

        var remainder = string.Join('/', segments.Skip(3));
        var builder = new UriBuilder("https", "cdn.jsdelivr.net")
        {
            Path = $"gh/{segments[0]}/{segments[1]}@{segments[2]}/{remainder}",
            Query = uri.Query.TrimStart('?')
        };
        return builder.Uri;
    }

    private void ApplyPosition(DateTimeOffset now, bool usingCache)
    {
        var schedule = _schedule!;
        var current = schedule.Tracks.FirstOrDefault(track => track.Contains(now));
        var next = schedule.Tracks.FirstOrDefault(track => track.StartAt > now);
        HitsStatusKind status;
        double offset;
        if (current is not null)
        {
            status = HitsStatusKind.Ready;
            offset = Math.Max(0, (now - current.StartAt).TotalSeconds);
        }
        else if (schedule.OffAirWindows.Any(window => window.Contains(now)))
        {
            status = HitsStatusKind.OffAir;
            offset = 0;
        }
        else
        {
            status = HitsStatusKind.Standby;
            offset = 0;
        }

        Publish(new HitsStateSnapshot(
            status,
            DescribePosition(status, current, next, schedule.EditionDate, usingCache),
            schedule.EditionDate,
            now,
            schedule.Tracks,
            current,
            next,
            offset,
            usingCache,
            false));
    }

    private void Publish(HitsStateSnapshot snapshot)
    {
        Current = snapshot;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static HitsManifestData ParseManifest(string raw)
    {
        using var document = JsonDocument.Parse(SanitizeJson(raw));
        var root = document.RootElement;
        var scheduleUrl = ReadString(root, "schedule_url");
        if (string.IsNullOrWhiteSpace(scheduleUrl) || !Uri.TryCreate(scheduleUrl, UriKind.Absolute, out var uri))
        {
            throw new FormatException("HITS latest manifest is missing schedule_url.");
        }

        return new HitsManifestData(
            ReadString(root, "active_edition_date") ?? string.Empty,
            uri);
    }

    private static HitsScheduleData ParseSchedule(string raw)
    {
        using var document = JsonDocument.Parse(SanitizeJson(raw));
        var root = document.RootElement;
        var editionDate = ReadString(root, "edition_date") ?? string.Empty;
        if (editionDate.Length == 0)
        {
            throw new FormatException("HITS schedule is missing edition_date.");
        }

        var serviceWindows = ParseWindows(root, "service_windows");
        var offAirWindows = ParseWindows(root, "off_air_windows");
        var tracks = new List<HitsScheduleItemModel>();
        if (root.TryGetProperty("tracks", out var trackArray) && trackArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in trackArray.EnumerateArray())
            {
                var track = ParseTrack(item);
                if (track is not null)
                {
                    tracks.Add(track);
                }
            }
        }

        tracks.Sort((left, right) => left.StartAt.CompareTo(right.StartAt));
        return new HitsScheduleData(editionDate, serviceWindows, offAirWindows, tracks);
    }

    private static HitsScheduleItemModel? ParseTrack(JsonElement item)
    {
        var stationTrackId = ReadString(item, "station_track_id");
        var title = ReadString(item, "title");
        var artist = ReadString(item, "artist");
        if (string.IsNullOrWhiteSpace(stationTrackId)
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(artist)
            || !TryReadDate(item, "start_at", out var startAt)
            || !TryReadDate(item, "end_at", out var endAt))
        {
            return null;
        }

        var provider = ReadString(item, "audio_provider") ?? "HITS";
        var providerTrackId = ReadString(item, "provider_track_id") ?? string.Empty;
        var playbackUrl = ReadString(item, "audio_url");
        if (string.IsNullOrWhiteSpace(playbackUrl)
            && provider.Equals("audius", StringComparison.OrdinalIgnoreCase)
            && providerTrackId.Length > 0)
        {
            playbackUrl = $"https://api.audius.co/v1/tracks/{Uri.EscapeDataString(providerTrackId)}/stream";
        }

        var sourcePath = providerTrackId.Length > 0
            ? $"hits://{provider.ToLowerInvariant()}/{Uri.EscapeDataString(providerTrackId)}"
            : $"hits://station/{Uri.EscapeDataString(stationTrackId)}";
        var durationMs = ReadInt(item, "duration_ms");
        var model = new TrackModel(
            stationTrackId,
            sourcePath,
            title,
            artist,
            ReadString(item, "album") ?? string.Empty,
            FormatDuration(durationMs),
            ReadString(item, "cover_url"),
            true,
            provider,
            playbackUrl,
            DurationSeconds: durationMs / 1000d);
        return new HitsScheduleItemModel(
            ReadInt(item, "slot"),
            stationTrackId,
            ReadString(item, "window") ?? string.Empty,
            startAt,
            endAt,
            model);
    }

    private static IReadOnlyList<HitsTimeWindowModel> ParseWindows(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<HitsTimeWindowModel>();
        }

        return values.EnumerateArray()
            .Select(item =>
            {
                DateTimeOffset startAt = default;
                DateTimeOffset endAt = default;
                var valid = TryReadDate(item, "start_at", out startAt)
                    && TryReadDate(item, "end_at", out endAt);
                return valid
                    ? new HitsTimeWindowModel(ReadString(item, "label") ?? string.Empty, startAt, endAt)
                    : null;
            })
            .Where(window => window is not null)
            .Cast<HitsTimeWindowModel>()
            .ToList();
    }

    private static string DescribePosition(
        HitsStatusKind status,
        HitsScheduleItemModel? current,
        HitsScheduleItemModel? next,
        string edition,
        bool usingCache)
    {
        var cacheLabel = usingCache ? " Cached schedule." : string.Empty;
        return status switch
        {
            HitsStatusKind.Ready => $"{current!.Track.Title} is on air. Edition {edition}.{cacheLabel}",
            HitsStatusKind.OffAir => $"HITS is currently off air. Edition {edition}.{cacheLabel}",
            _ when next is not null => $"HITS is standing by for {next.Track.Title} at {next.StartAt:HH:mm} UTC.{cacheLabel}",
            _ => $"HITS is standing by. Edition {edition}.{cacheLabel}"
        };
    }

    private static string DescribeFailure(HitsStatusKind status) => status switch
    {
        HitsStatusKind.NoNetwork => "No network connection is available for HITS.",
        HitsStatusKind.CloudTimeout => "The HITS cloud service did not respond in time.",
        _ => "The HITS schedule is unavailable."
    };

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, content, Encoding.UTF8, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static Uri WithCacheBust(Uri uri)
    {
        var separator = string.IsNullOrWhiteSpace(uri.Query) ? "?" : "&";
        return new Uri($"{uri}{separator}_pw={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    private static Uri ReplaceFileName(Uri uri, string fileName)
    {
        var builder = new UriBuilder(uri);
        var directory = builder.Path[..(builder.Path.LastIndexOf('/') + 1)];
        builder.Path = $"{directory}{fileName}";
        return builder.Uri;
    }

    private static string SanitizeJson(string raw) => raw.TrimStart('\uFEFF').Trim();

    private static string? ReadString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static int ReadInt(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static bool TryReadDate(JsonElement element, string property, out DateTimeOffset value)
    {
        value = default;
        var raw = ReadString(element, property);
        return raw is not null
            && DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out value);
    }

    private static string FormatDuration(int durationMs)
    {
        if (durationMs <= 0)
        {
            return "--:--";
        }

        var duration = TimeSpan.FromMilliseconds(durationMs);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private sealed record HitsManifestData(string ActiveEditionDate, Uri ScheduleUri);
    private sealed record HitsScheduleData(
        string EditionDate,
        IReadOnlyList<HitsTimeWindowModel> ServiceWindows,
        IReadOnlyList<HitsTimeWindowModel> OffAirWindows,
        IReadOnlyList<HitsScheduleItemModel> Tracks);
    private sealed record HitsBundle(
        string ManifestText,
        string ScheduleText,
        HitsManifestData Manifest,
        HitsScheduleData Schedule,
        bool UsingCache);

    private sealed class HitsServiceException(HitsStatusKind status, string message) : Exception(message)
    {
        public HitsStatusKind Status { get; } = status;
    }
}
