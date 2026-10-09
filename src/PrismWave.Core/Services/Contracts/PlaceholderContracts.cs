using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface ILyricsService
{
    Task<IReadOnlyList<LyricLineModel>> LoadLyricsAsync(TrackModel track, CancellationToken cancellationToken = default);
    Task<LyricsDocumentModel> LoadLyricsDocumentAsync(
        TrackModel track,
        string? sourceOverride = null,
        bool forceOnline = false,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LyricsSearchResultModel>> SearchOnlineLyricsAsync(
        TrackModel track,
        string query,
        CancellationToken cancellationToken = default);
    Task<LyricsDocumentModel> LoadSearchResultAsync(
        TrackModel track,
        LyricsSearchResultModel result,
        CancellationToken cancellationToken = default);
    Task<LyricsDocumentModel?> TryLoadWordSyncedLyricsDocumentAsync(
        TrackModel track,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<LyricsDocumentModel?>(null);
    string GetPreferredSource(TrackModel track);
    double GetOffsetSeconds(TrackModel track);
    Task SetPreferredSourceAsync(TrackModel track, string source);
    Task SetOffsetSecondsAsync(TrackModel track, double seconds);
}

public interface IWindowService
{
}

public interface IDialogService
{
}

public interface IUpdateService
{
}

public interface IThemeService
{
    string ThemeName { get; }
}
