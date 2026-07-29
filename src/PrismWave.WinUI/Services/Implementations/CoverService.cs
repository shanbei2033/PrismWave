using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Infrastructure.Http;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class CoverService : ICoverService
{
    private const int MaxSearchTerms = 4;
    private const int MaxSearchResults = 18;
    private const int MaxImageBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan SearchCacheLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly Regex AppleArtworkSizePattern = new(
        @"\d{2,4}x\d{2,4}bb",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BracketDecorationPattern = new(
        @"\[[^\]]*\]|\([^)]*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SearchDecorationPattern = new(
        @"\b(feat|ft|with)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RankingDecorationPattern = new(
        @"feat\.?|ft\.?|ver\.?|version|live|remix",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NonAlphaNumericPattern = new(
        @"[^\p{L}\p{Nd}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VariantPattern = new(
        @"\b(remix|edit|version|mix|live|vip|karaoke|instrumental|rework|sped\s*up|slowed|reverb)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly string _cacheRoot;
    private readonly string _imageCacheDirectory;
    private readonly string _searchCacheDirectory;
    private readonly SemaphoreSlim _musicBrainzGate = new(1, 1);
    private DateTimeOffset _lastMusicBrainzRequest = DateTimeOffset.MinValue;

    public CoverService(
        ISettingsService settingsService,
        HttpClient? httpClient = null,
        string? cacheRoot = null)
    {
        _settingsService = settingsService;
        _httpClient = SharedHttpClient.Resolve(httpClient);
        _cacheRoot = cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrismWave",
            "WinUI",
            "cover_cache");
        _imageCacheDirectory = Path.Combine(_cacheRoot, "images");
        _searchCacheDirectory = Path.Combine(_cacheRoot, "search_cache");
        Directory.CreateDirectory(_cacheRoot);
    }

    public event EventHandler<CoverChangedEventArgs>? CoverChanged;

    public string? ResolveCoverPath(TrackModel track)
    {
        if (_settingsService.Current.CustomCoverPaths is not { } customCovers)
        {
            return track.CoverPath;
        }

        var identityKey = TrackCoverIdentity.CreateKey(track.Title, track.Artist);
        var legacyKey = TrackKey(track);
        foreach (var key in new[] { identityKey, legacyKey }.Where(value => value.Length > 0))
        {
            if (customCovers.TryGetValue(key, out var customCover)
                && !string.IsNullOrWhiteSpace(customCover)
                && File.Exists(customCover))
            {
                return customCover;
            }
        }

        return track.CoverPath;
    }

    public async Task<IReadOnlyList<CoverSearchResultModel>> SearchOnlineCoversAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? track.Title.Trim() : query.Trim();
        var cached = await LoadSearchCacheAsync(track, normalizedQuery, cancellationToken);
        if (cached.Count > 0)
        {
            StartupLog.Write($"cover.search.cache-hit: track={track.Id}, count={cached.Count}");
            return cached;
        }

        var appleResults = await SearchAppleAsync(track, normalizedQuery, cancellationToken);
        var enoughAppleResults = HasEnoughCoverage(appleResults);
        var deezerResults = enoughAppleResults
            ? Array.Empty<CoverSearchResultModel>()
            : await SearchDeezerAsync(track, normalizedQuery, cancellationToken);
        var fallbackResults = enoughAppleResults || HasEnoughCoverage(deezerResults)
            ? Array.Empty<CoverSearchResultModel>()
            : await SearchMusicBrainzAsync(track, normalizedQuery, cancellationToken);

        var merged = MergeAndRank(
            track,
            normalizedQuery,
            appleResults.Concat(deezerResults).Concat(fallbackResults));
        if (merged.Count > 0)
        {
            await SaveSearchCacheAsync(track, normalizedQuery, merged, cancellationToken);
        }

        StartupLog.Write(
            $"cover.search.complete: track={track.Id}, apple={appleResults.Count}, deezer={deezerResults.Count}, musicbrainz={fallbackResults.Count}, total={merged.Count}");
        return merged;
    }

    public async Task<string> ApplyOnlineCoverAsync(
        TrackModel track,
        CoverSearchResultModel result,
        CancellationToken cancellationToken = default)
    {
        var urls = new[] { result.ThumbnailUrl, result.FullImageUrl }
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0)
        {
            throw new InvalidDataException("The selected cover has no downloadable image URL.");
        }

        DownloadedImage? downloaded = null;
        foreach (var url in urls)
        {
            downloaded = await DownloadImageAsync(url, cancellationToken);
            if (downloaded is not null)
            {
                break;
            }
        }

        if (downloaded is null)
        {
            throw new InvalidDataException("The selected cover did not return a supported image.");
        }

        Directory.CreateDirectory(_imageCacheDirectory);
        var key = TrackCoverIdentity.CreateKey(track.Title, track.Artist);
        if (key.Length == 0)
        {
            key = TrackKey(track);
        }
        var contentHash = Convert.ToHexString(
            SHA256.HashData(downloaded.Bytes)).ToLowerInvariant();
        var destination = Path.Combine(
            _imageCacheDirectory,
            $"{StableHash(key.ToLowerInvariant() + ":" + contentHash)}{downloaded.Extension}");
        await WriteAtomicAsync(destination, downloaded.Bytes, cancellationToken);

        var customCovers = new Dictionary<string, string>(
            _settingsService.Current.CustomCoverPaths
                ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase)
        {
            [key] = destination
        };
        await _settingsService.SaveAsync(_settingsService.Current with { CustomCoverPaths = customCovers });
        StartupLog.Write(
            $"cover.apply: track={track.Id}, source={result.Source}, bytes={downloaded.Bytes.Length}, path={destination}");
        CoverChanged?.Invoke(this, new CoverChangedEventArgs(
            track.Id,
            track.Path,
            destination,
            track.Title,
            track.Artist));
        return destination;
    }

    private async Task<IReadOnlyList<CoverSearchResultModel>> SearchAppleAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken)
    {
        var results = new List<CoverSearchResultModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in BuildSearchTerms(track, query).Take(MaxSearchTerms))
        {
            var uri = new Uri(
                $"https://itunes.apple.com/search?term={Uri.EscapeDataString(term)}&media=music&entity=song&limit=14&lang=zh_cn");
            using var document = await RequestJsonAsync(uri, cancellationToken);
            if (document is null
                || !document.RootElement.TryGetProperty("results", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                var artwork = ReadString(item, "artworkUrl100");
                var title = ReadString(item, "trackName");
                var artist = ReadString(item, "artistName");
                if (string.IsNullOrWhiteSpace(artwork)
                    || string.IsNullOrWhiteSpace(title)
                    || string.IsNullOrWhiteSpace(artist))
                {
                    continue;
                }

                var fullUrl = UpgradeAppleArtworkUrl(artwork, 1200);
                var dedupeKey = $"{title}|{artist}|{fullUrl}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                var id = ReadString(item, "trackId")
                    ?? ReadString(item, "collectionId")
                    ?? fullUrl;
                results.Add(new CoverSearchResultModel(
                    $"apple:{id}",
                    title,
                    artist,
                    ReadString(item, "collectionName") ?? string.Empty,
                    UpgradeAppleArtworkUrl(artwork, 300),
                    fullUrl,
                    "apple"));
            }

            if (results.Count >= 10)
            {
                break;
            }
        }

        return MergeAndRank(track, query, results);
    }

    private async Task<IReadOnlyList<CoverSearchResultModel>> SearchDeezerAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken)
    {
        var results = new List<CoverSearchResultModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in BuildSearchTerms(track, query).Take(MaxSearchTerms))
        {
            var uri = new Uri(
                $"https://api.deezer.com/search?q={Uri.EscapeDataString(term)}&limit=14");
            using var document = await RequestJsonAsync(uri, cancellationToken);
            if (document is null
                || !document.RootElement.TryGetProperty("data", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                var title = ReadString(item, "title");
                if (string.IsNullOrWhiteSpace(title)
                    || !item.TryGetProperty("artist", out var artistElement)
                    || !item.TryGetProperty("album", out var albumElement))
                {
                    continue;
                }

                var artist = ReadString(artistElement, "name");
                var album = ReadString(albumElement, "title") ?? string.Empty;
                var thumbnail = ReadString(albumElement, "cover_medium")
                    ?? ReadString(albumElement, "cover");
                var full = ReadString(albumElement, "cover_xl")
                    ?? ReadString(albumElement, "cover_big")
                    ?? thumbnail;
                if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(full))
                {
                    continue;
                }

                var dedupeKey = $"{title}|{artist}|{full}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                results.Add(new CoverSearchResultModel(
                    $"deezer:{ReadString(item, "id") ?? full}",
                    title,
                    artist,
                    album,
                    thumbnail ?? full,
                    full,
                    "deezer"));
            }

            if (results.Count >= 10)
            {
                break;
            }
        }

        return MergeAndRank(track, query, results);
    }

    private async Task<IReadOnlyList<CoverSearchResultModel>> SearchMusicBrainzAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken)
    {
        await WaitForMusicBrainzSlotAsync(cancellationToken);
        var searchTerm = string.Join(
            ' ',
            new[] { query, track.Artist, track.Album }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var uri = new Uri(
            $"https://musicbrainz.org/ws/2/release-group?query={Uri.EscapeDataString(searchTerm)}&fmt=json&limit=10");
        using var document = await RequestJsonAsync(uri, cancellationToken);
        if (document is null
            || !document.RootElement.TryGetProperty("release-groups", out var groups)
            || groups.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CoverSearchResultModel>();
        }

        var candidates = groups.EnumerateArray()
            .Select(ParseReleaseGroup)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .Take(8)
            .ToList();
        var covers = await Task.WhenAll(candidates.Select(candidate =>
            ResolveMusicBrainzCoverAsync(candidate, cancellationToken)));
        return MergeAndRank(track, query, covers.Where(cover => cover is not null).Select(cover => cover!));
    }

    private async Task<CoverSearchResultModel?> ResolveMusicBrainzCoverAsync(
        ReleaseGroupCandidate candidate,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://coverartarchive.org/release-group/{Uri.EscapeDataString(candidate.Id)}");
        using var document = await RequestJsonAsync(uri, cancellationToken);
        if (document is null
            || !document.RootElement.TryGetProperty("images", out var images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var image in images.EnumerateArray())
        {
            if (ReadBool(image, "approved") == false || ReadBool(image, "front") != true)
            {
                continue;
            }

            var full = ReadString(image, "image");
            string? thumbnail = null;
            if (image.TryGetProperty("thumbnails", out var thumbnails))
            {
                thumbnail = ReadString(thumbnails, "500")
                    ?? ReadString(thumbnails, "250")
                    ?? ReadString(thumbnails, "small");
            }

            if (!string.IsNullOrWhiteSpace(full) && !string.IsNullOrWhiteSpace(thumbnail))
            {
                return new CoverSearchResultModel(
                    candidate.Id,
                    candidate.Title,
                    candidate.Artist,
                    candidate.Album,
                    thumbnail,
                    full,
                    "musicbrainz");
            }
        }

        return null;
    }

    private async Task<JsonDocument?> RequestJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<DownloadedImage?> DownloadImageAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
            request.Headers.UserAgent.ParseAdd("PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength is > MaxImageBytes)
            {
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var destination = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, timeout.Token);
                if (read == 0)
                {
                    break;
                }

                if (destination.Length + read > MaxImageBytes)
                {
                    return null;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            var bytes = destination.ToArray();
            var extension = DetectImageExtension(bytes);
            return extension is null ? null : new DownloadedImage(bytes, extension);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private IReadOnlyList<CoverSearchResultModel> MergeAndRank(
        TrackModel track,
        string query,
        IEnumerable<CoverSearchResultModel> results)
    {
        return results
            .Where(result => !string.IsNullOrWhiteSpace(result.FullImageUrl))
            .GroupBy(
                result => $"{Normalize(result.Title)}|{Normalize(result.Artist)}|{result.FullImageUrl}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(result => result with { Score = ScoreResult(track, query, result) })
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(result => result.Artist, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxSearchResults)
            .ToList();
    }

    private static int ScoreResult(TrackModel track, string query, CoverSearchResultModel result)
    {
        var score = 0;
        var queryKey = Normalize(query);
        var titleKey = Normalize(track.Title);
        var artistKey = Normalize(track.Artist);
        var albumKey = Normalize(track.Album);
        var resultTitleKey = Normalize(result.Title);
        var resultArtistKey = Normalize(result.Artist);
        var resultAlbumKey = Normalize(result.Album);

        var titleExact = titleKey.Length > 0 && titleKey == resultTitleKey;
        var artistExact = artistKey.Length > 0 && artistKey == resultArtistKey;
        score += MatchScore(titleKey, resultTitleKey, 52, 22);
        score += MatchScore(artistKey, resultArtistKey, 38, 8);
        score += MatchScore(albumKey, resultAlbumKey, 18, 8);
        if (!string.IsNullOrWhiteSpace(queryKey)
            && (resultTitleKey.Contains(queryKey, StringComparison.Ordinal)
                || queryKey.Contains(resultTitleKey, StringComparison.Ordinal)
                || resultAlbumKey.Contains(queryKey, StringComparison.Ordinal)))
        {
            score += 10;
        }

        score += titleExact && artistExact ? result.Source.ToLowerInvariant() switch
        {
            "apple" => 12,
            "deezer" => 10,
            "musicbrainz" => 4,
            _ => 0
        } : 0;
        if (!artistExact)
        {
            score -= 36;
        }

        var requestedVariants = VariantPattern.Matches(track.Title)
            .Select(match => match.Value.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resultVariants = VariantPattern.Matches(result.Title)
            .Select(match => match.Value.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        score -= resultVariants.Except(requestedVariants).Count() * 30;
        return score;
    }

    private static int MatchScore(string expected, string actual, int exact, int partial)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return 0;
        }

        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return exact;
        }

        return expected.Contains(actual, StringComparison.Ordinal)
            || actual.Contains(expected, StringComparison.Ordinal)
            ? partial
            : 0;
    }

    private static IReadOnlyList<string> BuildSearchTerms(TrackModel track, string query)
    {
        var cleanQuery = StripSearchDecorations(query);
        var cleanTitle = StripSearchDecorations(track.Title);
        var cleanArtist = StripSearchDecorations(track.Artist);
        var cleanAlbum = StripSearchDecorations(track.Album);
        var simplifiedTitle = SimplifyTitle(string.IsNullOrWhiteSpace(cleanTitle) ? query : cleanTitle);
        var terms = new[]
        {
            ComposeQuery(query, cleanArtist),
            query,
            cleanQuery,
            $"{track.Title} {track.Artist}",
            $"{cleanTitle} {cleanArtist}",
            $"{simplifiedTitle} {cleanArtist}",
            $"{cleanTitle} {cleanArtist} {cleanAlbum}",
            $"{simplifiedTitle} {cleanArtist} {cleanAlbum}",
            $"{cleanAlbum} {cleanArtist}",
            cleanTitle
        };
        return terms
            .Select(term => Regex.Replace(term, @"\s+", " ").Trim())
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static string ComposeQuery(string query, string artist)
    {
        return !string.IsNullOrWhiteSpace(artist)
            && !Normalize(query).Contains(Normalize(artist), StringComparison.Ordinal)
                ? $"{query} {artist}".Trim()
                : query.Trim();
    }

    private static string StripSearchDecorations(string value)
    {
        var withoutBrackets = BracketDecorationPattern.Replace(value, " ");
        var withoutFeatures = SearchDecorationPattern.Replace(withoutBrackets, " ");
        return Regex.Replace(withoutFeatures, @"\s+", " ").Trim();
    }

    private static string SimplifyTitle(string value)
    {
        var separator = Regex.Match(value, @"\s[-:|/]\s");
        if (separator.Success)
        {
            var left = value[..separator.Index].Trim();
            var right = value[(separator.Index + separator.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(left) && VariantPattern.IsMatch(right))
            {
                return left;
            }
        }

        var simplified = Regex.Replace(VariantPattern.Replace(value, " "), @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(simplified) ? value.Trim() : simplified;
    }

    private static string Normalize(string input)
    {
        var value = BracketDecorationPattern.Replace(input.ToLowerInvariant(), string.Empty);
        value = RankingDecorationPattern.Replace(value, string.Empty);
        return NonAlphaNumericPattern.Replace(value, string.Empty);
    }

    private static string UpgradeAppleArtworkUrl(string input, int size)
    {
        return AppleArtworkSizePattern.Replace(input, $"{size}x{size}bb", 1);
    }

    private static bool HasEnoughCoverage(IReadOnlyList<CoverSearchResultModel> results)
    {
        return results.Count >= 6 || (results.Count > 0 && results[0].Score >= 84);
    }

    private async Task<IReadOnlyList<CoverSearchResultModel>> LoadSearchCacheAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken)
    {
        var path = SearchCachePath(track, query);
        if (!File.Exists(path))
        {
            return Array.Empty<CoverSearchResultModel>();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var cache = await JsonSerializer.DeserializeAsync<SearchCacheEnvelope>(
                stream,
                JsonOptions,
                cancellationToken);
            if (cache is null
                || DateTimeOffset.UtcNow - cache.SavedAt > SearchCacheLifetime
                || cache.Results.Count == 0)
            {
                return Array.Empty<CoverSearchResultModel>();
            }

            return cache.Results
                .Where(result => !string.IsNullOrWhiteSpace(result.ThumbnailUrl)
                    && !string.IsNullOrWhiteSpace(result.FullImageUrl))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return Array.Empty<CoverSearchResultModel>();
        }
    }

    private async Task SaveSearchCacheAsync(
        TrackModel track,
        string query,
        IReadOnlyList<CoverSearchResultModel> results,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_searchCacheDirectory);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new SearchCacheEnvelope(DateTimeOffset.UtcNow, results.ToList()),
            JsonOptions);
        await WriteAtomicAsync(SearchCachePath(track, query), payload, cancellationToken);
    }

    private string SearchCachePath(TrackModel track, string query)
    {
        var raw = $"v2::{TrackKey(track).ToLowerInvariant()}::{Normalize(query)}::{Normalize(track.Artist)}::{Normalize(track.Album)}";
        return Path.Combine(_searchCacheDirectory, $"{StableHash(raw)}.json");
    }

    private async Task WaitForMusicBrainzSlotAsync(CancellationToken cancellationToken)
    {
        await _musicBrainzGate.WaitAsync(cancellationToken);
        try
        {
            var delay = TimeSpan.FromMilliseconds(1100) - (DateTimeOffset.UtcNow - _lastMusicBrainzRequest);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            _lastMusicBrainzRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            _musicBrainzGate.Release();
        }
    }

    private static ReleaseGroupCandidate? ParseReleaseGroup(JsonElement element)
    {
        var id = ReadString(element, "id");
        var title = ReadString(element, "title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var artists = new List<string>();
        if (element.TryGetProperty("artist-credit", out var credits)
            && credits.ValueKind == JsonValueKind.Array)
        {
            foreach (var credit in credits.EnumerateArray())
            {
                var name = ReadString(credit, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    artists.Add(name);
                }
            }
        }

        return new ReleaseGroupCandidate(id, title, string.Join(", ", artists), title);
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string TrackKey(TrackModel track)
    {
        return !string.IsNullOrWhiteSpace(track.Path) ? track.Path : track.Id;
    }

    private static string? DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 12
            && bytes[0] == 0x52
            && bytes[1] == 0x49
            && bytes[2] == 0x46
            && bytes[3] == 0x46
            && bytes[8] == 0x57
            && bytes[9] == 0x45
            && bytes[10] == 0x42
            && bytes[11] == 0x50)
        {
            return ".webp";
        }

        if (bytes.Length >= 6
            && bytes[0] == 0x47
            && bytes[1] == 0x49
            && bytes[2] == 0x46)
        {
            return ".gif";
        }

        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return ".bmp";
        }

        return null;
    }

    private static async Task WriteAtomicAsync(
        string destination,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string StableHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record DownloadedImage(byte[] Bytes, string Extension);
    private sealed record ReleaseGroupCandidate(string Id, string Title, string Artist, string Album);
    private sealed record SearchCacheEnvelope(DateTimeOffset SavedAt, List<CoverSearchResultModel> Results);
}
