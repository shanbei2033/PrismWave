using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface ILibraryService
{
    IReadOnlyList<TrackModel> Tracks { get; }
    IReadOnlyList<string> Folders { get; }
    IReadOnlyList<AlbumModel> Albums { get; }
    IReadOnlyList<ArtistModel> Artists { get; }
    IReadOnlyList<TrackModel> Favorites { get; }
    bool IsScanning { get; }
    string? Error { get; }
    event EventHandler? LibraryChanged;
    Task AddFolderAsync(string folder);
    Task RemoveFolderAsync(string folder);
    Task RescanAsync();
    Task ToggleFavoriteAsync(TrackModel track);
    Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks);
    Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks);
    Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile);
    IReadOnlyList<TrackModel> GetAlbumTracks(string albumId);
    IReadOnlyList<TrackModel> GetArtistTracks(string artistName);
}
