using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class UpdateService : IUpdateService
{
    private const string RepoOwner = "shanbei2033";
    private const string RepoName = "PrismWave";
    private const string ApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public string CurrentVersion => "1.0.5";

    public string? LatestVersion { get; private set; }
    public string? LatestDownloadUrl { get; private set; }
    public bool HasUpdate { get; private set; }

    public event Action<UpdateCheckResult>? UpdateAvailable;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.UserAgent.ParseAdd($"PrismWave/{CurrentVersion}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, CurrentVersion, null, null, null);
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var tagName = json.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return new UpdateCheckResult(false, CurrentVersion, null, null, null);
            }

            // Normalize: strip leading 'v' or 'V'
            var latestVersion = tagName.TrimStart('v', 'V');

            // Find win-x64-portable.zip asset
            string? downloadUrl = null;
            if (json.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp) &&
                        asset.TryGetProperty("browser_download_url", out var urlProp))
                    {
                        var name = nameProp.GetString();
                        if (name is not null && name.Contains("win-x64-portable", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = urlProp.GetString();
                            break;
                        }
                    }
                }
            }

            var releaseNotesUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/tag/{tagName}";
            var hasUpdate = IsNewerVersion(latestVersion, CurrentVersion);

            LatestVersion = latestVersion;
            LatestDownloadUrl = downloadUrl;
            HasUpdate = hasUpdate;

            var result = new UpdateCheckResult(hasUpdate, CurrentVersion, latestVersion, downloadUrl, releaseNotesUrl);
            if (hasUpdate)
            {
                UpdateAvailable?.Invoke(result);
            }

            return result;
        }
        catch
        {
            return new UpdateCheckResult(false, CurrentVersion, null, null, null);
        }
    }

    /// <summary>
    /// Compares two version strings (e.g. "1.0.4" vs "1.0.3") and returns true
    /// if <paramref name="candidate"/> is strictly newer than <paramref name="current"/>.
    /// </summary>
    private static bool IsNewerVersion(string candidate, string current)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(current))
        {
            return false;
        }

        var candidateParts = candidate.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var currentParts = current.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var maxParts = Math.Max(candidateParts.Length, currentParts.Length);

        for (var i = 0; i < maxParts; i++)
        {
            var candidateValue = i < candidateParts.Length && int.TryParse(candidateParts[i], out var c) ? c : 0;
            var currentValue = i < currentParts.Length && int.TryParse(currentParts[i], out var cv) ? cv : 0;

            if (candidateValue > currentValue)
            {
                return true;
            }

            if (candidateValue < currentValue)
            {
                return false;
            }
        }

        return false;
    }
}
