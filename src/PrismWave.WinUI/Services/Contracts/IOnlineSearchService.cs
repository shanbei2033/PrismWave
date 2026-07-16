using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IOnlineSearchService
{
    Task<IReadOnlyList<SearchResultModel>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
