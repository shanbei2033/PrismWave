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

    /// <summary>局部刷新单曲（重读该文件元数据并重建派生集合），避免全库重扫。返回是否找到并刷新。</summary>
    Task<bool> RefreshTrackAsync(TrackModel track) => Task.FromResult(false);
    Task ToggleFavoriteAsync(TrackModel track);
    Task AddOnlineTrackAsync(TrackModel track) => Task.CompletedTask;
    bool IsOnlineTrackInLibrary(string descriptor) => false;
    Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks);
    Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks);
    Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile);
    IReadOnlyList<TrackModel> GetAlbumTracks(string albumId);
    IReadOnlyList<TrackModel> GetArtistTracks(string artistName);
}
