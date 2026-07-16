using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using Windows.Storage.Pickers;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class WindowsMusicFolderPicker(Func<nint> windowHandleProvider) : IMusicFolderPicker
{
    private readonly SemaphoreSlim _pickerGate = new(1, 1);

    public async Task<MusicFolderPickResult> PickAsync(CancellationToken cancellationToken = default)
    {
        if (!await _pickerGate.WaitAsync(0, cancellationToken))
        {
            return MusicFolderPickResult.Failed("A music folder picker is already open.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var windowHandle = windowHandleProvider();
            if (windowHandle == 0)
            {
                return MusicFolderPickResult.Failed("The app window is not ready for folder selection.");
            }

            var picker = new FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return folder is null
                ? MusicFolderPickResult.Canceled()
                : MusicFolderPickResult.Selected(folder.Path);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return MusicFolderPickResult.Canceled();
        }
        catch (Exception exception)
        {
            return MusicFolderPickResult.Failed($"Could not open the music folder picker: {exception.Message}");
        }
        finally
        {
            _pickerGate.Release();
        }
    }
}
