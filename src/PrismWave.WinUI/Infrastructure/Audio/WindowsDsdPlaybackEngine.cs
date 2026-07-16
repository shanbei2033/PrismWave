using System.Runtime.InteropServices;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Audio;

public sealed class WindowsDsdPlaybackEngine : IDisposable
{
    private const uint BassPosByte = 0;
    private const uint BassSampleFloat = 0x100;
    private const uint BassUnicode = 0x80000000;
    private const uint BassStreamDecode = 0x200000;
    private const uint BassDsdRaw = 0x200;
    private const uint BassDsdDop = 0x400;
    private const uint BassAsioThread = 1;
    private const uint BassAttribDsdRate = 0x14001;
    private const uint BassActivePlaying = 1;
    private const uint BassAsioFormatDsdLsb = 32;
    private const uint BassAsioFormatDsdMsb = 33;

    private readonly object _gate = new();
    private Timer? _positionTimer;
    private uint _stream;
    private bool _bassInitialized;
    private bool _runtimeChecked;
    private bool _runtimeAvailable;
    private bool _isPlaying;
    private bool _completedFired;
    private bool _disposed;

    public double DurationSeconds { get; private set; }

    public double PositionSeconds
    {
        get
        {
            lock (_gate)
            {
                return _stream == 0
                    ? 0
                    : SafeSeconds(_stream, BassChannelGetPosition(_stream, BassPosByte));
            }
        }
    }

    public bool IsPlaying => _isPlaying;
    public bool IsAvailable => EnsureRuntimeAvailable();
    public bool UsingRawDsd { get; private set; }
    public string OutputModeLabel => UsingRawDsd ? "DSD Native (ASIO)" : "DSD over PCM (DoP)";
    public string? ActiveDeviceName { get; private set; }
    public string? FallbackReason { get; private set; }
    public event EventHandler? PlaybackEnded;

    public bool Play(string file, double volume, string selectedDevice, out string? error)
    {
        lock (_gate)
        {
            error = null;
            FallbackReason = null;
            try
            {
                if (!EnsureRuntimeAvailable())
                {
                    error = "BASS DSD runtime is not available.";
                    return false;
                }

                DisposeCurrentStream();
                EnsureBassInitialized();

                var rawStream = CreateDsdStream(file, BassDsdRaw | BassStreamDecode);
                if (rawStream == 0)
                {
                    error = $"BASS_DSD_StreamCreateFile failed with code {BassErrorGetCode()}.";
                    return false;
                }

                if (!BassChannelGetInfo(rawStream, out var channelInfo))
                {
                    BassStreamFree(rawStream);
                    error = $"BASS_ChannelGetInfo failed with code {BassErrorGetCode()}.";
                    return false;
                }

                var rawRate = BassChannelGetAttribute(rawStream, BassAttribDsdRate, out var dsdRate)
                    ? dsdRate
                    : 0;
                var requestedDevice = ParseDeviceId(selectedDevice);
                if (!TryInitializeAsio(requestedDevice, out var asioError))
                {
                    BassStreamFree(rawStream);
                    error = asioError;
                    return false;
                }

                CaptureActiveDevice();
                var effectiveStream = rawStream;
                var effectiveRate = (double)rawRate;
                var channelCount = (int)channelInfo.Channels;
                UsingRawDsd = BassAsioSetDsd(true);

                if (!UsingRawDsd)
                {
                    BassAsioSetDsd(false);
                    BassStreamFree(rawStream);
                    effectiveStream = CreateDsdStream(file, BassDsdDop | BassSampleFloat | BassStreamDecode);
                    if (effectiveStream == 0)
                    {
                        BassAsioFree();
                        error = $"BASS_DSD_StreamCreateFile (DoP) failed with code {BassErrorGetCode()}.";
                        return false;
                    }

                    if (!BassChannelGetInfo(effectiveStream, out channelInfo))
                    {
                        BassStreamFree(effectiveStream);
                        BassAsioFree();
                        error = $"BASS_ChannelGetInfo (DoP) failed with code {BassErrorGetCode()}.";
                        return false;
                    }

                    effectiveRate = channelInfo.Frequency;
                    channelCount = (int)channelInfo.Channels;
                    FallbackReason = "The selected ASIO device did not accept native DSD; using DoP.";
                }

                if (effectiveRate <= 0)
                {
                    CleanupFailedStream(effectiveStream);
                    error = "Resolved DSD output rate is invalid.";
                    return false;
                }

                if (!BassAsioSetRate(effectiveRate))
                {
                    var code = BassAsioErrorGetCode();
                    CleanupFailedStream(effectiveStream);
                    error = $"BASS_ASIO_SetRate failed with code {code}.";
                    return false;
                }

                if (!BassAsioChannelEnableBass(false, 0, effectiveStream, true))
                {
                    var code = BassAsioErrorGetCode();
                    CleanupFailedStream(effectiveStream);
                    error = $"BASS_ASIO_ChannelEnableBASS failed with code {code}.";
                    return false;
                }

                if (channelCount == 1)
                {
                    BassAsioChannelEnableMirror(1, false, 0);
                }

                BassAsioChannelSetVolume(false, -1, (float)Math.Clamp(volume, 0, 1));
                _stream = effectiveStream;
                DurationSeconds = SafeSeconds(_stream, BassChannelGetLength(_stream, BassPosByte));
                _completedFired = false;

                if (!BassAsioStart(0, 0))
                {
                    var code = BassAsioErrorGetCode();
                    DisposeCurrentStream();
                    error = $"BASS_ASIO_Start failed with code {code}.";
                    return false;
                }

                _isPlaying = true;
                StartPolling();
                StartupLog.Write($"windows.dsd.loaded: path={file}, device={ActiveDeviceName}, mode={OutputModeLabel}, duration={DurationSeconds:0.###}");
                return true;
            }
            catch (Exception exception)
            {
                DisposeCurrentStream();
                error = exception.Message;
                StartupLog.Write("windows.dsd.failed", exception);
                return false;
            }
        }
    }

    public IReadOnlyList<WindowsDsdDeviceModel> ListAvailableDevices()
    {
        lock (_gate)
        {
            var result = new List<WindowsDsdDeviceModel> { WindowsDsdDeviceModel.Automatic };
            if (!EnsureRuntimeAvailable())
            {
                return result;
            }

            if (_stream != 0)
            {
                return result;
            }

            for (uint id = 0; id < 32; id++)
            {
                if (!BassAsioGetDeviceInfo(id, out var deviceInfo))
                {
                    break;
                }

                var name = PtrToAnsi(deviceInfo.Name, $"ASIO {id}");
                var driver = PtrToAnsi(deviceInfo.Driver, string.Empty);
                var inputs = 0;
                var outputs = 0;
                var supportsNative = false;

                if (BassAsioInit((int)id, BassAsioThread))
                {
                    if (BassAsioGetInfo(out var asioInfo))
                    {
                        inputs = (int)asioInfo.Inputs;
                        outputs = (int)asioInfo.Outputs;
                        supportsNative = SupportsNativeDsd(asioInfo.Outputs);
                    }

                    BassAsioFree();
                }

                result.Add(new WindowsDsdDeviceModel(
                    id.ToString(),
                    name,
                    driver,
                    inputs,
                    outputs,
                    supportsNative));
            }

            StartupLog.Write($"windows.dsd.devices: count={result.Count - 1}");
            return result;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_stream == 0)
            {
                return;
            }

            BassAsioStop();
            _isPlaying = false;
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_stream == 0 || _isPlaying)
            {
                return;
            }

            if (BassAsioStart(0, 0))
            {
                _isPlaying = true;
                StartPolling();
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            DisposeCurrentStream();
        }
    }

    public void SetVolume(double volume)
    {
        lock (_gate)
        {
            if (_stream != 0)
            {
                BassAsioChannelSetVolume(false, -1, (float)Math.Clamp(volume, 0, 1));
            }
        }
    }

    public void Seek(double seconds)
    {
        lock (_gate)
        {
            if (_stream == 0)
            {
                return;
            }

            var clamped = DurationSeconds > 0
                ? Math.Clamp(seconds, 0, DurationSeconds)
                : Math.Max(0, seconds);
            var bytes = BassChannelSeconds2Bytes(_stream, clamped);
            BassChannelSetPosition(_stream, bytes, BassPosByte);
            _completedFired = false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeCurrentStream();
            if (_bassInitialized)
            {
                BassFree();
                _bassInitialized = false;
            }
        }
    }

    private bool EnsureRuntimeAvailable()
    {
        if (_runtimeChecked)
        {
            return _runtimeAvailable;
        }

        _runtimeChecked = true;
        var nativeDirectory = Path.Combine(AppContext.BaseDirectory, "Native");
        var required = new[] { "bass.dll", "bassdsd.dll", "bassasio.dll" };
        _runtimeAvailable = Directory.Exists(nativeDirectory)
            && required.All(file => File.Exists(Path.Combine(nativeDirectory, file)));
        if (_runtimeAvailable)
        {
            SetDllDirectory(nativeDirectory);
        }
        else
        {
            StartupLog.Write($"windows.dsd.runtimeMissing: directory={nativeDirectory}");
        }

        return _runtimeAvailable;
    }

    private void EnsureBassInitialized()
    {
        if (_bassInitialized)
        {
            return;
        }

        if (!BassInit(0, 48000, 0, IntPtr.Zero, IntPtr.Zero))
        {
            var error = BassErrorGetCode();
            if (error != 14)
            {
                throw new InvalidOperationException($"BASS_Init failed with code {error}.");
            }
        }

        _bassInitialized = true;
    }

    private bool TryInitializeAsio(int requestedDevice, out string? error)
    {
        error = null;
        if (BassAsioInit(requestedDevice, BassAsioThread))
        {
            return true;
        }

        var selectedError = BassAsioErrorGetCode();
        if (requestedDevice >= 0 && BassAsioInit(-1, BassAsioThread))
        {
            FallbackReason = $"ASIO device {requestedDevice} failed with code {selectedError}; using the default device.";
            StartupLog.Write($"windows.dsd.deviceFallback: selected={requestedDevice}, error={selectedError}");
            return true;
        }

        error = $"BASS_ASIO_Init failed with code {BassAsioErrorGetCode()}.";
        return false;
    }

    private void CaptureActiveDevice()
    {
        var device = BassAsioGetDevice();
        if (device == uint.MaxValue || !BassAsioGetDeviceInfo(device, out var info))
        {
            ActiveDeviceName = device == uint.MaxValue ? null : $"ASIO {device}";
            return;
        }

        ActiveDeviceName = PtrToAnsi(info.Name, $"ASIO {device}");
    }

    private bool SupportsNativeDsd(uint outputCount)
    {
        for (uint channel = 0; channel < outputCount; channel++)
        {
            if (BassAsioChannelGetInfo(false, channel, out var info)
                && info.Format is BassAsioFormatDsdLsb or BassAsioFormatDsdMsb)
            {
                return true;
            }
        }

        return false;
    }

    private void StartPolling()
    {
        _positionTimer?.Dispose();
        _positionTimer = new Timer(_ => PollPosition(), null, 100, 100);
    }

    private void PollPosition()
    {
        EventHandler? completed = null;
        lock (_gate)
        {
            if (_stream == 0 || !_isPlaying)
            {
                return;
            }

            var position = SafeSeconds(_stream, BassChannelGetPosition(_stream, BassPosByte));
            var active = BassChannelIsActive(_stream) == BassActivePlaying;
            if (!_completedFired
                && DurationSeconds > 0
                && (position >= DurationSeconds - 0.05 || (!active && position > 0)))
            {
                _completedFired = true;
                _isPlaying = false;
                completed = PlaybackEnded;
            }
        }

        completed?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeCurrentStream()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
        if (_stream != 0)
        {
            BassAsioStop();
            BassAsioFree();
            BassStreamFree(_stream);
        }

        _stream = 0;
        _isPlaying = false;
        _completedFired = false;
        UsingRawDsd = false;
        DurationSeconds = 0;
        ActiveDeviceName = null;
    }

    private static void CleanupFailedStream(uint stream)
    {
        BassStreamFree(stream);
        BassAsioFree();
    }

    private static uint CreateDsdStream(string file, uint flags)
    {
        return BassDsdStreamCreateFile(0, file, 0, 0, flags | BassUnicode, 0);
    }

    private static int ParseDeviceId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value, out var id)
                ? id
                : -1;
    }

    private static double SafeSeconds(uint stream, ulong bytes)
    {
        var value = BassChannelBytes2Seconds(stream, bytes);
        return double.IsFinite(value) && value > 0 ? value : 0;
    }

    private static string PtrToAnsi(IntPtr pointer, string fallback)
    {
        return pointer == IntPtr.Zero
            ? fallback
            : Marshal.PtrToStringAnsi(pointer)?.Trim() is { Length: > 0 } value ? value : fallback;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BassChannelInfo
    {
        public uint Frequency;
        public uint Channels;
        public uint Flags;
        public uint ChannelType;
        public uint OriginalResolution;
        public IntPtr Plugin;
        public IntPtr Sample;
        public IntPtr FileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BassAsioDeviceInfo
    {
        public IntPtr Name;
        public IntPtr Driver;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct BassAsioInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
        public uint Version;
        public uint Inputs;
        public uint Outputs;
        public uint BufferMin;
        public uint BufferMax;
        public uint BufferPreferred;
        public int BufferGranularity;
        public uint InitFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct BassAsioChannelInfo
    {
        public uint Group;
        public uint Format;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("bass.dll", EntryPoint = "BASS_Init")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassInit(int device, uint frequency, uint flags, IntPtr window, IntPtr classId);

    [DllImport("bass.dll", EntryPoint = "BASS_Free")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassFree();

    [DllImport("bass.dll", EntryPoint = "BASS_ErrorGetCode")]
    private static extern int BassErrorGetCode();

    [DllImport("bass.dll", EntryPoint = "BASS_StreamFree")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassStreamFree(uint stream);

    [DllImport("bass.dll", EntryPoint = "BASS_ChannelGetInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassChannelGetInfo(uint handle, out BassChannelInfo info);

    [DllImport("bass.dll", EntryPoint = "BASS_ChannelGetAttribute")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassChannelGetAttribute(uint handle, uint attribute, out float value);

    [DllImport("bass.dll", EntryPoint = "BASS_ChannelGetLength")]
    private static extern ulong BassChannelGetLength(uint handle, uint mode);

    [DllImport("bass.dll", EntryPoint = "BASS_ChannelGetPosition")]
    private static extern ulong BassChannelGetPosition(uint handle, uint mode);

    [DllImport("bass.dll", EntryPoint = "BASS_ChannelSetPosition")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassChannelSetPosition(uint handle, ulong position, uint mode);

    [DllImport("bass.dll", EntryPoint = "BASS_ChannelBytes2Seconds")]
    private static extern double BassChannelBytes2Seconds(uint handle, ulong position);

    [DllImport("bass.dll", EntryPoint = "BASS_ChannelSeconds2Bytes")]
    private static extern ulong BassChannelSeconds2Bytes(uint handle, double position);

    [DllImport("bass.dll", EntryPoint = "BASS_ChannelIsActive")]
    private static extern uint BassChannelIsActive(uint handle);

    [DllImport("bassdsd.dll", EntryPoint = "BASS_DSD_StreamCreateFile", CharSet = CharSet.Unicode)]
    private static extern uint BassDsdStreamCreateFile(
        uint fileType,
        [MarshalAs(UnmanagedType.LPWStr)] string file,
        ulong offset,
        ulong length,
        uint flags,
        uint frequency);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_ErrorGetCode")]
    private static extern uint BassAsioErrorGetCode();

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_GetDeviceInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioGetDeviceInfo(uint device, out BassAsioDeviceInfo info);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_GetDevice")]
    private static extern uint BassAsioGetDevice();

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_Init")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioInit(int device, uint flags);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_Free")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioFree();

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_GetInfo", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioGetInfo(out BassAsioInfo info);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_SetRate")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioSetRate(double rate);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_Start")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioStart(uint bufferLength, uint threads);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_Stop")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioStop();

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_SetDSD")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioSetDsd([MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_ChannelEnableBASS")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioChannelEnableBass(
        [MarshalAs(UnmanagedType.Bool)] bool input,
        uint channel,
        uint handle,
        [MarshalAs(UnmanagedType.Bool)] bool join);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_ChannelEnableMirror")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioChannelEnableMirror(
        uint channel,
        [MarshalAs(UnmanagedType.Bool)] bool input,
        uint sourceChannel);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_ChannelGetInfo", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioChannelGetInfo(
        [MarshalAs(UnmanagedType.Bool)] bool input,
        uint channel,
        out BassAsioChannelInfo info);

    [DllImport("bassasio.dll", EntryPoint = "BASS_ASIO_ChannelSetVolume")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BassAsioChannelSetVolume(
        [MarshalAs(UnmanagedType.Bool)] bool input,
        int channel,
        float volume);
}
