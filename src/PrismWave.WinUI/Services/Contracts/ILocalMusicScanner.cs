using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface ILocalMusicScanner
{
    Task<LibraryScanResult> ScanAsync(
        IReadOnlyList<string> folders,
        IReadOnlyDictionary<string, string> customCoverPaths,
        IProgress<LibraryScanProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>重读单个音频文件的元数据（用于元数据编辑后的局部刷新，避免全库重扫）。</summary>
    TrackModel ScanFile(string file, string? customCover = null) =>
        throw new NotSupportedException("Single-file scanning is not supported by this scanner.");
}
