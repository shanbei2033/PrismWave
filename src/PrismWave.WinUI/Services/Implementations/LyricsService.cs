using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class LyricsService : ILyricsService
{
    private const string LocalSource = "local";
    private const string OnlineSource = "online";
    private const string LrclibProvider = "lrclib";
    private static readonly Regex QqLyricContentPattern = new(
        @"<content[^>]*><!\[CDATA\[(?<content>[\s\S]*?)\]\]></content>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HexLyricsPattern = new(
        @"^[0-9a-fA-F]+$",
        RegexOptions.Compiled);
    internal static TimeSpan DefaultRequestTimeout { get; } = TimeSpan.FromSeconds(12);
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public LyricsService(ISettingsService settingsService)
        : this(
            settingsService,
            new HttpClient(),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrismWave",
                "lyrics_cache"))
    {
    }

    public LyricsService(ISettingsService settingsService, HttpClient httpClient, string cacheDirectory)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory;
    }

    public async Task<IReadOnlyList<LyricLineModel>> LoadLyricsAsync(
        TrackModel track,
        CancellationToken cancellationToken = default)
    {
        var document = await LoadLyricsDocumentAsync(track, cancellationToken: cancellationToken);
        return document.Lines;
    }

    public async Task<LyricsDocumentModel> LoadLyricsDocumentAsync(
        TrackModel track,
        string? sourceOverride = null,
        bool forceOnline = false,
        CancellationToken cancellationToken = default)
    {
        var preferredSource = NormalizeSource(sourceOverride ?? GetPreferredSource(track));
        if (track.IsRemote)
        {
            preferredSource = OnlineSource;
        }

        if (!forceOnline && preferredSource == LocalSource)
        {
            var local = await LoadLocalLyricsAsync(track, cancellationToken);
            if (!local.IsEmpty)
            {
                return local;
            }
        }

        var online = await LoadOnlineLyricsAsync(track, forceOnline, cancellationToken);
        if (!online.IsEmpty)
        {
            return online;
        }

        if (!track.IsRemote && (forceOnline || preferredSource == OnlineSource))
        {
            var localFallback = await LoadLocalLyricsAsync(track, cancellationToken);
            if (!localFallback.IsEmpty)
            {
                return localFallback;
            }
        }

        return LyricsDocumentModel.Empty(preferredSource);
    }

    public async Task<IReadOnlyList<LyricsSearchResultModel>> SearchOnlineLyricsAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = string.IsNullOrWhiteSpace(query)
            ? BuildDefaultQuery(track)
            : query.Trim();
        if (normalizedQuery.Length == 0)
        {
            return Array.Empty<LyricsSearchResultModel>();
        }

        var results = await RequestSearchAsync(normalizedQuery, cancellationToken);
        if (results.Count > 0)
        {
            return ScoreResults(results, track, normalizedQuery)
                .Take(20)
                .ToList();
        }

        return await SearchQqLyricsAsync(track, normalizedQuery, cancellationToken);
    }

    public async Task<LyricsDocumentModel> LoadSearchResultAsync(
        TrackModel track,
        LyricsSearchResultModel result,
        CancellationToken cancellationToken = default)
    {
        var raw = !string.IsNullOrWhiteSpace(result.SyncedLyrics)
            ? result.SyncedLyrics
            : result.PlainLyrics;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return LyricsDocumentModel.Empty(OnlineSource);
        }

        var duration = track.DurationSeconds > 0 ? track.DurationSeconds : result.DurationSeconds;
        var document = LyricsParser.Parse(raw, duration, OnlineSource, result.Provider) with
        {
            SelectionKind = LyricsSelectionKind.Manual
        };
        if (document.IsEmpty)
        {
            return document;
        }

        await SaveCacheAsync(track, document, cancellationToken);
        await SetPreferredSourceAsync(track, OnlineSource);
        StartupLog.Write($"lyrics.online.selected: provider={result.Provider}, track=\"{track.Title}\", lines={document.Lines.Count}");
        return document;
    }

    public async Task<LyricsDocumentModel?> TryLoadWordSyncedLyricsDocumentAsync(
        TrackModel track,
        CancellationToken cancellationToken = default)
    {
        var query = BuildDefaultQuery(track);
        var candidates = await SearchQqSuggestionsAsync(query, cancellationToken);
        var exactCandidates = candidates
            .Where(candidate => IsExactIdentity(track.Title, candidate.Title)
                && IsExactIdentity(track.Artist, candidate.Artist))
            .Take(6)
            .ToList();
        if (exactCandidates.Count == 0)
        {
            return null;
        }

        var resolved = await Task.WhenAll(exactCandidates.Select(candidate =>
            FetchQqQrcLyricsAsync(candidate, cancellationToken)));
        var best = resolved
            .Where(result => result?.LyricsKind == LyricsSyncKind.WordSynced)
            .Cast<LyricsSearchResultModel>()
            .OrderByDescending(result => ScoreResult(result, track, query))
            .FirstOrDefault();
        if (best is null || string.IsNullOrWhiteSpace(best.SyncedLyrics))
        {
            return null;
        }

        var document = LyricsParser.Parse(
            best.SyncedLyrics,
            track.DurationSeconds,
            OnlineSource,
            best.Provider);
        if (document.IsEmpty || !document.HasTimedSegments)
        {
            return null;
        }

        await SaveCacheAsync(track, document, cancellationToken);
        StartupLog.Write(
            $"lyrics.online.word-upgrade: provider={best.Provider}, track=\"{track.Title}\", lines={document.Lines.Count}");
        return document;
    }

    private static bool IsExactIdentity(string expected, string actual)
    {
        var expectedKey = NormalizeSearchText(expected);
        var actualKey = NormalizeSearchText(actual);
        return expectedKey.Length > 0 && expectedKey == actualKey;
    }

    public string GetPreferredSource(TrackModel track)
    {
        if (track.IsRemote)
        {
            return OnlineSource;
        }

        var key = GetTrackKey(track);
        var sources = _settingsService.Current.PreferredLyricsSources;
        return sources is not null && sources.TryGetValue(key, out var source)
            ? NormalizeSource(source)
            : LocalSource;
    }

    public double GetOffsetSeconds(TrackModel track)
    {
        var key = GetTrackKey(track);
        var offsets = _settingsService.Current.LyricsOffsets;
        return offsets is not null && offsets.TryGetValue(key, out var offset) ? offset : 0;
    }

    public Task SetPreferredSourceAsync(TrackModel track, string source)
    {
        var key = GetTrackKey(track);
        var sources = new Dictionary<string, string>(
            _settingsService.Current.PreferredLyricsSources ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase)
        {
            [key] = track.IsRemote ? OnlineSource : NormalizeSource(source)
        };
        return _settingsService.SaveAsync(_settingsService.Current with { PreferredLyricsSources = sources });
    }

    public Task SetOffsetSecondsAsync(TrackModel track, double seconds)
    {
        var key = GetTrackKey(track);
        var rounded = Math.Round(seconds * 10, MidpointRounding.AwayFromZero) / 10d;
        var offsets = new Dictionary<string, double>(
            _settingsService.Current.LyricsOffsets ?? new Dictionary<string, double>(),
            StringComparer.OrdinalIgnoreCase);
        if (Math.Abs(rounded) < 0.001)
        {
            offsets.Remove(key);
        }
        else
        {
            offsets[key] = rounded;
        }

        return _settingsService.SaveAsync(_settingsService.Current with { LyricsOffsets = offsets });
    }

    private async Task<LyricsDocumentModel> LoadLocalLyricsAsync(
        TrackModel track,
        CancellationToken cancellationToken)
    {
        if (track.IsRemote || string.IsNullOrWhiteSpace(track.Path) || !File.Exists(track.Path))
        {
            return LyricsDocumentModel.Empty(LocalSource);
        }

        var sidecar = await ReadSidecarLyricsAsync(track.Path, cancellationToken);
        if (!string.IsNullOrWhiteSpace(sidecar))
        {
            var document = LyricsParser.Parse(sidecar, track.DurationSeconds, LocalSource, "sidecar");
            if (!document.IsEmpty)
            {
                StartupLog.Write($"lyrics.local.sidecar: track=\"{track.Title}\", lines={document.Lines.Count}");
                return document;
            }
        }

        var embedded = ReadEmbeddedLyrics(track.Path);
        if (!string.IsNullOrWhiteSpace(embedded))
        {
            var document = LyricsParser.Parse(embedded, track.DurationSeconds, LocalSource, "embedded");
            if (!document.IsEmpty)
            {
                StartupLog.Write($"lyrics.local.embedded: track=\"{track.Title}\", lines={document.Lines.Count}");
                return document;
            }
        }

        return LyricsDocumentModel.Empty(LocalSource);
    }

    private async Task<LyricsDocumentModel> LoadOnlineLyricsAsync(
        TrackModel track,
        bool forceOnline,
        CancellationToken cancellationToken)
    {
        if (!forceOnline)
        {
            var cached = await LoadCacheAsync(track, cancellationToken);
            if (!cached.IsEmpty)
            {
                StartupLog.Write($"lyrics.online.cache: track=\"{track.Title}\", lines={cached.Lines.Count}");
                return cached;
            }
        }

        var best = await ResolveBestOnlineResultAsync(track, cancellationToken);
        if (best is null)
        {
            StartupLog.Write($"lyrics.online.empty: track=\"{track.Title}\"");
            return LyricsDocumentModel.Empty(OnlineSource);
        }

        var raw = !string.IsNullOrWhiteSpace(best.SyncedLyrics) ? best.SyncedLyrics : best.PlainLyrics;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return LyricsDocumentModel.Empty(OnlineSource);
        }

        var document = LyricsParser.Parse(raw, track.DurationSeconds, OnlineSource, best.Provider);
        if (document.IsEmpty)
        {
            return document;
        }

        await SaveCacheAsync(track, document, cancellationToken);
        StartupLog.Write($"lyrics.online.loaded: provider={best.Provider}, track=\"{track.Title}\", lines={document.Lines.Count}");
        return document;
    }

    private async Task<LyricsSearchResultModel?> ResolveBestOnlineResultAsync(
        TrackModel track,
        CancellationToken cancellationToken)
    {
        var query = BuildDefaultQuery(track);
        using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<LyricsSearchResultModel?>>
        {
            RequestExactAsync(track, raceCancellation.Token),
            SearchBestOnlineResultAsync(track, query, raceCancellation.Token)
        };

        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            LyricsSearchResultModel? result;
            try
            {
                result = await completed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                result = null;
            }

            if (result is null)
            {
                continue;
            }

            raceCancellation.Cancel();
            foreach (var remaining in pending)
            {
                _ = ObserveAsync(remaining);
            }

            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var qqResults = await SearchQqLyricsAsync(track, query, cancellationToken);
        return qqResults.FirstOrDefault();
    }

    private async Task<LyricsSearchResultModel?> SearchBestOnlineResultAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken)
    {
        var results = await RequestSearchAsync(query, cancellationToken);
        return ScoreResults(results, track, query).FirstOrDefault();
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

    private async Task<LyricsSearchResultModel?> RequestExactAsync(
        TrackModel track,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["track_name"] = track.Title,
            ["artist_name"] = track.Artist
        };
        if (!string.IsNullOrWhiteSpace(track.Album))
        {
            parameters["album_name"] = track.Album;
        }

        if (track.DurationSeconds > 0)
        {
            parameters["duration"] = Math.Round(track.DurationSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        using var document = await RequestJsonAsync("/api/get", parameters, cancellationToken);
        return document is not null && document.RootElement.ValueKind == JsonValueKind.Object
            ? ReadSearchResult(document.RootElement)
            : null;
    }

    private async Task<IReadOnlyList<LyricsSearchResultModel>> RequestSearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<LyricsSearchResultModel>();
        }

        using var document = await RequestJsonAsync(
            "/api/search",
            new Dictionary<string, string> { ["q"] = query },
            cancellationToken);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LyricsSearchResultModel>();
        }

        return document.RootElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(ReadSearchResult)
            .Where(item => item is not null)
            .Cast<LyricsSearchResultModel>()
            .ToList();
    }

    private async Task<IReadOnlyList<LyricsSearchResultModel>> SearchQqLyricsAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken)
    {
        var candidates = await SearchQqSuggestionsAsync(query, cancellationToken);
        if (candidates.Count == 0)
        {
            return Array.Empty<LyricsSearchResultModel>();
        }

        var ordered = candidates
            .OrderByDescending(candidate =>
                MatchScore(track.Title, candidate.Title, 50, 24)
                + MatchScore(track.Artist, candidate.Artist, 35, 16))
            .Take(6)
            .ToList();
        var resolved = await Task.WhenAll(ordered.Select(candidate =>
            FetchQqLyricsAsync(candidate, cancellationToken)));
        var usable = resolved.Where(result => result is not null).Cast<LyricsSearchResultModel>();
        return ScoreResults(usable, track, query).Take(20).ToList();
    }

    private async Task<IReadOnlyList<QqSongCandidate>> SearchQqSuggestionsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        using var document = await RequestJsonAsync(
            "c.y.qq.com",
            "/splcloud/fcgi-bin/smartbox_new.fcg",
            new Dictionary<string, string> { ["key"] = query },
            cancellationToken);
        if (document is null
            || !document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("song", out var song)
            || !song.TryGetProperty("itemlist", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<QqSongCandidate>();
        }

        var candidates = new List<QqSongCandidate>();
        foreach (var item in items.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idValue)
                ? idValue.ValueKind == JsonValueKind.String ? idValue.GetString() ?? string.Empty : idValue.ToString()
                : string.Empty;
            var mid = ReadString(item, "mid") ?? string.Empty;
            var title = ReadString(item, "name") ?? string.Empty;
            var artist = ReadString(item, "singer") ?? "Unknown artist";
            if (id.Length == 0 || mid.Length == 0 || title.Length == 0)
            {
                continue;
            }

            candidates.Add(new QqSongCandidate(id, mid, title, artist));
        }

        return candidates;
    }

    private async Task<LyricsSearchResultModel?> FetchQqLyricsAsync(
        QqSongCandidate candidate,
        CancellationToken cancellationToken)
    {
        using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<LyricsSearchResultModel?>>
        {
            FetchQqQrcLyricsAsync(candidate, raceCancellation.Token),
            FetchQqLineLyricsAsync(candidate, raceCancellation.Token)
        };

        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            LyricsSearchResultModel? result;
            try
            {
                result = await completed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                result = null;
            }

            if (result is null)
            {
                continue;
            }

            raceCancellation.Cancel();
            foreach (var remaining in pending)
            {
                _ = ObserveAsync(remaining);
            }

            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private async Task<LyricsSearchResultModel?> FetchQqQrcLyricsAsync(
        QqSongCandidate candidate,
        CancellationToken cancellationToken)
    {
        var response = await RequestTextAsync(
            "c.y.qq.com",
            "/qqmusic/fcgi-bin/lyric_download.fcg",
            new Dictionary<string, string>
            {
                ["version"] = "15",
                ["miniversion"] = "82",
                ["lrctype"] = "4",
                ["musicid"] = candidate.Id
            },
            cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var match = QqLyricContentPattern.Match(response);
        if (!match.Success)
        {
            return null;
        }

        var content = WebUtility.HtmlDecode(match.Groups["content"].Value).Trim();
        var resolved = content.Length % 16 == 0 && HexLyricsPattern.IsMatch(content)
            ? QqQrcDecoder.Decrypt(content)
            : content;
        if (string.IsNullOrWhiteSpace(resolved)
            || LyricsParser.Parse(resolved, provider: "qqmusic").IsEmpty)
        {
            return null;
        }

        return new LyricsSearchResultModel(
            candidate.Id,
            candidate.Title,
            candidate.Artist,
            string.Empty,
            0,
            resolved,
            null,
            "qqmusic");
    }

    private async Task<LyricsSearchResultModel?> FetchQqLineLyricsAsync(
        QqSongCandidate candidate,
        CancellationToken cancellationToken)
    {
        using var document = await RequestJsonAsync(
            "c.y.qq.com",
            "/lyric/fcgi-bin/fcg_query_lyric_new.fcg",
            new Dictionary<string, string>
            {
                ["songmid"] = candidate.Mid,
                ["format"] = "json",
                ["nobase64"] = "1",
                ["g_tk"] = "5381",
                ["loginUin"] = "0",
                ["hostUin"] = "0",
                ["inCharset"] = "utf8",
                ["outCharset"] = "utf-8",
                ["notice"] = "0",
                ["platform"] = "yqq.json",
                ["needNewCode"] = "0"
            },
            cancellationToken);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var lyric = ReadString(document.RootElement, "lyric");
        if (string.IsNullOrWhiteSpace(lyric)
            || LyricsParser.Parse(lyric, provider: "qqmusic").IsEmpty)
        {
            return null;
        }

        return new LyricsSearchResultModel(
            candidate.Id,
            candidate.Title,
            candidate.Artist,
            string.Empty,
            0,
            lyric,
            null,
            "qqmusic");
    }

    private async Task<JsonDocument?> RequestJsonAsync(
        string path,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        return await RequestJsonAsync("lrclib.net", path, parameters, cancellationToken);
    }

    private async Task<JsonDocument?> RequestJsonAsync(
        string host,
        string path,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var query = string.Join(
            "&",
            parameters
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => $"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value)}"));
        var uri = new Uri($"https://{host}{path}?{query}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)");
        if (host.EndsWith("y.qq.com", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Referrer = new Uri("https://y.qq.com/");
            request.Headers.TryAddWithoutValidation("Origin", "https://y.qq.com");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultRequestTimeout);

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StartupLog.Write($"lyrics.online.timeout: host={host}, path={path}");
            return null;
        }
        catch (HttpRequestException exception)
        {
            StartupLog.Write($"lyrics.online.network: {exception.Message}");
            return null;
        }
        catch (JsonException exception)
        {
            StartupLog.Write($"lyrics.online.json: {exception.Message}");
            return null;
        }
    }

    private async Task<string?> RequestTextAsync(
        string host,
        string path,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var query = string.Join(
            "&",
            parameters
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => $"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value)}"));
        var uri = new Uri($"https://{host}{path}?{query}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.UserAgent.ParseAdd("PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)");
        if (host.EndsWith("y.qq.com", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Referrer = new Uri("https://y.qq.com/");
            request.Headers.TryAddWithoutValidation("Origin", "https://y.qq.com");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultRequestTimeout);
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StartupLog.Write($"lyrics.online.timeout: host={host}, path={path}");
            return null;
        }
        catch (HttpRequestException exception)
        {
            StartupLog.Write($"lyrics.online.network: {exception.Message}");
            return null;
        }
    }

    private static LyricsSearchResultModel? ReadSearchResult(JsonElement item)
    {
        if (ReadBool(item, "instrumental"))
        {
            return null;
        }

        var synced = ReadString(item, "syncedLyrics");
        var plain = ReadString(item, "plainLyrics");
        if (string.IsNullOrWhiteSpace(synced) && string.IsNullOrWhiteSpace(plain))
        {
            return null;
        }

        return new LyricsSearchResultModel(
            ReadId(item),
            ReadString(item, "trackName") ?? ReadString(item, "name") ?? "Unknown track",
            ReadString(item, "artistName") ?? "Unknown artist",
            ReadString(item, "albumName") ?? string.Empty,
            ReadDouble(item, "duration"),
            synced,
            plain,
            LrclibProvider);
    }

    private static IEnumerable<LyricsSearchResultModel> ScoreResults(
        IEnumerable<LyricsSearchResultModel> results,
        TrackModel track,
        string query)
    {
        return results
            .Where(result => !string.IsNullOrWhiteSpace(result.SyncedLyrics) || !string.IsNullOrWhiteSpace(result.PlainLyrics))
            .DistinctBy(result => $"{result.Provider}|{result.Id}", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(result => ScoreResult(result, track, query));
    }

    private static int ScoreResult(LyricsSearchResultModel result, TrackModel track, string query)
    {
        var score = string.IsNullOrWhiteSpace(result.SyncedLyrics) ? 0 : 10;
        score += MatchScore(track.Title, result.TrackName, 50, 24);
        score += MatchScore(track.Artist, result.ArtistName, 35, 16);
        score += MatchScore(track.Album, result.AlbumName, 12, 0);
        score += MatchScore(query, result.TrackName, 12, 6);
        if (track.DurationSeconds > 0 && result.DurationSeconds > 0)
        {
            var delta = Math.Abs(track.DurationSeconds - result.DurationSeconds);
            score += delta <= 2 ? 16 : delta <= 5 ? 10 : delta <= 10 ? 5 : 0;
        }

        return score;
    }

    private static int MatchScore(string expected, string actual, int exact, int partial)
    {
        var expectedKey = NormalizeSearchText(expected);
        var actualKey = NormalizeSearchText(actual);
        if (expectedKey.Length == 0 || actualKey.Length == 0)
        {
            return 0;
        }

        if (expectedKey == actualKey)
        {
            return exact;
        }

        return expectedKey.Contains(actualKey, StringComparison.Ordinal)
            || actualKey.Contains(expectedKey, StringComparison.Ordinal)
            ? partial
            : 0;
    }

    private async Task<LyricsDocumentModel> LoadCacheAsync(TrackModel track, CancellationToken cancellationToken)
    {
        var path = GetCachePath(track);
        if (!File.Exists(path))
        {
            return LyricsDocumentModel.Empty(OnlineSource);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<LyricsDocumentModel>(stream, JsonOptions, cancellationToken);
            return document is null || document.IsEmpty ? LyricsDocumentModel.Empty(OnlineSource) : document;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            StartupLog.Write($"lyrics.cache.readFailed: {exception.Message}");
            return LyricsDocumentModel.Empty(OnlineSource);
        }
    }

    private async Task SaveCacheAsync(
        TrackModel track,
        LyricsDocumentModel document,
        CancellationToken cancellationToken)
    {
        if (document.IsEmpty)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = GetCachePath(track);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StartupLog.Write($"lyrics.cache.writeFailed: {exception.Message}");
        }
    }

    private string GetCachePath(TrackModel track)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(GetTrackKey(track).ToUpperInvariant()));
        return Path.Combine(_cacheDirectory, $"{Convert.ToHexString(bytes).ToLowerInvariant()}.json");
    }

    private static async Task<string?> ReadSidecarLyricsAsync(
        string audioPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(audioPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var baseName = Path.GetFileNameWithoutExtension(audioPath);
        var candidateDirectories = new[]
        {
            directory,
            Path.Combine(directory, "lyrics"),
            Path.Combine(Path.GetDirectoryName(directory) ?? directory, "lyrics")
        };
        foreach (var candidateDirectory in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var extension in new[] { ".qrc", ".lrc", ".txt" })
            {
                var candidate = Path.Combine(candidateDirectory, $"{baseName}{extension}");
                if (!File.Exists(candidate))
                {
                    continue;
                }

                return await ReadTextFileAsync(candidate, cancellationToken);
            }
        }

        return null;
    }

    private static async Task<string> ReadTextFileAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }

    private static string? ReadEmbeddedLyrics(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);
            return string.IsNullOrWhiteSpace(file.Tag.Lyrics) ? null : file.Tag.Lyrics;
        }
        catch
        {
            return null;
        }
    }

    private static string GetTrackKey(TrackModel track)
    {
        return !string.IsNullOrWhiteSpace(track.Path) ? track.Path : track.Id;
    }

    private static string NormalizeSource(string? source)
    {
        return string.Equals(source, OnlineSource, StringComparison.OrdinalIgnoreCase)
            ? OnlineSource
            : LocalSource;
    }

    private static string BuildDefaultQuery(TrackModel track)
    {
        return string.Join(" ", new[] { track.Title, track.Artist }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string NormalizeSearchText(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string ReadId(JsonElement item)
    {
        if (!item.TryGetProperty("id", out var value))
        {
            return Guid.NewGuid().ToString("N");
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static string? ReadString(JsonElement item, string property)
    {
        return item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double ReadDouble(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value))
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

    private static bool ReadBool(JsonElement item, string property)
    {
        return item.TryGetProperty(property, out var value)
            && value.ValueKind is JsonValueKind.True;
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record QqSongCandidate(string Id, string Mid, string Title, string Artist);
}
