using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface ISettingsService
{
    SettingsSnapshot Current { get; }
    event EventHandler? SettingsChanged;
    Task SaveAsync(SettingsSnapshot snapshot);
}
