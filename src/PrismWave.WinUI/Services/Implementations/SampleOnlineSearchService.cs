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
        var localKeys = local
            .Select(item => Identity(item.Result.Title, item.Result.Artist))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var online = onlineTracks
            .Where(track => !localKeys.Contains(Identity(track.Title, track.Artist)))
            .Select(track => new RankedResult(
                new SearchResultModel(
                    track.Title,
                    track.Artist,
                    track.Album,
                    track.ProviderLabel,
                    FormatDuration(track.DurationSeconds),
                    false,
                    track.Descriptor,
                    track.CoverUrl),
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
                    item.Track.CoverPath),
                item.Score + LocalBoost))
            .ToList();
    }

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

    private static string Identity(string title, string artist)
    {
        return $"{title.Trim().ToLowerInvariant()}|{artist.Trim().ToLowerInvariant()}";
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
