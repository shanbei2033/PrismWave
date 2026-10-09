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
