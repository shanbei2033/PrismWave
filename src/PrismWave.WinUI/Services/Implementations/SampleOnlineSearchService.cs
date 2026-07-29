using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class OnlineSearchService : IOnlineSearchService
{
    private const double LocalBoost = 0.3;
    private readonly ILibraryService _libraryService;
    private readonly IOnlineProviderService _providerService;

    public OnlineSearchService(
        ILibraryService libraryService,
        IOnlineProviderService providerService)
    {
        _libraryService = libraryService;
        _providerService = providerService;
    }

    public async Task<IReadOnlyList<SearchResultModel>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return Array.Empty<SearchResultModel>();
        }

        var local = SearchLocal(trimmed);
        var onlineTracks = await _providerService.SearchAsync(trimmed, cancellationToken);
        var online = onlineTracks
            .Select(track => new RankedResult(
                MapOnline(track),
                ScoreOnline(track, trimmed)))
            .ToList();

        var results = local
            .Concat(online)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Result.IsLocal)
            .ThenBy(item => item.Result.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(80)
            .Select(item => item.Result)
            .ToList();
        StartupLog.Write(
            $"online.search.complete: query=\"{trimmed}\", local={local.Count}, online={online.Count}, total={results.Count}");
        return results;
    }

    public Task<IReadOnlyList<SearchResultModel>> SearchLocalAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trimmed = query.Trim();
        return Task.FromResult<IReadOnlyList<SearchResultModel>>(
            trimmed.Length == 0
                ? Array.Empty<SearchResultModel>()
                : SearchLocal(trimmed)
                    .OrderByDescending(item => item.Score)
                    .Take(80)
                    .Select(item => item.Result)
                    .ToList());
    }

    public async Task<IReadOnlyList<SearchResultModel>> SearchProviderAsync(
        string query,
        string provider,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return Array.Empty<SearchResultModel>();
        }

        return (await _providerService.SearchProviderAsync(trimmed, provider, cancellationToken))
            .Select(track => new RankedResult(MapOnline(track), ScoreOnline(track, trimmed)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Result.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(80)
            .Select(item => item.Result)
            .ToList();
    }

    public Task<string?> ResolveCoverAsync(
        string title,
        string artist,
        CancellationToken cancellationToken = default)
        => _providerService.ResolveCoverFromDeezerAsync(title, artist, cancellationToken);

    private List<RankedResult> SearchLocal(string query)
    {
        return _libraryService.Tracks
            .Select(track => new { Track = track, Score = ScoreLocal(track, query) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Select(item => new RankedResult(
                new SearchResultModel(
                    item.Track.Title,
                    item.Track.Artist,
                    item.Track.Album,
                    "Local",
                    item.Track.Duration,
                    true,
                    item.Track.Path,
                    item.Track.CoverPath,
                    "local",
                    IsFavorite: item.Track.IsFavorite),
                item.Score + LocalBoost))
            .ToList();
    }

    private static SearchResultModel MapOnline(OnlineProviderTrackModel track) => new(
        track.Title,
        track.Artist,
        track.Album,
        track.ProviderLabel,
        FormatDuration(track.DurationSeconds),
        false,
        track.Descriptor,
        track.CoverUrl,
        track.Provider,
        track.ProviderTrackId,
        track.DirectAudioUrl,
        RequiresVip: track.RequiresVip);

    private static double ScoreLocal(TrackModel track, string query)
    {
        var title = track.Title.Trim();
        var artist = track.Artist.Trim();
        var album = track.Album.Trim();
        if (title.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1.0;
        if (artist.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0.95;
        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0.85;
        if (artist.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0.80;
        if (title.Contains(query, StringComparison.OrdinalIgnoreCase)) return 0.70;
        if (artist.Contains(query, StringComparison.OrdinalIgnoreCase)) return 0.60;
        if (album.Contains(query, StringComparison.OrdinalIgnoreCase)) return 0.45;
        return 0;
    }

    private static double ScoreOnline(OnlineProviderTrackModel track, string query)
    {
        var score = track.Title.Equals(query, StringComparison.OrdinalIgnoreCase)
            ? 0.90
            : track.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                ? 0.70
                : track.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    ? 0.55
                    : track.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
                        ? 0.40
                        : track.Album.Contains(query, StringComparison.OrdinalIgnoreCase)
                            ? 0.35
                            : 0.25;
        if (!string.IsNullOrWhiteSpace(track.DirectAudioUrl))
        {
            score += 0.05;
        }

        return score;
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return "--:--";
        }

        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private sealed record RankedResult(SearchResultModel Result, double Score);
}
