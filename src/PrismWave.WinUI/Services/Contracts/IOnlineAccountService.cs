using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface IOnlineAccountService
{
    event EventHandler<OnlineAccountSnapshot>? AccountChanged;

    Task<OnlineLoginChallenge> CreateChallengeAsync(string providerKey, CancellationToken cancellationToken);

    Task<OnlineAccountSnapshot> PollAsync(string providerKey, CancellationToken cancellationToken);

    OnlineAccountSnapshot GetSnapshot(string providerKey);

    Task<OnlineProviderSession?> GetSessionAsync(string providerKey, CancellationToken cancellationToken);

    Task<OnlineProviderSession?> HandleAuthenticationFailureAsync(
        string providerKey,
        CancellationToken cancellationToken);

    Task InvalidateSessionAsync(string providerKey, CancellationToken cancellationToken);

    Task SignOutAsync(string providerKey, CancellationToken cancellationToken);
}

public interface IProviderCredentialStore
{
    Task<ProviderCredential?> LoadAsync(string providerKey, CancellationToken cancellationToken);

    Task SaveAsync(ProviderCredential credential, CancellationToken cancellationToken);

    Task DeleteAsync(string providerKey, CancellationToken cancellationToken);
}
