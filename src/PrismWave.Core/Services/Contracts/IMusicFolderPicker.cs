using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IMusicFolderPicker
{
    Task<MusicFolderPickResult> PickAsync(CancellationToken cancellationToken = default);
}
