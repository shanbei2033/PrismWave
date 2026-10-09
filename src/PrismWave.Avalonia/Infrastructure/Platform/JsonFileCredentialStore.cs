using System.Text.Json;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave.Avalonia.Infrastructure.Platform;

/// <summary>
/// 跨平台凭据存储（JSON 文件，用户本地目录）。
/// Windows 版 WinUI 使用 PasswordVault；Avalonia 版后续如需系统级保护再扩展。
/// </summary>
public sealed class JsonFileCredentialStore : IProviderCredentialStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrismWave",
        "Avalonia",
        "credentials.json");
    private readonly object _gate = new();

    public Task<ProviderCredential?> LoadAsync(string providerKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var credentials = ReadAll();
            return Task.FromResult(credentials.TryGetValue(providerKey, out var secret)
                ? new ProviderCredential(providerKey, secret)
                : null);
        }
    }

    public Task SaveAsync(ProviderCredential credential, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var credentials = ReadAll();
            credentials[credential.ProviderKey] = credential.Secret;
            WriteAll(credentials);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string providerKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var credentials = ReadAll();
            if (credentials.Remove(providerKey))
            {
                WriteAll(credentials);
            }
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, string> ReadAll()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StorePath))
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void WriteAll(Dictionary<string, string> credentials)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(credentials));
    }
}
