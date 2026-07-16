using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class ThemeService : IThemeService
{
    public ThemeService(ISettingsService settingsService)
    {
        LowEffects = settingsService.Current.LowEffects;
    }

    public bool LowEffects { get; }
    public string ThemeName => "Fluent Dark";
}
