namespace PrismWave_WinUI.Services.Contracts;

public interface IDeveloperLogService
{
    IReadOnlyList<string> Lines { get; }
    string FilePath { get; }
    event EventHandler? LogsChanged;
    void Write(string category, string message, Exception? exception = null);
    void Clear();
    void OpenLogFile();
}
