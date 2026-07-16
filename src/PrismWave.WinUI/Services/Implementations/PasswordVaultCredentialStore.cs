using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using System.Runtime.InteropServices;
using Windows.Security.Credentials;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class PasswordVaultCredentialStore : IProviderCredentialStore
{
    private const string ResourcePrefix = "PrismWave.OnlineAccount";
    private const string SessionUserName = "session";
    private const int CredentialNotFoundHResult = unchecked((int)0x80070490);

    private readonly PasswordVault _vault = new();

    public Task<ProviderCredential?> LoadAsync(string providerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedProvider = NormalizeProvider(providerKey);
        try
        {
            var credential = FindCredential(BuildResource(normalizedProvider));
            if (credential is null)
            {
                return Task.FromResult<ProviderCredential?>(null);
            }

            credential.RetrievePassword();
            return Task.FromResult<ProviderCredential?>(
                new ProviderCredential(normalizedProvider, credential.Password));
        }
        catch
        {
            throw new InvalidOperationException("Credential storage operation failed.");
        }
    }

    public Task SaveAsync(ProviderCredential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedProvider = NormalizeProvider(credential.ProviderKey);
        try
        {
            RemoveResourceCredentials(BuildResource(normalizedProvider));
            _vault.Add(new PasswordCredential(
                BuildResource(normalizedProvider),
                SessionUserName,
                credential.Secret));
            return Task.CompletedTask;
        }
        catch
        {
            throw new InvalidOperationException("Credential storage operation failed.");
        }
    }

    public Task DeleteAsync(string providerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedProvider = NormalizeProvider(providerKey);
        try
        {
            RemoveResourceCredentials(BuildResource(normalizedProvider));
            return Task.CompletedTask;
        }
        catch
        {
            throw new InvalidOperationException("Credential storage operation failed.");
        }
    }

    private PasswordCredential? FindCredential(string resource)
    {
        try
        {
            return _vault.FindAllByResource(resource)
                .FirstOrDefault(static credential => credential.UserName == SessionUserName);
        }
        catch (COMException error) when (error.HResult == CredentialNotFoundHResult)
        {
            return null;
        }
        catch
        {
            throw new InvalidOperationException("Credential storage operation failed.");
        }
    }

    private void RemoveResourceCredentials(string resource)
    {
        IReadOnlyList<PasswordCredential> credentials;
        try
        {
            credentials = _vault.FindAllByResource(resource);
        }
        catch (COMException error) when (error.HResult == CredentialNotFoundHResult)
        {
            return;
        }
        catch
        {
            throw new InvalidOperationException("Credential storage operation failed.");
        }

        foreach (var credential in credentials)
        {
            _vault.Remove(credential);
        }
    }

    private static string BuildResource(string providerKey) => $"{ResourcePrefix}.{providerKey}";

    private static string NormalizeProvider(string providerKey)
    {
        var normalized = providerKey?.Trim().ToLowerInvariant();
        return normalized is "netease" or "qq"
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(providerKey), "Only netease and qq accounts are supported.");
    }
}
