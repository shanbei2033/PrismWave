using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace PrismWave_WinUI.Infrastructure;

internal static class WindowMinSizeGuard
{
    private const int GWLP_WNDPROC = -4;
    private const uint WM_GETMINMAXINFO = 0x0024;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private static readonly Dictionary<nint, GuardState> States = new();

    private sealed class GuardState
    {
        public required WndProcDelegate NewProc { get; init; }
        public nint OldProc { get; init; }
        public int MinWidth { get; init; }
        public int MinHeight { get; init; }
    }

    public static void Apply(Window window, int minWidth, int minHeight)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == 0 || States.ContainsKey(hwnd))
        {
            return;
        }

        WndProcDelegate newProc = (h, msg, wParam, lParam) => WindowProc(h, msg, wParam, lParam);
        var oldProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(newProc));
        States[hwnd] = new GuardState
        {
            NewProc = newProc,
            OldProc = oldProc,
            MinWidth = minWidth,
            MinHeight = minHeight,
        };

        window.Closed += (_, _) =>
        {
            if (States.Remove(hwnd, out var state))
            {
                SetWindowLongPtr(hwnd, GWLP_WNDPROC, state.OldProc);
            }
        };
    }

    private static nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (!States.TryGetValue(hWnd, out var state))
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        if (msg == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize.X = state.MinWidth;
            info.ptMinTrackSize.Y = state.MinHeight;
            Marshal.StructureToPtr(info, lParam, false);
            return 0;
        }

        return CallWindowProc(state.OldProc, hWnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLong32(hWnd, nIndex, dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern nint SetWindowLong32(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);
}
