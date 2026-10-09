namespace PrismWave_WinUI.Services.Contracts;

public interface IUiDispatcher
{
    bool HasThreadAccess { get; }

    void Enqueue(Action action);

    IUiDispatcherTimer CreateTimer(TimeSpan interval);
}

public interface IUiDispatcherTimer : IDisposable
{
    void Start();

    void Stop();

    event EventHandler? Tick;
}
