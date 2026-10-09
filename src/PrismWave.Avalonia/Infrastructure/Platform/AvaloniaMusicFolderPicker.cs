using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave.Avalonia.Infrastructure.Platform;

public sealed class AvaloniaMusicFolderPicker(Func<TopLevel?> topLevelProvider) : IMusicFolderPicker
{
    public async Task<MusicFolderPickResult> PickAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = topLevelProvider();
        if (topLevel?.StorageProvider is not { CanPickFolder: true } storage)
        {
            return MusicFolderPickResult.Failed("Folder picker is unavailable on this platform.");
        }

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "选择音乐文件夹"
        });

        var folder = result.Count > 0 ? result[0] : null;
        if (folder?.TryGetLocalPath() is not { Length: > 0 } localPath)
        {
            return folder is null
                ? MusicFolderPickResult.Canceled()
                : MusicFolderPickResult.Failed("所选文件夹不是本地路径。");
        }

        return MusicFolderPickResult.Selected(localPath);
    }
}
