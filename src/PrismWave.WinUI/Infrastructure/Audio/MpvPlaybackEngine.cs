using System.Runtime.InteropServices;
using System.Text;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Audio;

public sealed class MpvPlaybackEngine : IPlaybackEngine
{
    private const int MpvFormatFlag = 3;
    private const int MpvFormatDouble = 5;
    private const int MpvEventShutdown = 1;
    private const int MpvEventEndFileId = 7;
    private const int MpvEventFileLoadedId = 8;
    private const int MpvEventPlaybackRestartId = 21;
    private const int MpvEventPropertyChangeId = 10;
    private const int MpvEndFileReasonEof = 0;
    private const int MpvEndFileReasonStop = 2;

    private readonly object _gate = new();
    private readonly object _loadContextGate = new();
    private readonly Queue<EngineLoadContext> _pendingLoadContexts = new();
    private readonly Thread _eventThread;
    private IntPtr _handle;
    private bool _disposed;
    private bool _loaded;
    private int _suppressedStopEvents;
    private long _nextInternalLoadSequence;
    private long _latestLoadSequence;
    private EngineLoadContext? _openedLoadContext;

    public MpvPlaybackEngine(AudioOutputRoute route, string outputDevice)
    {
        var nativeDirectory = Path.Combine(AppContext.BaseDirectory, "Native");
        if (Directory.Exists(nativeDirectory))
        {
            SetDllDirectory(nativeDirectory);
        }

        _handle = mpv_create();
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("mpv_create failed.");
        }

        SetOption("terminal", "no");
        SetOption("sub-auto", "no");
        SetOption("cover-art-auto", "no");
        SetOption("audio-display", "no");
        SetOption("video", "no");
        SetOption("force-window", "no");
        SetOption("msg-level", "all=v");
        SetOption("cache-secs", "12");
        SetOption("demuxer-max-bytes", "20MiB");
        SetOption("demuxer-max-back-bytes", "10MiB");
        SetOption("cache-on-disk", "no");
        SetOption("audio-client-name", "PrismWave");
        ApplyOutputOptions(route, outputDevice);

        var initializeResult = mpv_initialize(_handle);
        if (initializeResult < 0)
        {
            throw new InvalidOperationException($"mpv_initialize failed: {ErrorString(initializeResult)}");
        }

        // Observe property changes so that time updates are triggered instead of only sampled.
        SubscribeDoubleProperty("time-pos");
        SubscribeDoubleProperty("duration");
        SubscribeFlagProperty("pause");

        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "PrismWave mpv event loop"
        };
        _eventThread.Start();
    }

    private void SubscribeDoubleProperty(string name)
    {
        if (_handle == IntPtr.Zero) return;
        Command("observe-property", name);
    }

    private void SubscribeFlagProperty(string name)
    {
        if (_handle == IntPtr.Zero) return;
        Command("observe-property", name);
    }

    public double PositionSeconds => GetDouble("time-pos");
    public double DurationSeconds => GetDouble("duration");
    public bool IsPlaying => _loaded && !GetFlag("pause");
    public string? Error { get; private set; }
    public event EventHandler? PlaybackEnded;
    public event EventHandler<PlaybackLoadEventArgs>? PlaybackStarted;
    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;
    public event EventHandler? StateChanged;

    public bool Load(TrackModel track, double volume, bool autoplay, out string? error)
    {
        var loadSequence = Interlocked.Decrement(ref _nextInternalLoadSequence);
        return Load(
            track,
            volume,
            autoplay,
            loadSequence,
            OnlinePlaybackCandidateKey.Create(track),
            out error);
    }

    public bool Load(
        TrackModel track,
        double volume,
        bool autoplay,
        long loadSequence,
        string sourceKey,
        out string? error)
    {
        error = null;
        Error = null;
        if (!TryResolveSource(track, out var source, out error))
        {
            Error = error;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        EngineLoadContext? loadContext = null;
        try
        {
            ApplyHeaders(track);
            SetVolume(volume);
            SetFlag("pause", !autoplay);
            Interlocked.Increment(ref _suppressedStopEvents);
            loadContext = RegisterPendingLoad(loadSequence, sourceKey);
            var result = Command("loadfile", source, "replace");
            if (result < 0)
            {
                loadContext.Cancelled = true;
                error = ErrorString(result);
                Error = error;
                StateChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }

            _loaded = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            if (loadContext is not null)
            {
                loadContext.Cancelled = true;
            }

            error = exception.Message;
            Error = error;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }

    public void Play()
    {
        SetFlag("pause", false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        SetFlag("pause", true);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        Interlocked.Increment(ref _suppressedStopEvents);
        Command("stop");
        _loaded = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(double seconds)
    {
        Command("seek", Math.Max(0, seconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "absolute");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVolume(double volume)
    {
        SetDouble("volume", Math.Clamp(volume, 0, 1) * 100d);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var handle = _handle;
        _handle = IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            mpv_command_string(handle, "quit");
            mpv_terminate_destroy(handle);
        }
    }

    private void EventLoop()
    {
        while (!_disposed)
        {
            try
            {
                var handle = _handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                var eventPointer = mpv_wait_event(handle, 0.25);
                if (eventPointer == IntPtr.Zero)
                {
                    continue;
                }

                var evt = Marshal.PtrToStructure<MpvEvent>(eventPointer);
                if (evt.EventId == MpvEventShutdown)
                {
                    return;
                }

                HandleEvent(evt);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                StartupLog.Write($"mpv event loop error: {exception}");
            }
        }
    }

    private void HandleEvent(MpvEvent evt)
    {
        if (evt.EventId == MpvEventPropertyChangeId)
        {
            // Property change events require additional data parsing for the name and format.
            // These trigger StateChanged to notify UI refresh in RefreshPosition().
            // For now, just log that we received a property change event so we know it's firing.
            var engine = GetDouble("time-pos");
            StartupLog.Write($"mpv property change: time-pos={engine} s");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (evt.EventId == MpvEventEndFileId)
        {
            if (evt.Data != IntPtr.Zero)
            {
                var endFile = Marshal.PtrToStructure<MpvEventEndFile>(evt.Data);
                if (endFile.Reason == MpvEndFileReasonStop && TryConsumeSuppressedStop())
                {
                    DiscardStoppedLoadContext();
                    return;
                }

                var completedContext = TakeCompletedLoadContext();
                var isLatest = completedContext is not null
                    && completedContext.LoadSequence == Volatile.Read(ref _latestLoadSequence);
                if (isLatest)
                {
                    _loaded = false;
                }

                if (endFile.Reason == MpvEndFileReasonEof)
                {
                    if (isLatest)
                    {
                        Error = null;
                        PlaybackEnded?.Invoke(this, EventArgs.Empty);
                    }
                }
                else if (endFile.Error < 0 && completedContext is not null)
                {
                    var failure = ErrorString(endFile.Error);
                    if (isLatest)
                    {
                        Error = failure;
                    }

                    StartupLog.Write($"mpv playback ended with error: {failure}");
                    PlaybackFailed?.Invoke(
                        this,
                        new PlaybackFailedEventArgs(
                            failure,
                            completedContext.LoadSequence,
                            completedContext.SourceKey));
                }
            }
            else
            {
                _loaded = false;
            }
        }

        if (evt.EventId == MpvEventFileLoadedId)
        {
            var openedContext = TakeOpenedLoadContext();
            if (openedContext is not null
                && openedContext.LoadSequence == Volatile.Read(ref _latestLoadSequence))
            {
                _loaded = false;
                Error = null;
            }
        }

        if (evt.EventId == MpvEventPlaybackRestartId)
        {
            var startedContext = TakePlaybackStartedContext();
            if (startedContext is not null)
            {
                if (startedContext.LoadSequence == Volatile.Read(ref _latestLoadSequence))
                {
                    _loaded = true;
                    Error = null;
                }

                PlaybackStarted?.Invoke(
                    this,
                    new PlaybackLoadEventArgs(
                        startedContext.LoadSequence,
                        startedContext.SourceKey));
            }
        }
    }

    private bool TryConsumeSuppressedStop()
    {
        while (true)
        {
            var current = Volatile.Read(ref _suppressedStopEvents);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _suppressedStopEvents, current - 1, current) == current)
            {
                return true;
            }
        }
    }

    private EngineLoadContext RegisterPendingLoad(long loadSequence, string sourceKey)
    {
        var context = new EngineLoadContext(loadSequence, sourceKey);
        lock (_loadContextGate)
        {
            _pendingLoadContexts.Enqueue(context);
            Volatile.Write(ref _latestLoadSequence, loadSequence);
        }

        return context;
    }

    private EngineLoadContext? TakeOpenedLoadContext()
    {
        lock (_loadContextGate)
        {
            var context = DequeuePendingLoadContext();
            _openedLoadContext = context;
            return context;
        }
    }

    private EngineLoadContext? TakeCompletedLoadContext()
    {
        lock (_loadContextGate)
        {
            if (_openedLoadContext is not null)
            {
                var context = _openedLoadContext;
                _openedLoadContext = null;
                return context;
            }

            return DequeuePendingLoadContext();
        }
    }

    private EngineLoadContext? TakePlaybackStartedContext()
    {
        lock (_loadContextGate)
        {
            if (_openedLoadContext is null || _openedLoadContext.Started)
            {
                return null;
            }

            _openedLoadContext.Started = true;
            return _openedLoadContext;
        }
    }

    private void DiscardStoppedLoadContext()
    {
        lock (_loadContextGate)
        {
            if (_openedLoadContext is not null)
            {
                _openedLoadContext = null;
                return;
            }

            _ = DequeuePendingLoadContext();
        }
    }

    private EngineLoadContext? DequeuePendingLoadContext()
    {
        while (_pendingLoadContexts.Count > 0)
        {
            var context = _pendingLoadContexts.Dequeue();
            if (!context.Cancelled)
            {
                return context;
            }
        }

        return null;
    }

    private static bool TryResolveSource(TrackModel track, out string source, out string? error)
    {
        source = track.PlaybackSource;
        error = null;
        if (string.IsNullOrWhiteSpace(source))
        {
            error = "Track has no playback source.";
            return false;
        }

        if (track.IsRemote)
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile))
            {
                return true;
            }

            error = "This online result still needs provider URL resolution before playback.";
            return false;
        }

        if (File.Exists(source))
        {
            source = Path.GetFullPath(source);
            return true;
        }

        error = "Local audio file does not exist.";
        return false;
    }

    private void ApplyHeaders(TrackModel track)
    {
        if (track.PlaybackHeaders is null || track.PlaybackHeaders.Count == 0)
        {
            SetOption("http-header-fields", string.Empty, "<none>");
            return;
        }

        var fields = string.Join(",", track.PlaybackHeaders.Select(pair => $"{pair.Key}: {pair.Value}"));
        SetOption(
            "http-header-fields",
            fields,
            PlaybackHeaderLogSanitizer.FormatHeaderNames(track.PlaybackHeaders));
    }

    private int Command(params string[] args)
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return -1;
            }

            var stringPointers = new IntPtr[args.Length];
            var argv = IntPtr.Zero;
            try
            {
                argv = Marshal.AllocHGlobal((args.Length + 1) * IntPtr.Size);
                for (var i = 0; i < args.Length; i++)
                {
                    stringPointers[i] = StringToUtf8(args[i]);
                    Marshal.WriteIntPtr(argv, i * IntPtr.Size, stringPointers[i]);
                }

                Marshal.WriteIntPtr(argv, args.Length * IntPtr.Size, IntPtr.Zero);
                return mpv_command(_handle, argv);
            }
            finally
            {
                foreach (var pointer in stringPointers)
                {
                    if (pointer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pointer);
                    }
                }

                if (argv != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(argv);
                }
            }
        }
    }

    private void SetOption(string name, string value, string? safeLogValue = null)
    {
        var result = mpv_set_option_string(_handle, name, value);
        if (result < 0)
        {
            StartupLog.Write(
                $"mpv option failed: {name}={safeLogValue ?? value}: {ErrorString(result)}");
        }
    }

    private void ApplyOutputOptions(AudioOutputRoute route, string outputDevice)
    {
        var device = string.IsNullOrWhiteSpace(outputDevice) ? "auto" : outputDevice.Trim();
        // mpv's canonical device name (as listed by --audio-device=help) is
        // "wasapi/{0.0.0.00000000}.{guid}" in every AO mode. A value without
        // the "<ao>/" prefix is parsed as an AO driver name, so the device is
        // never matched (silent fallback, broken position reporting).
        if (device.StartsWith("{0.", StringComparison.Ordinal))
        {
            device = $"wasapi/{device}";
        }

        if (route is AudioOutputRoute.WasapiShared or AudioOutputRoute.WasapiExclusive)
        {
            SetOption("ao", "wasapi");
        }

        SetOption(
            "audio-exclusive",
            route == AudioOutputRoute.WasapiExclusive ? "yes" : "no");
        if (route == AudioOutputRoute.WasapiExclusive)
        {
            SetOption("wasapi-exclusive-buffer", "50000");
        }

        SetOption("audio-device", device);
        StartupLog.Write(
            $"mpv route configured: output={route}, device={device}");
    }

    private void SetFlag(string name, bool value)
    {
        var flag = value ? 1 : 0;
        mpv_set_property(_handle, name, MpvFormatFlag, ref flag);
    }

    private bool GetFlag(string name)
    {
        var flag = 0;
        var result = mpv_get_property(_handle, name, MpvFormatFlag, ref flag);
        return result >= 0 && flag != 0;
    }

    private void SetDouble(string name, double value)
    {
        mpv_set_property(_handle, name, MpvFormatDouble, ref value);
    }

    private double GetDouble(string name)
    {
        var value = 0d;
        var result = mpv_get_property(_handle, name, MpvFormatDouble, ref value);
        return result >= 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : 0;
    }

    private static string ErrorString(int error)
    {
        var pointer = mpv_error_string(error);
        return pointer == IntPtr.Zero ? $"mpv error {error}" : Marshal.PtrToStringUTF8(pointer) ?? $"mpv error {error}";
    }

    private static IntPtr StringToUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var pointer = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        Marshal.WriteByte(pointer, bytes.Length, 0);
        return pointer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MpvEvent
    {
        public readonly int EventId;
        public readonly int Error;
        public readonly ulong ReplyUserData;
        public readonly IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MpvEventEndFile
    {
        public readonly int Reason;
        public readonly int Error;
        public readonly long PlaylistEntryId;
        public readonly int PlaylistInsertId;
        public readonly int PlaylistInsertNumEntries;
    }

    private sealed class EngineLoadContext(long loadSequence, string sourceKey)
    {
        public long LoadSequence { get; } = loadSequence;
        public string SourceKey { get; } = sourceKey;
        public bool Cancelled { get; set; }
        public bool Started { get; set; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command(IntPtr ctx, IntPtr args);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int mpv_command_string(IntPtr ctx, string args);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int mpv_set_option_string(IntPtr ctx, string name, string value);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int mpv_set_property(IntPtr ctx, string name, int format, ref int value);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int mpv_set_property(IntPtr ctx, string name, int format, ref double value);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int mpv_get_property(IntPtr ctx, string name, int format, ref int value);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int mpv_get_property(IntPtr ctx, string name, int format, ref double value);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_error_string(int error);
}
