using System.Security.Cryptography;
using System.Text;
using PrismWave_WinUI.Infrastructure.Library;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class LocalMusicScanner : ILocalMusicScanner
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".aac", ".m4a", ".mp4", ".wav", ".flac", ".ogg", ".ape", ".wma", ".dsf", ".dff"
    };

    private static readonly string CoverCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrismWave",
        "WinUI",
        "covers");

    public Task<LibraryScanResult> ScanAsync(
        IReadOnlyList<string> folders,
        IReadOnlyDictionary<string, string> customCoverPaths,
        IProgress<LibraryScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => Scan(folders, customCoverPaths, progress, cancellationToken),
            cancellationToken);
    }

    private static LibraryScanResult Scan(
        IReadOnlyList<string> folders,
        IReadOnlyDictionary<string, string> customCoverPaths,
        IProgress<LibraryScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var statuses = new List<LibraryFolderStatus>();
        var warnings = new List<string>();
        var validRoots = 0;

        progress?.Report(new LibraryScanProgress(0, LibraryScanPhase.Enumerating, 0, 0, null));
        foreach (var rawFolder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displayPath = LibraryFolderPath.Normalize(rawFolder, requireExisting: false) ?? rawFolder.Trim();
            var root = LibraryFolderPath.Normalize(rawFolder);
            if (root is null)
            {
                var error = $"Music folder is unavailable: {displayPath}";
                statuses.Add(new LibraryFolderStatus(displayPath, false, error));
                warnings.Add(error);
                continue;
            }

            validRoots++;
            statuses.Add(new LibraryFolderStatus(root, true, null));
            foreach (var file in EnumerateAudioFiles(root, warnings, cancellationToken))
            {
                if (!seenFiles.Add(file))
                {
                    continue;
                }

                files.Add(file);
                if (files.Count == 1 || files.Count % 25 == 0)
                {
                    progress?.Report(new LibraryScanProgress(
                        0,
                        LibraryScanPhase.Enumerating,
                        files.Count,
                        0,
                        file));
                }
            }
        }

        var tracks = new List<TrackModel>(files.Count);
        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            customCoverPaths.TryGetValue(file, out var customCover);
            var track = FromFile(file, customCover);
            var identity = TrackCoverIdentity.CreateKey(track.Title, track.Artist);
            if (identity.Length > 0
                && customCoverPaths.TryGetValue(identity, out var identityCover)
                && System.IO.File.Exists(identityCover))
            {
                track = track with { CoverPath = Path.GetFullPath(identityCover) };
            }

            tracks.Add(track);
            if (index == 0 || (index + 1) % 25 == 0 || index + 1 == files.Count)
            {
                progress?.Report(new LibraryScanProgress(
                    0,
                    LibraryScanPhase.ReadingMetadata,
                    files.Count,
                    index + 1,
                    file));
            }
        }

        var fatalError = folders.Count > 0 && validRoots == 0
            ? warnings.FirstOrDefault() ?? "No configured music folder is available."
            : null;
        progress?.Report(new LibraryScanProgress(
            0,
            fatalError is null ? LibraryScanPhase.Completed : LibraryScanPhase.Failed,
            files.Count,
            tracks.Count,
            null));
        return new LibraryScanResult(tracks, statuses, warnings, fatalError);
    }

    private static IEnumerable<string> EnumerateAudioFiles(
        string root,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (!visited.Add(directory))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Cannot read {directory}: {exception.Message}");
                files = [];
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AudioExtensions.Contains(Path.GetExtension(file)))
                {
                    yield return Path.GetFullPath(file);
                }
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Cannot enumerate {directory}: {exception.Message}");
                directories = [];
            }

            foreach (var child in directories)
            {
                try
                {
                    if ((System.IO.File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Cannot inspect {child}: {exception.Message}");
                }
            }
        }
    }

    private static TrackModel FromFile(string file, string? customCover)
    {
        var album = Path.GetFileName(Path.GetDirectoryName(file)) ?? "Unknown Album";
        var title = Path.GetFileNameWithoutExtension(file);
        var artist = "Unknown Artist";
        var duration = "--:--";
        var durationSeconds = 0d;
        var bitrate = 0;
        var sampleRate = 0;
        var channels = 0;
        var codec = Path.GetExtension(file).TrimStart('.').ToUpperInvariant();
        string? coverPath = null;
        var waveInfo = Path.GetExtension(file).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? ReadWaveInfo(file)
            : default;

        try
        {
            using var taggedFile = TagLib.File.Create(file);
            title = FirstNonEmpty(taggedFile.Tag.Title, waveInfo.Title, title);
            artist = FirstNonEmpty(taggedFile.Tag.FirstPerformer, waveInfo.Artist, artist);
            album = FirstNonEmpty(taggedFile.Tag.Album, waveInfo.Album, album);
            durationSeconds = taggedFile.Properties.Duration.TotalSeconds;
            duration = FormatDuration(taggedFile.Properties.Duration);
            bitrate = taggedFile.Properties.AudioBitrate;
            sampleRate = taggedFile.Properties.AudioSampleRate;
            channels = taggedFile.Properties.AudioChannels;
            codec = taggedFile.Properties.Codecs.FirstOrDefault()?.Description ?? codec;
            coverPath = ExtractCover(file, taggedFile.Tag.Pictures);
        }
        catch
        {
            title = FirstNonEmpty(waveInfo.Title, title);
            artist = FirstNonEmpty(waveInfo.Artist, artist);
            album = FirstNonEmpty(waveInfo.Album, album);
        }

        if (!string.IsNullOrWhiteSpace(customCover) && System.IO.File.Exists(customCover))
        {
            coverPath = Path.GetFullPath(customCover);
        }
        else
        {
            coverPath ??= FindSidecarCover(file);
        }

        long fileSize = 0;
        try
        {
            fileSize = new FileInfo(file).Length;
        }
        catch
        {
        }

        return new TrackModel(
            file,
            file,
            title,
            artist,
            album,
            duration,
            coverPath,
            DurationSeconds: durationSeconds,
            BitrateKbps: bitrate,
            SampleRateHz: sampleRate,
            Channels: channels,
            FileSizeBytes: fileSize,
            Codec: codec);
    }

    private static WaveInfo ReadWaveInfo(string file)
    {
        try
        {
            using var stream = System.IO.File.OpenRead(file);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (ReadFourCc(reader) != "RIFF")
            {
                return default;
            }

            _ = reader.ReadUInt32();
            if (ReadFourCc(reader) != "WAVE")
            {
                return default;
            }

            string? title = null;
            string? artist = null;
            string? album = null;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadFourCc(reader);
                var chunkSize = reader.ReadUInt32();
                var chunkEnd = Math.Min(stream.Length, stream.Position + chunkSize);
                if (chunkId == "LIST" && chunkSize >= 4 && ReadFourCc(reader) == "INFO")
                {
                    while (stream.Position + 8 <= chunkEnd)
                    {
                        var infoId = ReadFourCc(reader);
                        var infoSize = reader.ReadUInt32();
                        var available = (int)Math.Min(infoSize, chunkEnd - stream.Position);
                        var value = Encoding.Default.GetString(reader.ReadBytes(available)).TrimEnd('\0', ' ', '\r', '\n');
                        if ((infoSize & 1) != 0 && stream.Position < chunkEnd)
                        {
                            stream.Position++;
                        }

                        switch (infoId)
                        {
                            case "INAM": title = value; break;
                            case "IART": artist = value; break;
                            case "IPRD": album = value; break;
                        }
                    }
                }

                stream.Position = Math.Min(stream.Length, chunkEnd + (chunkSize & 1));
            }

            return new WaveInfo(title, artist, album);
        }
        catch
        {
            return default;
        }
    }

    private static string ReadFourCc(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "--:--";
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string? ExtractCover(string file, TagLib.IPicture[] pictures)
    {
        var picture = pictures.FirstOrDefault(item => item.Data.Count > 0);
        if (picture is null)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(CoverCacheDirectory);
            var extension = MimeToExtension(picture.MimeType);
            var coverPath = Path.Combine(CoverCacheDirectory, $"{HashPath(file)}{extension}");
            if (!System.IO.File.Exists(coverPath))
            {
                System.IO.File.WriteAllBytes(coverPath, picture.Data.Data);
            }

            return coverPath;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindSidecarCover(string file)
    {
        var directory = Path.GetDirectoryName(file);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        string[] names = ["cover", "folder", "front", "album", Path.GetFileNameWithoutExtension(file)];
        string[] extensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];
        foreach (var name in names)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, $"{name}{extension}");
                if (System.IO.File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static string MimeToExtension(string? mime) => mime?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".jpg"
    };

    private static string HashPath(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private readonly record struct WaveInfo(string? Title, string? Artist, string? Album);
}
