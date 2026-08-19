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

        // PowerShell 7+ (pwsh.exe) defaults to UTF-8 for both console output
        // and Get-Content file reading, so Chinese characters display correctly
        // without any extra encoding switches.
        // Windows PowerShell 5.x (powershell.exe) decodes UTF-8-without-BOM as
        // ANSI/GBK producing mojibake; it is only used as a last-resort fallback.
        var (shell, arguments) = ResolveLogTailCommand();
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = shell,
                Arguments = arguments,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // pwsh.exe not found on this machine — fall back to Windows PowerShell.
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
        }
    }

    private (string Shell, string Arguments) ResolveLogTailCommand()
    {
        // Escape single quotes in file path to prevent PowerShell injection
        var escapedPath = FilePath.Replace("'", "''");
        
        // Prefer pwsh (PowerShell 7+) — native UTF-8, no encoding gymnastics needed.
        if (IsCommandAvailable("pwsh.exe"))
        {
            return (
                "pwsh.exe",
                $"-NoExit -Command \"Write-Host 'PrismWave Developer Log - Live Stream' -ForegroundColor Cyan; Write-Host 'File: {FilePath}' -ForegroundColor DarkGray; Write-Host ''; Get-Content -Path '{escapedPath}' -Wait -Tail 50 -Encoding UTF8\"");
        }

        // Windows PowerShell 5.x fallback — must force UTF-8 on both console and pipeline.
        return (
            "powershell.exe",
            $"-NoExit -Command \"chcp 65001 > $null; [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [System.Text.Encoding]::UTF8; Write-Host 'PrismWave Developer Log - Live Stream' -ForegroundColor Cyan; Write-Host 'File: {FilePath}' -ForegroundColor DarkGray; Write-Host ''; Get-Content -Path '{escapedPath}' -Wait -Tail 50 -Encoding UTF8\"");
    }

    private static bool IsCommandAvailable(string commandName)
    {
        try
        {
            var probe = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = commandName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            probe.Start();
            probe.WaitForExit(3000);
            return probe.ExitCode == 0
                && !string.IsNullOrWhiteSpace(probe.StandardOutput.ReadLine());
        }
        catch
        {
            return false;
        }
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
