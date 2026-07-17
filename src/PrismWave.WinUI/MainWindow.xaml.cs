using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Views.Shell;

namespace PrismWave_WinUI;

public sealed partial class MainWindow : Window
{
    private bool _isImmersiveTitleBar;
    private readonly ISettingsService _settingsService;
    private string? _activeAppearanceStyle;

    public MainWindow(WindowLaunchSize launchSize)
    {
        StartupLog.Write("MainWindow constructor");
        InitializeComponent();
        _settingsService = App.Services.SettingsService;
        _settingsService.SettingsChanged += SettingsService_SettingsChanged;
        Closed += MainWindow_Closed;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Title = "PrismWave";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(60, 48, launchSize.Width, launchSize.Height));
        ApplyAppearanceStyle(_settingsService.Current.AppearanceStyle);
        StartupLog.Write($"Window launch size: {launchSize.Width}x{launchSize.Height}");

        RootFrame.Navigate(typeof(ShellPage));
        StartupLog.Write("ShellPage navigation requested");
    }

    internal void SetImmersiveTitleBar(bool isImmersive, UIElement? dragRegion = null)
    {
        _isImmersiveTitleBar = isImmersive;
        TitleBarBackground.Visibility = isImmersive
            ? Visibility.Collapsed
            : Visibility.Visible;
        SetTitleBar(dragRegion ?? AppTitleBar);
    }

    private void SettingsService_SettingsChanged(object? sender, EventArgs e)
    {
        var appearanceStyle = _settingsService.Current.AppearanceStyle;
        if (App.DispatcherQueue.HasThreadAccess)
        {
            ApplyAppearanceStyle(appearanceStyle);
        }
        else
        {
            App.DispatcherQueue.TryEnqueue(() => ApplyAppearanceStyle(appearanceStyle));
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _settingsService.SettingsChanged -= SettingsService_SettingsChanged;
        StartupLog.Write("MainWindow closed");
    }

    private void ApplyAppearanceStyle(string? requestedStyle)
    {
        var style = AppearanceStyleIds.Normalize(requestedStyle);
        if (string.Equals(_activeAppearanceStyle, style, StringComparison.Ordinal))
        {
            return;
        }

        _activeAppearanceStyle = style;
        var useTransparentBackground = false;
        try
        {
            SystemBackdrop = style switch
            {
                AppearanceStyleIds.Acrylic => new DesktopAcrylicBackdrop(),
                AppearanceStyleIds.Mica => new MicaBackdrop(),
                _ => null
            };
            useTransparentBackground = SystemBackdrop is not null;
        }
        catch (Exception exception)
        {
            StartupLog.Write($"appearance.backdrop.fallback: requested={style}", exception);
            SystemBackdrop = null;
        }

        if (Application.Current.Resources["PrismBackgroundBrush"] is SolidColorBrush backgroundBrush)
        {
            backgroundBrush.Color = useTransparentBackground
                ? Microsoft.UI.Colors.Transparent
                : Windows.UI.Color.FromArgb(0xFF, 0x29, 0x2A, 0x2D);
        }

        StartupLog.Write($"appearance.changed: requested={style}, transparent={useTransparentBackground}");
    }

}
