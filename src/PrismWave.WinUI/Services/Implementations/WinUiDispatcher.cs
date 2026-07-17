using Microsoft.UI.Dispatching;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class WinUiDispatcher(DispatcherQueue dispatcherQueue) : IUiDispatcher
{
    public void Enqueue(Action action)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _ = dispatcherQueue.TryEnqueue(() => action());
    }
}
