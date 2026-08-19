using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface ILocalMusicScanner
{
    Task<LibraryScanResult> ScanAsync(
        IReadOnlyList<string> folders,
        IReadOnlyDictionary<string, string> customCoverPaths,
        IProgress<LibraryScanProgress>? progress,
        CancellationToken cancellationToken);
}
