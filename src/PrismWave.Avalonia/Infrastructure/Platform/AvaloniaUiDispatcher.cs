using Avalonia.Threading;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave.Avalonia.Infrastructure.Platform;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static AvaloniaUiDispatcher Instance { get; } = new();

    public bool HasThreadAccess => Dispatcher.UIThread.CheckAccess();

    public void Enqueue(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(action);
    }

    public IUiDispatcherTimer CreateTimer(TimeSpan interval) => new AvaloniaDispatcherTimer(interval);

    private sealed class AvaloniaDispatcherTimer : IUiDispatcherTimer
    {
        private readonly DispatcherTimer _timer;

        public AvaloniaDispatcherTimer(TimeSpan interval)
        {
            _timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) =>
                Tick?.Invoke(this, EventArgs.Empty));
        }

        public event EventHandler? Tick;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public void Dispose() => Stop();
    }
}
