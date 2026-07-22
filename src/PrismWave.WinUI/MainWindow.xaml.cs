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
        WindowMinSizeGuard.Apply(this, minWidth: launchSize.Width, minHeight: launchSize.Height);
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
        ApplyTitleBarColors();
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
        WindowRoot.RequestedTheme = style == AppearanceStyleIds.Mica
            ? ElementTheme.Light
            : ElementTheme.Dark;
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

        ApplyAppearancePalette(style, useTransparentBackground);
        ApplyTitleBarColors();

        StartupLog.Write($"appearance.changed: requested={style}, transparent={useTransparentBackground}");
    }

    private void ApplyAppearancePalette(string style, bool useTransparentBackground)
    {
        var palette = style switch
        {
            AppearanceStyleIds.Mica => new AppearancePalette(
                Background: Color(0x00, 0xF3, 0xF3, 0xF3),
                Surface: Color(0xB8, 0xF3, 0xF3, 0xF3),
                SurfaceElevated: Color(0xD9, 0xFF, 0xFF, 0xFF),
                SurfaceStrong: Color(0xEA, 0xFF, 0xFF, 0xFF),
                TextPrimary: Color(0xFF, 0x1B, 0x1B, 0x1F),
                TextSecondary: Color(0xFF, 0x5D, 0x63, 0x6E),
                TextMuted: Color(0xFF, 0x78, 0x7E, 0x88),
                CardBorder: Color(0x24, 0x00, 0x00, 0x00),
                CardHover: Color(0x0F, 0x00, 0x00, 0x00),
                Glass: Color(0xC4, 0xFF, 0xFF, 0xFF),
                GlassSoft: Color(0x96, 0xFF, 0xFF, 0xFF),
                Selection: Color(0x18, 0x00, 0x00, 0x00),
                Control: Color(0x0D, 0x00, 0x00, 0x00)),
            AppearanceStyleIds.Acrylic => new AppearancePalette(
                Background: Color(0x00, 0x29, 0x2A, 0x2D),
                Surface: Color(0xB5, 0x2D, 0x2E, 0x33),
                SurfaceElevated: Color(0xD1, 0x3A, 0x3B, 0x40),
                SurfaceStrong: Color(0xE2, 0x4A, 0x4B, 0x51),
                TextPrimary: Color(0xFF, 0xF6, 0xF6, 0xF7),
                TextSecondary: Color(0xFF, 0xB9, 0xBE, 0xC8),
                TextMuted: Color(0xFF, 0x7F, 0x87, 0x94),
                CardBorder: Color(0x2B, 0xFF, 0xFF, 0xFF),
                CardHover: Color(0x18, 0xFF, 0xFF, 0xFF),
                Glass: Color(0xA8, 0x38, 0x39, 0x3D),
                GlassSoft: Color(0x72, 0x41, 0x42, 0x46),
                Selection: Color(0x24, 0xFF, 0xFF, 0xFF),
                Control: Color(0x12, 0xFF, 0xFF, 0xFF)),
            _ => new AppearancePalette(
                Background: Color(0xFF, 0x29, 0x2A, 0x2D),
                Surface: Color(0xFF, 0x30, 0x31, 0x34),
                SurfaceElevated: Color(0xFF, 0x38, 0x39, 0x3D),
                SurfaceStrong: Color(0xFF, 0x45, 0x46, 0x4A),
                TextPrimary: Color(0xFF, 0xF6, 0xF6, 0xF7),
                TextSecondary: Color(0xFF, 0xB9, 0xBE, 0xC8),
                TextMuted: Color(0xFF, 0x7F, 0x87, 0x94),
                CardBorder: Color(0x2B, 0xFF, 0xFF, 0xFF),
                CardHover: Color(0x18, 0xFF, 0xFF, 0xFF),
                Glass: Color(0xF2, 0x38, 0x39, 0x3D),
                GlassSoft: Color(0xE8, 0x41, 0x42, 0x46),
                Selection: Color(0x24, 0xFF, 0xFF, 0xFF),
                Control: Color(0x12, 0xFF, 0xFF, 0xFF))
        };

        SetBrushColor("PrismBackgroundBrush", useTransparentBackground
            ? Microsoft.UI.Colors.Transparent
            : palette.Background);
        SetBrushColor("PrismSurfaceBrush", palette.Surface);
        SetBrushColor("PrismSurfaceElevatedBrush", palette.SurfaceElevated);
        SetBrushColor("PrismSurfaceStrongBrush", palette.SurfaceStrong);
        SetBrushColor("PrismTextPrimaryBrush", palette.TextPrimary);
        SetBrushColor("PrismTextSecondaryBrush", palette.TextSecondary);
        SetBrushColor("PrismTextMutedBrush", palette.TextMuted);
        SetBrushColor("PrismCardBorderBrush", palette.CardBorder);
        SetBrushColor("PrismCardHoverBrush", palette.CardHover);
        SetBrushColor("PrismGlassBrush", palette.Glass);
        SetBrushColor("PrismGlassSoftBrush", palette.GlassSoft);
        SetBrushColor("PrismSelectionBrush", palette.Selection);
        SetBrushColor("PrismControlBrush", palette.Control);
    }

    private void ApplyTitleBarColors()
    {
        var useLightForeground = _isImmersiveTitleBar
            || _activeAppearanceStyle != AppearanceStyleIds.Mica;
        var foreground = useLightForeground
            ? Microsoft.UI.Colors.White
            : Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20);
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = foreground;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    private static Windows.UI.Color Color(byte alpha, byte red, byte green, byte blue) =>
        Windows.UI.Color.FromArgb(alpha, red, green, blue);

    private static void SetBrushColor(string resourceKey, Windows.UI.Color color)
    {
        if (Application.Current.Resources[resourceKey] is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    private sealed record AppearancePalette(
        Windows.UI.Color Background,
        Windows.UI.Color Surface,
        Windows.UI.Color SurfaceElevated,
        Windows.UI.Color SurfaceStrong,
        Windows.UI.Color TextPrimary,
        Windows.UI.Color TextSecondary,
        Windows.UI.Color TextMuted,
        Windows.UI.Color CardBorder,
        Windows.UI.Color CardHover,
        Windows.UI.Color Glass,
        Windows.UI.Color GlassSoft,
        Windows.UI.Color Selection,
        Windows.UI.Color Control);

}
