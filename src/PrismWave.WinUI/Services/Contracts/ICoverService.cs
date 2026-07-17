using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface ICoverService
{
    event EventHandler<CoverChangedEventArgs>? CoverChanged;

    string? ResolveCoverPath(TrackModel track);

    Task<IReadOnlyList<CoverSearchResultModel>> SearchOnlineCoversAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken = default);

    Task<string> ApplyOnlineCoverAsync(
        TrackModel track,
        CoverSearchResultModel result,
        CancellationToken cancellationToken = default);
}
