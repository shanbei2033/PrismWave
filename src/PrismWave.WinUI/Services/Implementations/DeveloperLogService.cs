using System.Diagnostics;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class DeveloperLogService : IDeveloperLogService, IDisposable
{
    private const int MaxLines = 1000;
    private readonly object _gate = new();
    private readonly List<string> _lines;

    public DeveloperLogService()
    {
        _lines = StartupLog.ReadRecent(MaxLines).ToList();
        StartupLog.LineWritten += StartupLog_LineWritten;
    }

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToList();
            }
        }
    }

    public string FilePath => StartupLog.FilePath;
    public event EventHandler? LogsChanged;

    public void Write(string category, string message, Exception? exception = null)
    {
        StartupLog.Write($"{category}.{message}", exception);
    }

    public void Clear()
    {
        StartupLog.Clear();
        lock (_gate)
        {
            _lines.Clear();
        }

        RaiseChanged();
    }

    public void OpenLogFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        if (!File.Exists(FilePath))
        {
            File.WriteAllText(FilePath, string.Empty);
        }

        Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
    }

    public void Dispose()
    {
        StartupLog.LineWritten -= StartupLog_LineWritten;
    }

    private void StartupLog_LineWritten(object? sender, string line)
    {
        lock (_gate)
        {
            if (_lines.Count >= MaxLines)
            {
                _lines.RemoveRange(0, _lines.Count - MaxLines + 1);
            }

            _lines.Add(line);
        }

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        var dispatcher = App.DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            LogsChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            dispatcher.TryEnqueue(() => LogsChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
