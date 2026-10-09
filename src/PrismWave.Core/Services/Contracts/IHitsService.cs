using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IHitsService
{
    HitsStateSnapshot Current { get; }
    event EventHandler? StateChanged;
    Task RefreshAsync(DateTimeOffset? nowUtc = null, CancellationToken cancellationToken = default);
    void UpdatePosition(DateTimeOffset nowUtc);
}
