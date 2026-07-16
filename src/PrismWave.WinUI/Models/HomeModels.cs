namespace PrismWave_WinUI.Models;

public sealed record HomeTrackModel(
    string Title,
    string Artist,
    string Album,
    string Duration,
    string Provider,
    string? CoverUrl,
    string? AudioUrl = null,
    string? ProviderTrackId = null);

public sealed record HomeSectionModel(
    string Id,
    string Title,
    string Subtitle,
    IReadOnlyList<HomeTrackModel> Tracks);

public sealed record AlbumModel(
    string Id,
    string Title,
    string Artist,
    int TrackCount,
    string? CoverUrl);

public sealed record ArtistModel(
    string Name,
    string Initial,
    int TrackCount);

public sealed record SearchResultModel(
    string Title,
    string Artist,
    string Album,
    string Provider,
    string Duration,
    bool IsLocal,
    string? Source = null,
    string? CoverPath = null);
