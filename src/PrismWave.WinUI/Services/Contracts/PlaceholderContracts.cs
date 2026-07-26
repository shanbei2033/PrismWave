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
    /// <summary>当前应用版本号，如 "1.0.3"</summary>
    string CurrentVersion { get; }

    /// <summary>最新版本号，未检测时为 null</summary>
    string? LatestVersion { get; }

    /// <summary>最新版的下载直链</summary>
    string? LatestDownloadUrl { get; }

    /// <summary>是否检测到新版本</summary>
    bool HasUpdate { get; }

    /// <summary>检测最新版本（异步）</summary>
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>当检测到新版本时触发</summary>
    event Action<UpdateCheckResult>? UpdateAvailable;
}

public sealed record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string? LatestVersion,
    string? DownloadUrl,
    string? ReleaseNotesUrl);

public interface IThemeService
{
    string ThemeName { get; }
}
