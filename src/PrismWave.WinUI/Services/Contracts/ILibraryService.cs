using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface ILibraryService
{
    IReadOnlyList<TrackModel> Tracks { get; }
    IReadOnlyList<string> Folders { get; }
    IReadOnlyList<AlbumModel> Albums { get; }
    IReadOnlyList<ArtistModel> Artists { get; }
    IReadOnlyList<TrackModel> Favorites { get; }
    IReadOnlyList<LibraryFolderStatus> FolderStatuses => Folders
        .Select(path => new LibraryFolderStatus(path, Directory.Exists(path), Directory.Exists(path) ? null : "Unavailable"))
        .ToList();
    LibraryScanProgress ScanProgress => LibraryScanProgress.Idle;
    bool IsScanning { get; }
    string? Error { get; }
    event EventHandler? LibraryChanged;
    Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task AddFolderAsync(string folder);
    Task AddFolderAsync(string folder, CancellationToken cancellationToken) => AddFolderAsync(folder);
    Task RemoveFolderAsync(string folder);
    Task RemoveFolderAsync(string folder, CancellationToken cancellationToken) => RemoveFolderAsync(folder);
    Task RescanAsync();
    Task RescanAsync(CancellationToken cancellationToken) => RescanAsync();
    Task ToggleFavoriteAsync(TrackModel track);
    Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks);
    Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks);
    Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile);
    IReadOnlyList<TrackModel> GetAlbumTracks(string albumId);
    IReadOnlyList<TrackModel> GetArtistTracks(string artistName);
}
