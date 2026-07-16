using System.Runtime.InteropServices;
using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class BundledLibMpvCodecTests
{
    private const string SilentEac3M4aGzipBase64 =
        "H4sIAAAAAAACCu2dQUgUURjHv1VSQTMhD0JmG3jMZc2SKCkvQUS38JB0aNw3i8vOc5aZ0dUI6lSn8NKtg90iQrqUFRshEXkqo4OHYksQEjpsLNJBEOx7b3ZpbCIml73E/zHDmzfvvW/e95udmd9tiSie9mZyGdeWRA2kajE208/1UZk71k9ELWnHNImutEhheK35Pdtnbi2SKsO832nqMm4e2ni4vjx3+trmxu6q6+8e1TB7s7x98uXuZ2+sL219q2H28lzt6d9TOJspaokVi8W1pdsLnyRvKnXpHwa20ZUn8+NqbOONyGFLpVJ57Y0f4AuvLhRUzmdHVx7rFSxGjprP56fLa34Am7MNBV3gpWZH9djVyFFPFQovpst+gKcMMUwgqxDosbnIUQ8Wi5+HpisB+N4MhharlrqikZ4g4K073iilvVT63jNUCcBP1N3QYj8qBOptRd0EvHXHG6U0M9jZnsoK+T34Nvw0KAT6xdhOwFt/vBFKY6FQWJo94Afgz87X8NOgELwPfM2At6546yMLwPufulitZf8r+D/8HwYF/4f/Ay/8H4IK/4f/Ay/8H/4P/4f/Ay/8H3jh/zAo+D/8H3jh//B/+D/8H3jh/xBU+D/8H3jh//B/+D/8H/4P/wde+D8EFf4P/wde+D/8H/4P/4f/w/+BF/4PQYX/w/+BF/4P/4f/w//h//B/CCr8H/4PvPB/4IX/w//h//B/+D/8H4IK/4f/Ay/8H4IK/4f/18//+T8C7kvbnuKGJafGxc7+AieYWKUYqS3A6HdmO5vDf79oA0/44DlGlo8ve1l9zcZwtETw+xCL/ft1e03huep3alqu92uGiltNKHZeiozBB3Ep/pR7kkZe60bfuLCcao9rT04ER17ktjhnTAjL1GN+yMxEmusOV+4I2iv8893CMdOBFNomHSteOX7uemMW1yOu54rAmLNmqm8glHYTdVSXymWvMFMDXRTfpxqdY57jZ936zN+JulxPE6kGaVER1IL4YqnA+Uqfrju576rf1UaV83wuZQfGJ3k3JoWnSF6Spq5VORykxqQdI5ezguSOZPxb0/vAs21NxtCTdYYXjKn04EDieKI/mfwJ7aTRf9diAAA=";

    [Fact]
    public void BundledLibMpv_DecodesEac3Audio()
    {
        var dllPath = FindRepositoryFile("native", "libmpv-winui", "libmpv-2.dll");
        var mediaPath = WriteEac3Fixture();

        try
        {
            using var probe = new LibMpvProbe(dllPath);
            var result = probe.PlayToNullAudio(mediaPath, TimeSpan.FromSeconds(4));

            Assert.True(result.Started, result.Diagnostic);
        }
        finally
        {
            File.Delete(mediaPath);
        }
    }

    [Fact]
    public void BundledLibMpv_SequentialAudioOnlyLoadsRestartWithoutOpeningVideo()
    {
        var dllPath = FindRepositoryFile("native", "libmpv-winui", "libmpv-2.dll");
        var mediaPath = WriteEac3Fixture();
        try
        {
            using var probe = new LibMpvProbe(dllPath);
            var first = probe.PlayToNullAudio(mediaPath, TimeSpan.FromSeconds(4));
            var second = probe.PlayToNullAudio(mediaPath, TimeSpan.FromSeconds(4));
            var third = probe.PlayToNullAudio(mediaPath, TimeSpan.FromSeconds(4));

            Assert.True(first.Started, first.Diagnostic);
            Assert.True(second.Started, second.Diagnostic);
            Assert.True(third.Started, third.Diagnostic);
        }
        finally
        {
            File.Delete(mediaPath);
        }
    }

    [Fact]
    public void WinUiProject_AlwaysStagesDedicatedLibMpvBinary()
    {
        var projectPath = FindRepositoryFile("src", "PrismWave.WinUI", "PrismWave.WinUI.csproj");
        var document = XDocument.Load(projectPath);
        var item = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Content"
            && element.Attribute("Include")?.Value == "..\\..\\native\\libmpv-winui\\libmpv-2.dll");

        Assert.Equal("Native\\libmpv-2.dll", item.Attribute("Link")?.Value);
        Assert.Equal("Native\\libmpv-2.dll", item.Attribute("PackagePath")?.Value);
        Assert.Equal("Always", item.Attribute("CopyToOutputDirectory")?.Value);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }

    private static string WriteEac3Fixture()
    {
        var mediaPath = Path.Combine(Path.GetTempPath(), $"prismwave-eac3-{Guid.NewGuid():N}.m4a");
        using var compressed = new MemoryStream(Convert.FromBase64String(SilentEac3M4aGzipBase64));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var output = File.Create(mediaPath);
        gzip.CopyTo(output);
        return mediaPath;
    }

    private sealed class LibMpvProbe : IDisposable
    {
        private const int EventEndFile = 7;
        private const int EventPlaybackRestart = 21;
        private readonly nint _library;
        private readonly MpvTerminateDestroy _terminateDestroy;
        private readonly MpvWaitEvent _waitEvent;
        private readonly MpvCommand _command;
        private nint _handle;

        public LibMpvProbe(string dllPath)
        {
            _library = NativeLibrary.Load(dllPath);
            var create = GetExport<MpvCreate>("mpv_create");
            var initialize = GetExport<MpvInitialize>("mpv_initialize");
            var setOption = GetExport<MpvSetOptionString>("mpv_set_option_string");
            _terminateDestroy = GetExport<MpvTerminateDestroy>("mpv_terminate_destroy");
            _waitEvent = GetExport<MpvWaitEvent>("mpv_wait_event");
            _command = GetExport<MpvCommand>("mpv_command");

            _handle = create();
            Assert.NotEqual(nint.Zero, _handle);
            Assert.True(setOption(_handle, "terminal", "no") >= 0);
            Assert.True(setOption(_handle, "sub-auto", "no") >= 0);
            Assert.True(setOption(_handle, "cover-art-auto", "no") >= 0);
            Assert.True(setOption(_handle, "audio-display", "no") >= 0);
            Assert.True(setOption(_handle, "video", "no") >= 0);
            Assert.True(setOption(_handle, "force-window", "no") >= 0);
            Assert.True(setOption(_handle, "ao", "null") >= 0);
            Assert.True(initialize(_handle) >= 0);
        }

        public (bool Started, string Diagnostic) PlayToNullAudio(string mediaPath, TimeSpan timeout)
        {
            var commandResult = Command("loadfile", mediaPath, "replace");
            if (commandResult < 0)
            {
                return (false, $"mpv loadfile command failed with {commandResult}.");
            }

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var eventPointer = _waitEvent(_handle, 0.2);
                if (eventPointer == nint.Zero)
                {
                    continue;
                }

                var evt = Marshal.PtrToStructure<MpvEvent>(eventPointer);
                if (evt.EventId == EventPlaybackRestart)
                {
                    return (true, "Playback restarted.");
                }

                if (evt.EventId == EventEndFile && evt.Data != nint.Zero)
                {
                    var ended = Marshal.PtrToStructure<MpvEventEndFile>(evt.Data);
                    if (ended.Reason == 2 && ended.Error == 0)
                    {
                        continue;
                    }

                    return (false, $"mpv ended before playback restart: reason={ended.Reason}, error={ended.Error}.");
                }
            }

            return (false, "mpv timed out before playback restart.");
        }

        public void Dispose()
        {
            if (_handle != nint.Zero)
            {
                _terminateDestroy(_handle);
                _handle = nint.Zero;
            }

            NativeLibrary.Free(_library);
        }

        private int Command(params string[] arguments)
        {
            var pointers = new nint[arguments.Length];
            var argv = Marshal.AllocHGlobal((arguments.Length + 1) * nint.Size);
            try
            {
                for (var index = 0; index < arguments.Length; index++)
                {
                    var bytes = Encoding.UTF8.GetBytes(arguments[index]);
                    pointers[index] = Marshal.AllocHGlobal(bytes.Length + 1);
                    Marshal.Copy(bytes, 0, pointers[index], bytes.Length);
                    Marshal.WriteByte(pointers[index], bytes.Length, 0);
                    Marshal.WriteIntPtr(argv, index * nint.Size, pointers[index]);
                }

                Marshal.WriteIntPtr(argv, arguments.Length * nint.Size, nint.Zero);
                return _command(_handle, argv);
            }
            finally
            {
                foreach (var pointer in pointers)
                {
                    if (pointer != nint.Zero)
                    {
                        Marshal.FreeHGlobal(pointer);
                    }
                }

                Marshal.FreeHGlobal(argv);
            }
        }

        private T GetExport<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint MpvCreate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int MpvInitialize(nint handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MpvTerminateDestroy(nint handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint MpvWaitEvent(nint handle, double timeout);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int MpvCommand(nint handle, nint arguments);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int MpvSetOptionString(nint handle, string name, string value);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MpvEvent
        {
            public readonly int EventId;
            public readonly int Error;
            public readonly ulong ReplyUserData;
            public readonly nint Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MpvEventEndFile
        {
            public readonly int Reason;
            public readonly int Error;
            public readonly long PlaylistEntryId;
            public readonly long PlaylistInsertId;
            public readonly int PlaylistInsertNumEntries;
        }
    }
}
