using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Audio;

public sealed class MpvPlaybackEngineHost : IDisposable
{
    private readonly IPlaybackEngineFactory _factory;
    private readonly AudioOutputFailoverState _failover;
    private IPlaybackEngine _engine;
    private bool _disposed;

    public MpvPlaybackEngineHost(
        IPlaybackEngineFactory factory,
        string preferredModeId,
        string outputDevice)
    {
        _factory = factory;
        _failover = new AudioOutputFailoverState(preferredModeId);
        OutputDevice = NormalizeDevice(outputDevice);
        _engine = CreateAvailableEngine();
        Attach(_engine);
    }

    public IPlaybackEngine Engine => _engine;
    public long Generation { get; private set; }
    public AudioOutputRoute ActiveRoute => _failover.ActiveRoute;
    public string ActiveRouteLabel => AudioOutputPolicy.GetRouteDisplayName(ActiveRoute);
    public string? FallbackReason => _failover.FallbackReason;
    public string PreferredModeId => _failover.PreferredModeId;
    public string OutputDevice { get; private set; }

    public event EventHandler<PlaybackLoadEventArgs>? PlaybackStarted;
    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;
    public event EventHandler? PlaybackEnded;
    public event EventHandler? StateChanged;

    public bool ResetPreference(string modeId, string outputDevice)
    {
        ThrowIfDisposed();
        var normalizedMode = AudioOutputPolicy.NormalizeModeId(modeId);
        var normalizedDevice = NormalizeDevice(outputDevice);
        var preferredRoute = AudioOutputPolicy.BuildFallbackChain(normalizedMode)[0];
        if (string.Equals(normalizedMode, PreferredModeId, StringComparison.Ordinal)
            && string.Equals(normalizedDevice, OutputDevice, StringComparison.OrdinalIgnoreCase)
            && ActiveRoute == preferredRoute)
        {
            return false;
        }

        _failover.Reset(normalizedMode);
        OutputDevice = normalizedDevice;
        ReplaceEngine("preference changed");
        return true;
    }

    public bool TryFallback(string reason)
    {
        ThrowIfDisposed();
        if (!_failover.TryAdvance(reason))
        {
            return false;
        }

        ReplaceEngine(reason);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RetireEngine(_engine);
    }

    private void ReplaceEngine(string reason)
    {
        var retired = _engine;
        Detach(retired);
        RetireEngine(retired, alreadyDetached: true);
        Generation++;
        _engine = CreateAvailableEngine();
        Attach(_engine);
        StartupLog.Write(
            $"mpv host replaced: generation={Generation}, preferred={PreferredModeId}, active={ActiveRoute}, device={OutputDevice}, reason={reason}");
    }

    private IPlaybackEngine CreateAvailableEngine()
    {
        while (true)
        {
            try
            {
                return _factory.Create(ActiveRoute, OutputDevice);
            }
            catch (Exception exception)
            {
                StartupLog.Write(
                    $"mpv host create failed: active={ActiveRoute}, device={OutputDevice}, error={exception.Message}");
                if (!_failover.TryAdvance($"{ActiveRoute} initialization failed: {exception.Message}"))
                {
                    throw;
                }
            }
        }
    }

    private void Attach(IPlaybackEngine engine)
    {
        engine.PlaybackStarted += Engine_PlaybackStarted;
        engine.PlaybackFailed += Engine_PlaybackFailed;
        engine.PlaybackEnded += Engine_PlaybackEnded;
        engine.StateChanged += Engine_StateChanged;
    }

    private void Detach(IPlaybackEngine engine)
    {
        engine.PlaybackStarted -= Engine_PlaybackStarted;
        engine.PlaybackFailed -= Engine_PlaybackFailed;
        engine.PlaybackEnded -= Engine_PlaybackEnded;
        engine.StateChanged -= Engine_StateChanged;
    }

    private void RetireEngine(IPlaybackEngine engine, bool alreadyDetached = false)
    {
        if (!alreadyDetached)
        {
            Detach(engine);
        }

        try
        {
            engine.Stop();
        }
        catch (Exception exception)
        {
            StartupLog.Write($"mpv host stop failed: {exception.Message}");
        }

        try
        {
            engine.Dispose();
        }
        catch (Exception exception)
        {
            StartupLog.Write($"mpv host dispose failed: {exception.Message}");
        }
    }

    private void Engine_PlaybackStarted(object? sender, PlaybackLoadEventArgs args)
    {
        if (ReferenceEquals(sender, _engine))
        {
            PlaybackStarted?.Invoke(this, args);
        }
    }

    private void Engine_PlaybackFailed(object? sender, PlaybackFailedEventArgs args)
    {
        if (ReferenceEquals(sender, _engine))
        {
            PlaybackFailed?.Invoke(this, args);
        }
    }

    private void Engine_PlaybackEnded(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, _engine))
        {
            PlaybackEnded?.Invoke(this, args);
        }
    }

    private void Engine_StateChanged(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, _engine))
        {
            StateChanged?.Invoke(this, args);
        }
    }

    private static string NormalizeDevice(string? outputDevice) =>
        string.IsNullOrWhiteSpace(outputDevice) ? "auto" : outputDevice.Trim();

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
