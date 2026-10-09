using Microsoft.UI.Dispatching;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class WinUiDispatcher(DispatcherQueue dispatcherQueue) : IUiDispatcher
{
    public bool HasThreadAccess => dispatcherQueue.HasThreadAccess;

    public void Enqueue(Action action)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _ = dispatcherQueue.TryEnqueue(() => action());
    }

    public IUiDispatcherTimer CreateTimer(TimeSpan interval) => new WinUiDispatcherTimer(dispatcherQueue, interval);

    private sealed class WinUiDispatcherTimer : IUiDispatcherTimer
    {
        private readonly DispatcherQueueTimer _timer;

        public WinUiDispatcherTimer(DispatcherQueue dispatcherQueue, TimeSpan interval)
        {
            _timer = dispatcherQueue.CreateTimer();
            _timer.Interval = interval;
            _timer.Tick += OnTick;
        }

        public event EventHandler? Tick;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public void Dispose() => Stop();

        private void OnTick(object? sender, object e) => Tick?.Invoke(this, EventArgs.Empty);
    }
}
