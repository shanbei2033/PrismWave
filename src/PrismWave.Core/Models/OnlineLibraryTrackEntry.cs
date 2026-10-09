namespace PrismWave_WinUI.Models;

public sealed record OnlineLibraryTrackEntry(
    string Provider,
    string ProviderTrackId,
    string Path,
    string Title,
    string Artist,
    string Album,
    string Duration,
    string? CoverUrl,
    string? PlaybackUrl,
    double DurationSeconds,
    bool IsFavorite);
