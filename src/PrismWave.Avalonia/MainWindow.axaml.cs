using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PrismWave_WinUI.Infrastructure;

namespace PrismWave.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow(string[] args)
    {
        InitializeComponent();
        var launchSize = WindowLaunchSize.ResolveLaunch(string.Join(" ", args));
        Width = launchSize.Width;
        Height = launchSize.Height;
        StartupLog.Write($"[Avalonia] window launch size: {launchSize.Width}x{launchSize.Height}");
        Opened += (_, _) => ClampToWorkArea();
    }

    /// <summary>高 DPI 小屏（如 1368x912 逻辑）下钳制窗口不超出工作区并重新居中。</summary>
    private void ClampToWorkArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var maxWidth = area.Width / scale;
        var maxHeight = area.Height / scale;
        var resized = false;
        if (Width > maxWidth)
        {
            Width = maxWidth;
            resized = true;
        }

        if (Height > maxHeight)
        {
            Height = maxHeight;
            resized = true;
        }

        if (resized)
        {
            var left = area.X + (area.Width - (int)(Width * scale)) / 2;
            var top = area.Y + (area.Height - (int)(Height * scale)) / 2;
            Position = new PixelPoint(left, top);
            StartupLog.Write($"[Avalonia] window clamped to {Width:0}x{Height:0} (screen {area.Width}x{area.Height} @{scale:0.##})");
        }
    }

    private void TitleBarDragRegion_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void TitleBarDragRegion_DoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
