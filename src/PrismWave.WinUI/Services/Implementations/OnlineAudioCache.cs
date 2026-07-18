using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class OnlineAudioCache : IOnlineAudioCache, IDisposable
{
    public const long DefaultMaximumBytes = 5L * 1024 * 1024 * 1024;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly object _gate = new();
    private readonly HashSet<string> _activeKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _ownsHttpClient;

    public OnlineAudioCache(ISettingsService settingsService, HttpClient? httpClient = null)
    {
        _settingsService = settingsService;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _settingsService.SettingsChanged += SettingsService_SettingsChanged;
        Refresh();
    }

    public OnlineAudioCacheStatus Status { get; private set; } = new(
        string.Empty,
        0,
        DefaultMaximumBytes,
        0,
        false);

    public event EventHandler? CacheChanged;

    public TrackModel? TryGetCachedTrack(TrackModel track)
    {
        if (!track.IsRemote)
        {
            return null;
        }

        var key = CreateTrackKey(track);
        var file = FindCacheFile(key);
        if (file is null || file.Length <= 0)
        {
            return null;
        }

        StartupLog.Write($"playback.cache.hit: key={key}, title=\"{track.Title}\", bytes={file.Length}");
        return track with
        {
            PlaybackUrl = new Uri(file.FullName).AbsoluteUri,
            PlaybackHeaders = null,
            OnlineCandidateKey = $"cache:{key}"
        };
    }

    public async Task CacheAsync(
        TrackModel track,
        OnlinePlaybackResolution resolution,
        CancellationToken cancellationToken = default)
    {
        if (!track.IsRemote
            || !Uri.TryCreate(resolution.PlaybackUrl, UriKind.Absolute, out var source)
            || (source.Scheme != Uri.UriSchemeHttp && source.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var key = CreateTrackKey(track);
        lock (_gate)
        {
            if (_activeKeys.Contains(key) || FindCacheFile(key) is not null || Status.IsAtCapacity)
            {
                return;
            }

            _activeKeys.Add(key);
        }

        var directory = ResolveDirectory();
        Directory.CreateDirectory(directory);
        var extension = GetSafeExtension(source.AbsolutePath);
        var finalPath = Path.Combine(directory, $"{key}{extension}");
        var temporaryPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            foreach (var header in resolution.PlaybackHeaders ?? new Dictionary<string, string>())
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            Refresh();
            var remaining = Math.Max(0, Status.MaximumBytes - Status.CurrentBytes);
            if (remaining == 0
                || response.Content.Headers.ContentLength is long length && length > remaining)
            {
                StartupLog.Write($"playback.cache.skipped-capacity: title=\"{track.Title}\"");
                return;
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[128 * 1024];
            long written = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                written += read;
                if (written > remaining)
                {
                    StartupLog.Write($"playback.cache.aborted-capacity: title=\"{track.Title}\"");
                    return;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
            output.Close();
            if (written > 0)
            {
                File.Move(temporaryPath, finalPath, overwrite: true);
                StartupLog.Write($"playback.cache.saved: key={key}, title=\"{track.Title}\", bytes={written}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException
            or IOException
            or UnauthorizedAccessException)
        {
            StartupLog.Write($"playback.cache.failed: title=\"{track.Title}\", error={exception.Message}");
        }
        finally
        {
            TryDelete(temporaryPath);
            lock (_gate)
            {
                _activeKeys.Remove(key);
            }

            Refresh();
        }
    }

    public void Invalidate(TrackModel cachedTrack)
    {
        if (!Uri.TryCreate(cachedTrack.PlaybackSource, UriKind.Absolute, out var source)
            || source.Scheme != Uri.UriSchemeFile
            || !IsWithinCacheDirectory(source.LocalPath))
        {
            return;
        }

        TryDelete(source.LocalPath);
        StartupLog.Write($"playback.cache.invalidated: title=\"{cachedTrack.Title}\"");
        Refresh();
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var directory = ResolveDirectory();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDelete(file);
            }
        }

        StartupLog.Write("playback.cache.cleared");
        Refresh();
        return Task.CompletedTask;
    }

    public void Refresh()
    {
        var directory = ResolveDirectory();
        long bytes = 0;
        var count = 0;
        try
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory)
                    .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
                {
                    var info = new FileInfo(file);
                    bytes += Math.Max(0, info.Length);
                    count++;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StartupLog.Write($"playback.cache.scan-failed: {exception.Message}");
        }

        var maximum = Math.Max(0, _settingsService.Current.OnlineCacheMaximumBytes);
        Status = new OnlineAudioCacheStatus(
            directory,
            bytes,
            maximum,
            count,
            maximum == 0 || bytes >= maximum);
        CacheChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _settingsService.SettingsChanged -= SettingsService_SettingsChanged;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private void SettingsService_SettingsChanged(object? sender, EventArgs e) => Refresh();

    private FileInfo? FindCacheFile(string key)
    {
        var directory = ResolveDirectory();
        try
        {
            return !Directory.Exists(directory)
                ? null
                : Directory.EnumerateFiles(directory, $"{key}.*")
                    .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    .Select(path => new FileInfo(path))
                    .FirstOrDefault(file => file.Length > 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StartupLog.Write($"playback.cache.lookup-failed: {exception.Message}");
            return null;
        }
    }

    private string ResolveDirectory()
    {
        var configured = _settingsService.Current.OnlineCacheDirectory?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrismWave",
                "WinUI",
                "audio_cache")
            : Path.GetFullPath(configured);
    }

    private bool IsWithinCacheDirectory(string path)
    {
        var directory = Path.GetFullPath(ResolveDirectory())
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path);
        return candidate.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTrackKey(TrackModel track)
    {
        var identity = string.Join("|", new[]
        {
            Normalize(track.Title),
            Normalize(track.Artist),
            Normalize(track.Album),
            Math.Round(track.DurationSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
    }

    private static string Normalize(string value) => string.Join(
        ' ',
        value.Trim().ToLowerInvariant().Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

    private static string GetSafeExtension(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".mp3" or ".m4a" or ".aac" or ".flac" or ".ogg" or ".wav" or ".opus"
            ? extension
            : ".audio";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
