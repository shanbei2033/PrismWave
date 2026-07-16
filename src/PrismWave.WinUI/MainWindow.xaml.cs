using Microsoft.UI.Xaml;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Views.Shell;

namespace PrismWave_WinUI;

public sealed partial class MainWindow : Window
{
    private bool _isImmersiveTitleBar;

    public MainWindow(WindowLaunchSize launchSize)
    {
        StartupLog.Write("MainWindow constructor");
        InitializeComponent();
        Closed += (_, _) => StartupLog.Write("MainWindow closed");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Title = "PrismWave";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(60, 48, launchSize.Width, launchSize.Height));
        StartupLog.Write($"Window launch size: {launchSize.Width}x{launchSize.Height}");

        RootFrame.Navigate(typeof(ShellPage));
        StartupLog.Write("ShellPage navigation requested");
    }

    internal void SetImmersiveTitleBar(bool isImmersive)
    {
        if (_isImmersiveTitleBar == isImmersive)
        {
            return;
        }

        _isImmersiveTitleBar = isImmersive;
        TitleBarBackground.Visibility = isImmersive
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
