using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class LibraryService : ILibraryService
{
    private const char AlbumKeySeparator = '\u001f';
    private readonly ISettingsService _settingsService;
    private readonly ICoverService _coverService;
    private readonly List<TrackModel> _tracks = new();
    private readonly List<string> _folders = new();
    private readonly List<AlbumModel> _albums = new();
    private readonly List<ArtistModel> _artists = new();
    private readonly List<TrackModel> _favorites = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".aac", ".m4a", ".mp4", ".wav", ".flac", ".ogg", ".ape", ".wma", ".dsf", ".dff"
    };

    private static readonly string CoverCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrismWave",
        "WinUI",
        "covers");

    public LibraryService(ISettingsService settingsService, ICoverService coverService)
    {
        _settingsService = settingsService;
        _coverService = coverService;
        _coverService.CoverChanged += CoverService_CoverChanged;
        ReplaceFolders(settingsService.Current.LibraryFolders);
        if (_folders.Count > 0)
        {
            _ = RescanAsync();
        }
    }

    public IReadOnlyList<TrackModel> Tracks => _tracks;
    public IReadOnlyList<string> Folders => _folders;
    public IReadOnlyList<AlbumModel> Albums => _albums;
    public IReadOnlyList<ArtistModel> Artists => _artists;
    public IReadOnlyList<TrackModel> Favorites => _favorites;
    public bool IsScanning { get; private set; }
    public string? Error { get; private set; }
    public event EventHandler? LibraryChanged;

    public async Task AddFolderAsync(string folder)
    {
        var normalized = NormalizeDirectory(folder);
        if (normalized is null)
        {
            Error = "The selected music folder is unavailable.";
            Notify();
            return;
        }

        var folders = _settingsService.Current.LibraryFolders
            .Select(path => NormalizeDirectory(path) ?? path)
            .ToList();
        if (folders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        folders.Add(normalized);
        await SaveSettingsAsync(_settingsService.Current with { LibraryFolders = folders });
        await RescanAsync();
    }

    public async Task RemoveFolderAsync(string folder)
    {
        var folders = _settingsService.Current.LibraryFolders
            .Where(item => !string.Equals(item, folder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await SaveSettingsAsync(_settingsService.Current with { LibraryFolders = folders });
        await RescanAsync();
    }

    public async Task RescanAsync()
    {
        await _scanGate.WaitAsync();
        try
        {
            IsScanning = true;
            Error = null;
            ReplaceFolders(_settingsService.Current.LibraryFolders);
            Notify();

            var settings = _settingsService.Current;
            StartupLog.Write($"library.scan.start: folders={_folders.Count}");
            var customCovers = settings.CustomCoverPaths
                ?? DecodeStringMap(settings.Migration.Values, "library.customCoverPaths");
            var scanResult = await Task.Run(() => LoadTracks(_folders, customCovers));
            var ordered = ApplyStoredTrackOrder(
                scanResult.Tracks,
                settings.TrackOrderPaths,
                settings.HiddenTrackPaths);

            var activePaths = ordered.Select(track => track.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var favoritePaths = settings.FavoritePaths
                .Where(activePaths.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var favoriteSet = favoritePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var favoriteOrder = SanitizeOrder(settings.FavoriteOrderPaths, favoritePaths, activePaths);
            var tracks = ordered
                .Select(track => track with { IsFavorite = favoriteSet.Contains(track.Path) })
                .ToList();

            ReplaceTracks(tracks, favoriteOrder);
            Error = scanResult.FatalError;
            StartupLog.Write($"library.scan.complete: tracks={tracks.Count}, albums={_albums.Count}, artists={_artists.Count}");

            var normalizedOrder = tracks.Select(track => track.Path).ToList();
            if (!SequenceEqual(settings.TrackOrderPaths, normalizedOrder)
                || !SequenceEqual(settings.FavoritePaths, favoritePaths)
                || !SequenceEqual(settings.FavoriteOrderPaths, favoriteOrder))
            {
                await SaveSettingsAsync(settings with
                {
                    TrackOrderPaths = normalizedOrder,
                    FavoritePaths = favoritePaths,
                    FavoriteOrderPaths = favoriteOrder
                });
            }
        }
        catch (Exception exception)
        {
            Error = $"Library scan failed: {exception.Message}";
            StartupLog.Write("library.scan.failed", exception);
        }
        finally
        {
            IsScanning = false;
            _scanGate.Release();
            Notify();
        }
    }

    public async Task ToggleFavoriteAsync(TrackModel track)
    {
        var favorites = _settingsService.Current.FavoritePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var favoriteOrder = _settingsService.Current.FavoriteOrderPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existing = favorites.FirstOrDefault(path => PathsEqual(path, track.Path));
        if (existing is null)
        {
            favorites.Add(track.Path);
            if (!favoriteOrder.Contains(track.Path, StringComparer.OrdinalIgnoreCase))
            {
                favoriteOrder.Add(track.Path);
            }
        }
        else
        {
            favorites.Remove(existing);
            favoriteOrder.RemoveAll(path => PathsEqual(path, track.Path));
        }

        await SaveSettingsAsync(_settingsService.Current with
        {
            FavoritePaths = favorites,
            FavoriteOrderPaths = favoriteOrder
        });
        ApplyFavoriteFlags(favorites, favoriteOrder);
        StartupLog.Write($"library.favorite: path={track.Path}, favorite={favorites.Contains(track.Path, StringComparer.OrdinalIgnoreCase)}");
        Notify();
    }

    public async Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks)
    {
        var reordered = MergeVisibleOrder(
            _tracks.Select(track => track.Path).ToList(),
            visibleTracks.Select(track => track.Path).ToList());
        if (reordered.Count == 0)
        {
            return;
        }

        await SaveSettingsAsync(_settingsService.Current with { TrackOrderPaths = reordered });
        StartupLog.Write($"library.reorder: visible={visibleTracks.Count}, total={_tracks.Count}");
        ReorderTracksInMemory(reordered);
        Notify();
    }

    public async Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks)
    {
        var reordered = MergeVisibleOrder(
            _favorites.Select(track => track.Path).ToList(),
            visibleTracks.Select(track => track.Path).ToList());
        if (reordered.Count == 0)
        {
            return;
        }

        await SaveSettingsAsync(_settingsService.Current with { FavoriteOrderPaths = reordered });
        StartupLog.Write($"library.favoriteReorder: count={visibleTracks.Count}");
        RebuildDerivedCollections(reordered);
        Notify();
    }

    public async Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile)
    {
        if (track.IsRemote || string.IsNullOrWhiteSpace(track.Path))
        {
            return;
        }

        if (deleteSourceFile)
        {
            await Task.Run(() => DeleteTrackSourceFiles(track.Path));
        }

        var settings = _settingsService.Current;
        var hidden = settings.HiddenTrackPaths
            .Where(path => !PathsEqual(path, track.Path))
            .ToList();
        if (!deleteSourceFile && !hidden.Contains(track.Path, StringComparer.OrdinalIgnoreCase))
        {
            hidden.Add(track.Path);
        }

        var favorites = settings.FavoritePaths.Where(path => !PathsEqual(path, track.Path)).ToList();
        var favoriteOrder = settings.FavoriteOrderPaths.Where(path => !PathsEqual(path, track.Path)).ToList();
        var trackOrder = settings.TrackOrderPaths.Where(path => !PathsEqual(path, track.Path)).ToList();
        await SaveSettingsAsync(settings with
        {
            HiddenTrackPaths = hidden,
            FavoritePaths = favorites,
            FavoriteOrderPaths = favoriteOrder,
            TrackOrderPaths = trackOrder
        });

        _tracks.RemoveAll(item => PathsEqual(item.Path, track.Path));
        StartupLog.Write($"library.remove: path={track.Path}, deleteSource={deleteSourceFile}");
        RebuildDerivedCollections(favoriteOrder);
        Notify();
    }

    public IReadOnlyList<TrackModel> GetAlbumTracks(string albumId)
    {
        return _tracks.Where(track => AlbumIdOf(track) == albumId).ToList();
    }

    public IReadOnlyList<TrackModel> GetArtistTracks(string artistName)
    {
        return _tracks
            .Where(track => string.Equals(NormalizeArtist(track.Artist), artistName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void CoverService_CoverChanged(object? sender, CoverChangedEventArgs e)
    {
        var changed = false;
        for (var index = 0; index < _tracks.Count; index++)
        {
            var track = _tracks[index];
            if (!string.Equals(track.Id, e.TrackId, StringComparison.OrdinalIgnoreCase)
                && !PathsEqual(track.Path, e.TrackPath)
                && !TrackCoverIdentity.Matches(track.Title, track.Artist, e.Title, e.Artist))
            {
                continue;
            }

            _tracks[index] = track with { CoverPath = e.CoverPath };
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        RebuildDerivedCollections(_settingsService.Current.FavoriteOrderPaths);
        Notify();
    }

    private static ScanResult LoadTracks(
        IReadOnlyList<string> folders,
        IReadOnlyDictionary<string, string> customCovers)
    {
        var tracks = new List<TrackModel>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validRoots = 0;
        string? firstError = null;

        foreach (var folder in folders)
        {
            var root = NormalizeDirectory(folder);
            if (root is null)
            {
                firstError ??= $"Music folder is unavailable: {folder}";
                continue;
            }

            validRoots++;
            foreach (var file in EnumerateAudioFiles(root, error => firstError ??= error))
            {
                if (!seenFiles.Add(file))
                {
                    continue;
                }

                customCovers.TryGetValue(file, out var legacyCover);
                var track = FromFile(file, legacyCover);
                var identityKey = TrackCoverIdentity.CreateKey(track.Title, track.Artist);
                if (identityKey.Length > 0
                    && customCovers.TryGetValue(identityKey, out var identityCover)
                    && File.Exists(identityCover))
                {
                    track = track with { CoverPath = identityCover };
                }

                tracks.Add(track);
            }
        }

        var fatalError = validRoots == 0 && folders.Count > 0 ? firstError : null;
        return new ScanResult(tracks, fatalError);
    }

    private static IEnumerable<string> EnumerateAudioFiles(string root, Action<string> reportError)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(root);

        while (pending.Count > 0)
        {
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
                reportError($"Cannot read {directory}: {exception.Message}");
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
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
                reportError($"Cannot enumerate {directory}: {exception.Message}");
                directories = Array.Empty<string>();
            }

            foreach (var child in directories)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    reportError($"Cannot inspect {child}: {exception.Message}");
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
        }

        if (!string.IsNullOrWhiteSpace(customCover) && File.Exists(customCover))
        {
            coverPath = customCover;
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
            using var stream = File.OpenRead(file);
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

    private static string ReadFourCc(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

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
            if (!File.Exists(coverPath))
            {
                File.WriteAllBytes(coverPath, picture.Data.Data);
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

        var names = new[] { "cover", "folder", "front", "album", Path.GetFileNameWithoutExtension(file) };
        var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };
        foreach (var name in names)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, $"{name}{extension}");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<TrackModel> ApplyStoredTrackOrder(
        IReadOnlyList<TrackModel> tracks,
        IReadOnlyList<string> savedOrder,
        IReadOnlyList<string> hiddenPaths)
    {
        var hidden = hiddenPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byPath = tracks
            .Where(track => !hidden.Contains(track.Path))
            .GroupBy(track => track.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var ordered = new List<TrackModel>(byPath.Count);

        foreach (var path in savedOrder)
        {
            if (byPath.Remove(path, out var track))
            {
                ordered.Add(track);
            }
        }

        ordered.AddRange(byPath.Values.OrderBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase));
        return ordered;
    }

    private static List<string> SanitizeOrder(
        IReadOnlyList<string> preferredOrder,
        IReadOnlyList<string> members,
        IReadOnlySet<string> activePaths)
    {
        var memberSet = members.Where(activePaths.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var path in preferredOrder.Concat(members))
        {
            if (memberSet.Remove(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    private static List<string> MergeVisibleOrder(IReadOnlyList<string> fullOrder, IReadOnlyList<string> visibleOrder)
    {
        var visible = visibleOrder.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var visibleSet = visible.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (visible.Count <= 1 || !visibleSet.IsSubsetOf(fullOrder))
        {
            return fullOrder.ToList();
        }

        var cursor = 0;
        return fullOrder
            .Select(path => visibleSet.Contains(path) ? visible[cursor++] : path)
            .ToList();
    }

    private void ReplaceTracks(IReadOnlyList<TrackModel> tracks, IReadOnlyList<string> favoriteOrder)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);
        RebuildDerivedCollections(favoriteOrder);
    }

    private void ApplyFavoriteFlags(IReadOnlyList<string> favoritePaths, IReadOnlyList<string> favoriteOrder)
    {
        var favorites = favoritePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _tracks.Count; index++)
        {
            _tracks[index] = _tracks[index] with { IsFavorite = favorites.Contains(_tracks[index].Path) };
        }

        RebuildDerivedCollections(favoriteOrder);
    }

    private void ReorderTracksInMemory(IReadOnlyList<string> orderedPaths)
    {
        var byPath = _tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        var reordered = orderedPaths.Where(byPath.ContainsKey).Select(path => byPath[path]).ToList();
        foreach (var track in _tracks)
        {
            if (!reordered.Any(item => PathsEqual(item.Path, track.Path)))
            {
                reordered.Add(track);
            }
        }

        _tracks.Clear();
        _tracks.AddRange(reordered);
        RebuildDerivedCollections(_settingsService.Current.FavoriteOrderPaths);
    }

    private void RebuildDerivedCollections(IReadOnlyList<string> favoriteOrder)
    {
        var albums = _tracks
            .GroupBy(AlbumIdOf, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => NormalizeAlbum(group.First().Album), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new AlbumModel(
                group.Key,
                NormalizeAlbum(group.First().Album),
                NormalizeArtist(group.First().Artist),
                group.Count(),
                group.Select(track => track.CoverPath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))))
            .ToList();
        var artists = _tracks
            .GroupBy(track => NormalizeArtist(track.Artist), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ArtistModel(group.Key, InitialOf(group.Key), group.Count()))
            .ToList();
        var favoriteByPath = _tracks
            .Where(track => track.IsFavorite)
            .ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        var favorites = new List<TrackModel>();
        foreach (var path in favoriteOrder)
        {
            if (favoriteByPath.Remove(path, out var track))
            {
                favorites.Add(track);
            }
        }
        favorites.AddRange(_tracks.Where(track => favoriteByPath.ContainsKey(track.Path)));

        _albums.Clear();
        _albums.AddRange(albums);
        _artists.Clear();
        _artists.AddRange(artists);
        _favorites.Clear();
        _favorites.AddRange(favorites);
    }

    private void ReplaceFolders(IEnumerable<string> folders)
    {
        _folders.Clear();
        _folders.AddRange(folders
            .Select(path => NormalizeDirectory(path) ?? path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task SaveSettingsAsync(SettingsSnapshot snapshot)
    {
        await _settingsService.SaveAsync(snapshot);
    }

    private static void DeleteTrackSourceFiles(string trackPath)
    {
        if (File.Exists(trackPath))
        {
            File.Delete(trackPath);
        }

        var stem = Path.Combine(Path.GetDirectoryName(trackPath) ?? string.Empty, Path.GetFileNameWithoutExtension(trackPath));
        foreach (var extension in new[] { ".lrc", ".qrc" })
        {
            var lyricPath = stem + extension;
            if (File.Exists(lyricPath))
            {
                File.Delete(lyricPath);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> DecodeStringMap(
        IReadOnlyDictionary<string, object?> values,
        string key)
    {
        if (!values.TryGetValue(key, out var raw) || raw is not string json || string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var decoded = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
            return new Dictionary<string, string>(decoded, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? NormalizeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var normalized = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(normalized);
            if (!string.Equals(root, normalized, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return Directory.Exists(normalized) ? normalized : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count
            && left.Zip(right).All(pair => PathsEqual(pair.First, pair.Second));
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string AlbumIdOf(TrackModel track)
    {
        return $"{NormalizeArtist(track.Artist)}{AlbumKeySeparator}{NormalizeAlbum(track.Album)}";
    }

    private static string NormalizeAlbum(string? value) => FirstNonEmpty(value, "Unknown Album");

    private static string NormalizeArtist(string? value) => FirstNonEmpty(value, "Unknown Artist");

    private static string InitialOf(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "?" : value.Trim();
        return text[..1].ToUpperInvariant();
    }

    private static string MimeToExtension(string? mime)
    {
        return mime?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };
    }

    private static string HashPath(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void Notify()
    {
        var dispatcher = App.DispatcherQueue;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            dispatcher.TryEnqueue(() => LibraryChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    private sealed record ScanResult(IReadOnlyList<TrackModel> Tracks, string? FatalError);

    private readonly record struct WaveInfo(string? Title, string? Artist, string? Album);
}
