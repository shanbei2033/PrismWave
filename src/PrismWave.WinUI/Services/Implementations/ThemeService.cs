using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string ThemeName => AppearanceStyleIds.Normalize(_settingsService.Current.AppearanceStyle);
}
