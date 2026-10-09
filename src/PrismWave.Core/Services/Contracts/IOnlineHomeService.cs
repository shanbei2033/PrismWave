using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IOnlineHomeService
{
    HomeSectionModel TopPlaylist { get; }
    IReadOnlyList<HomeSectionModel> Sections { get; }
    IReadOnlyList<AlbumModel> Albums { get; }
    DateTimeOffset GeneratedAt { get; }
    bool RecommendationsUnavailable { get; }
    bool RecommendationsPendingGeneration { get; }
    bool IsRefreshing { get; }
    string? Error { get; }
    string? SourceDescription { get; }
    event EventHandler? HomeChanged;
    Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HomeTrackModel>> LoadAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default);
}
