using System.Text.Json;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class OnlineHomeService : IOnlineHomeService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(7)
    };

    private static readonly Uri[] RemoteHomeUris =
    {
        new("https://raw.githubusercontent.com/shanbei2033/prismwave-hits/main/home/latest_home.json"),
        new("https://cdn.jsdelivr.net/gh/shanbei2033/prismwave-hits@main/home/latest_home.json")
    };

    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public OnlineHomeService()
    {
        if (!TryLoadHomeJson(out var jsonPath, out var document))
        {
            SetUnavailable("No valid schema 8 home data is available.");
        }
        else
        {
            try
            {
                using (document)
                {
                    ApplyDocument(document, jsonPath);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException)
            {
                SetUnavailable($"Home data validation failed: {exception.Message}");
            }
        }

        _ = RefreshAsync();
    }

    public HomeSectionModel TopPlaylist { get; private set; } = EmptyTopPlaylist();
    public IReadOnlyList<HomeSectionModel> Sections { get; private set; } = Array.Empty<HomeSectionModel>();
    public IReadOnlyList<AlbumModel> Albums { get; private set; } = Array.Empty<AlbumModel>();
    public DateTimeOffset GeneratedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool RecommendationsUnavailable { get; private set; }
    public bool RecommendationsPendingGeneration { get; private set; }
    public bool IsRefreshing { get; private set; }
    public string? Error { get; private set; }
    public string? SourcePath { get; private set; }
    public string? SourceDescription { get; private set; }
    public event EventHandler? HomeChanged;

    public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            IsRefreshing = true;
            Error = null;
            Notify();

            var todayCache = CacheFileForDate(BeijingNow().ToString("yyyy-MM-dd"));
            if (!force && TryLoadDocument(todayCache, out var cached))
            {
                using (cached)
                {
                    ApplyDocument(cached, todayCache);
                }
                SourceDescription = "Today's cache";
                StartupLog.Write($"online.home.cache.today: {todayCache}");
                return;
            }

            Exception? lastError = null;
            foreach (var uri in RemoteHomeUris)
            {
                try
                {
                    using var response = await Http.GetAsync(uri, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var remote = JsonDocument.Parse(json);
                    if (!IsValidSchema8(remote.RootElement))
                    {
                        throw new InvalidDataException("Remote home payload is not a usable schema 8 document.");
                    }

                    var edition = ReadString(remote.RootElement, "editionDate") ?? BeijingNow().ToString("yyyy-MM-dd");
                    await WriteCacheAsync(json, edition, cancellationToken);
                    ApplyDocument(remote, uri.ToString());
                    SourceDescription = "Remote daily home";
                    StartupLog.Write($"online.home.remote.success: uri={uri}, edition={edition}");
                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    lastError = exception;
                    StartupLog.Write($"online.home.remote.failed: uri={uri}: {exception.Message}");
                }
            }

            var yesterday = CacheFileForDate(BeijingNow().AddDays(-1).ToString("yyyy-MM-dd"));
            if (TryLoadDocument(yesterday, out var stale))
            {
                using (stale)
                {
                    ApplyDocument(stale, yesterday);
                }
                RecommendationsPendingGeneration = true;
                SourceDescription = "Yesterday's cache";
                Error = lastError?.Message;
                StartupLog.Write($"online.home.cache.yesterday: {yesterday}");
                return;
            }

            if (TryLoadDocument(BundledHomePath(), out var bundled))
            {
                using (bundled)
                {
                    ApplyDocument(bundled, BundledHomePath());
                }
                SourceDescription = "Bundled fallback";
                Error = lastError?.Message;
                return;
            }

            SetUnavailable(lastError?.Message ?? "Online home is unavailable.");
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
            Notify();
        }
    }

    public async Task<IReadOnlyList<HomeTrackModel>> LoadAlbumTracksAsync(
        string albumId,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(albumId, out var id) || id <= 0)
        {
            return Array.Empty<HomeTrackModel>();
        }

        try
        {
            var uri = new Uri($"https://music.163.com/api/v1/album/{id}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 PrismWave/1.0.0");
            request.Headers.Referrer = new Uri("https://music.163.com/");
            using var response = await Http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("songs", out var songs)
                || songs.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<HomeTrackModel>();
            }

            var tracks = songs.EnumerateArray()
                .Select(ParseNeteaseAlbumTrack)
                .Where(track => track is not null)
                .Select(track => track!)
                .ToList();
            StartupLog.Write($"online.album.loaded: albumId={id}, tracks={tracks.Count}");
            return tracks;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StartupLog.Write($"online.album.failed: albumId={id}", exception);
            return Array.Empty<HomeTrackModel>();
        }
    }

    private static HomeTrackModel? ParseNeteaseAlbumTrack(JsonElement song)
    {
        var id = ReadLong(song, "id");
        var title = ReadString(song, "name");
        if (id <= 0 || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var artistsElement = song.TryGetProperty("ar", out var compactArtists)
            ? compactArtists
            : song.TryGetProperty("artists", out var legacyArtists) ? legacyArtists : default;
        var artist = artistsElement.ValueKind == JsonValueKind.Array
            ? string.Join(", ", artistsElement.EnumerateArray()
                .Select(item => ReadString(item, "name"))
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            : string.Empty;
        if (string.IsNullOrWhiteSpace(artist))
        {
            artist = "Unknown Artist";
        }

        var albumElement = song.TryGetProperty("al", out var compactAlbum)
            ? compactAlbum
            : song.TryGetProperty("album", out var legacyAlbum) ? legacyAlbum : default;
        var album = ReadString(albumElement, "name") ?? string.Empty;
        var cover = UpgradeCoverUrl(ReadString(albumElement, "picUrl"));
        var durationMs = ReadNumber(song, "dt");
        if (durationMs <= 0)
        {
            durationMs = ReadNumber(song, "duration");
        }

        return new HomeTrackModel(
            title,
            artist,
            album,
            FormatDurationMs(durationMs),
            "netease",
            cover,
            ProviderTrackId: id.ToString());
    }

    private static bool TryLoadHomeJson(out string path, out JsonDocument document)
    {
        foreach (var candidate in CandidateHomeFiles())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                document = JsonDocument.Parse(File.ReadAllText(candidate));
                path = candidate;
                return true;
            }
            catch
            {
            }
        }

        path = string.Empty;
        document = null!;
        return false;
    }

    private static IEnumerable<string> CandidateHomeFiles()
    {
        var now = BeijingNow();
        yield return CacheFileForDate(now.ToString("yyyy-MM-dd"));
        yield return Path.Combine(CacheDirectory(), "home.json");
        yield return CacheFileForDate(now.AddDays(-1).ToString("yyyy-MM-dd"));
        yield return BundledHomePath();
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "app", "assets", "home", "latest_home.json"));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "prismwave-hits", "home", "latest_home.json"));
    }

    private void ApplyDocument(JsonDocument document, string source)
    {
        var root = document.RootElement;
        if (!IsValidSchema8(root))
        {
            throw new InvalidDataException("Home data must use schema 8 and contain playable sections.");
        }

        GeneratedAt = ParseDate(root, "generatedAt") ?? DateTimeOffset.UtcNow;
        var editionDate = ReadString(root, "editionDate");
        RecommendationsPendingGeneration = IsPendingGeneration(editionDate);
        TopPlaylist = ParseSection(root.TryGetProperty("topPlaylist", out var top) ? top : default, "Trending");
        Sections = ParseSections(root);
        Albums = ParseAlbums(root);
        if (TopPlaylist.Tracks.Count == 0 && Sections.Count > 0)
        {
            TopPlaylist = Sections[0];
        }

        RecommendationsUnavailable = TopPlaylist.Tracks.Count == 0 && Sections.Count == 0;
        SourcePath = source;
        SourceDescription ??= source;
        Error = null;
    }

    private void SetUnavailable(string error)
    {
        TopPlaylist = EmptyTopPlaylist();
        Sections = Array.Empty<HomeSectionModel>();
        Albums = Array.Empty<AlbumModel>();
        RecommendationsUnavailable = true;
        RecommendationsPendingGeneration = false;
        Error = error;
        SourceDescription = null;
    }

    private static HomeSectionModel EmptyTopPlaylist()
    {
        return new HomeSectionModel(
            "daily-top-100",
            "Trending",
            "Top 100 from multi-platform trend signals",
            Array.Empty<HomeTrackModel>());
    }

    private static bool TryLoadDocument(string path, out JsonDocument document)
    {
        if (!File.Exists(path))
        {
            document = null!;
            return false;
        }

        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
            if (IsValidSchema8(document.RootElement))
            {
                return true;
            }

            document.Dispose();
        }
        catch
        {
        }

        document = null!;
        return false;
    }

    private static bool IsValidSchema8(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || ReadNumber(root, "schemaVersion") < 8)
        {
            return false;
        }

        var top = root.TryGetProperty("topPlaylist", out var rawTop)
            ? ParseSection(rawTop, "Trending")
            : EmptyTopPlaylist();
        return top.Tracks.Count > 0 || ParseSections(root).Count > 0;
    }

    private static async Task WriteCacheAsync(string json, string editionDate, CancellationToken cancellationToken)
    {
        var directory = CacheDirectory();
        Directory.CreateDirectory(directory);
        await WriteAtomicAsync(CacheFileForDate(editionDate), json, cancellationToken);
        await WriteAtomicAsync(Path.Combine(directory, "home.json"), json, cancellationToken);
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static string CacheDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrismWave",
            "online_home_cache");
    }

    private static string CacheFileForDate(string date)
    {
        return Path.Combine(CacheDirectory(), $"home-{date}.json");
    }

    private static string BundledHomePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "HomeFallback", "latest_home.json");
    }

    private static DateTimeOffset BeijingNow()
    {
        try
        {
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));
        }
        catch
        {
            return DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        }
    }

    private void Notify()
    {
        var dispatcher = App.DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            HomeChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            dispatcher.TryEnqueue(() => HomeChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    private static IReadOnlyList<HomeSectionModel> ParseSections(JsonElement root)
    {
        if (!root.TryGetProperty("sections", out var rawSections) || rawSections.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<HomeSectionModel>();
        }

        return rawSections.EnumerateArray()
            .Select(section => ParseSection(section, "Section"))
            .Where(section => section.Tracks.Count > 0)
            .ToList();
    }

    private static HomeSectionModel ParseSection(JsonElement section, string fallbackTitle)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            return new HomeSectionModel("empty", fallbackTitle, string.Empty, Array.Empty<HomeTrackModel>());
        }

        var id = ReadString(section, "id") ?? fallbackTitle;
        var title = ReadLocalizedTitle(section, fallbackTitle);
        var subtitle = ReadString(section, "subtitle") ?? string.Empty;
        var tracks = new List<HomeTrackModel>();
        if (section.TryGetProperty("tracks", out var rawTracks) && rawTracks.ValueKind == JsonValueKind.Array)
        {
            foreach (var track in rawTracks.EnumerateArray())
            {
                var parsed = ParseTrack(track);
                if (parsed is not null)
                {
                    tracks.Add(parsed);
                }
            }
        }

        return new HomeSectionModel(id, title, subtitle, tracks);
    }

    private static HomeTrackModel? ParseTrack(JsonElement track)
    {
        if (track.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = ReadString(track, "title");
        var artist = ReadString(track, "artist");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        var durationMs = ReadNumber(track, "durationMs");
        var provider = ReadString(track, "audioProvider") ?? ReadString(track, "provider") ?? "Online";
        return new HomeTrackModel(
            title,
            artist,
            ReadString(track, "album") ?? string.Empty,
            FormatDurationMs(durationMs),
            provider,
            ReadString(track, "coverUrl"),
            ReadString(track, "audioUrl"),
            ReadString(track, "providerTrackId"));
    }

    private static IReadOnlyList<AlbumModel> ParseAlbums(JsonElement root)
    {
        if (!root.TryGetProperty("albumRecommendations", out var rawAlbums) || rawAlbums.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AlbumModel>();
        }

        return rawAlbums.EnumerateArray()
            .Where(album => album.ValueKind == JsonValueKind.Object)
            .Select(album =>
            {
                var id = ReadLong(album, "albumId").ToString();
                return new AlbumModel(
                    id,
                    ReadString(album, "name") ?? "Unknown Album",
                    ReadString(album, "artist") ?? "Unknown Artist",
                    0,
                    ReadString(album, "coverUrl"));
            })
            .ToList();
    }

    private static string ReadLocalizedTitle(JsonElement section, string fallback)
    {
        if (!section.TryGetProperty("title", out var title))
        {
            return fallback;
        }

        if (title.ValueKind == JsonValueKind.String)
        {
            return title.GetString() ?? fallback;
        }

        if (title.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "zh-Hans", "zh-Hant", "en-US" })
            {
                if (title.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? fallback;
                }
            }
        }

        return fallback;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static int ReadNumber(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static long ReadLong(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            ? number
            : 0;
    }

    private static string? UpgradeCoverUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var value = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? $"https://{url[7..]}"
            : url;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Host.EndsWith("music.126.net", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("music.163.com", StringComparison.OrdinalIgnoreCase))
            && !uri.Query.Contains("param=", StringComparison.OrdinalIgnoreCase))
        {
            return $"{value}{(string.IsNullOrEmpty(uri.Query) ? "?" : "&")}param=512y512";
        }

        return value;
    }

    private static DateTimeOffset? ParseDate(JsonElement element, string property)
    {
        var value = ReadString(element, property);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string FormatDurationMs(int durationMs)
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

    private static bool IsPendingGeneration(string? editionDate)
    {
        if (string.IsNullOrWhiteSpace(editionDate))
        {
            return false;
        }

        var chinaTime = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));
        return editionDate != chinaTime.ToString("yyyy-MM-dd");
    }
}
