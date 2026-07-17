namespace PrismWave_WinUI.Services.Contracts;

public interface IUiDispatcher
{
    void Enqueue(Action action);
}
