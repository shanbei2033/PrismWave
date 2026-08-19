using System.Text.Json;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Infrastructure.Library;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class LibraryService : ILibraryService
{
    private const char AlbumKeySeparator = '\u001f';
    private readonly ISettingsService _settingsService;
    private readonly ICoverService _coverService;
    private readonly ILocalMusicScanner _scanner;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly List<TrackModel> _tracks = new();
    private readonly List<string> _folders = new();
    private readonly List<AlbumModel> _albums = new();
    private readonly List<ArtistModel> _artists = new();
    private readonly List<TrackModel> _favorites = new();
    private readonly List<LibraryFolderStatus> _folderStatuses = new();
    private readonly SemaphoreSlim _folderMutationGate = new(1, 1);
    private readonly object _scanSync = new();
    private CancellationTokenSource? _activeScanCancellation;
    private long _scanRevision;

    public LibraryService(ISettingsService settingsService, ICoverService coverService)
        : this(settingsService, coverService, new LocalMusicScanner(), new ImmediateUiDispatcher())
    {
    }

    public LibraryService(
        ISettingsService settingsService,
        ICoverService coverService,
        ILocalMusicScanner scanner,
        IUiDispatcher uiDispatcher)
    {
        _settingsService = settingsService;
        _coverService = coverService;
        _scanner = scanner;
        _uiDispatcher = uiDispatcher;
        _coverService.CoverChanged += CoverService_CoverChanged;
        ReplaceFolders(settingsService.Current.LibraryFolders);
        RefreshConfiguredFolderStatuses();
    }

    public IReadOnlyList<TrackModel> Tracks => _tracks;
    public IReadOnlyList<string> Folders => _folders;
    public IReadOnlyList<AlbumModel> Albums => _albums;
    public IReadOnlyList<ArtistModel> Artists => _artists;
    public IReadOnlyList<TrackModel> Favorites => _favorites;
    public IReadOnlyList<LibraryFolderStatus> FolderStatuses => _folderStatuses;
    public LibraryScanProgress ScanProgress { get; private set; } = LibraryScanProgress.Idle;
    public bool IsScanning { get; private set; }
    public string? Error { get; private set; }
    public event EventHandler? LibraryChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return _folders.Count == 0
            ? CompleteEmptyInitializationAsync()
            : RescanAsync(cancellationToken);
    }

    public Task AddFolderAsync(string folder) => AddFolderAsync(folder, CancellationToken.None);

    public async Task AddFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var normalized = LibraryFolderPath.Normalize(folder);
        if (normalized is null)
        {
            Error = "The selected music folder is unavailable.";
            Notify();
            return;
        }

        await _folderMutationGate.WaitAsync(cancellationToken);
        try
        {
            var folders = _settingsService.Current.LibraryFolders
                .Select(path => LibraryFolderPath.Normalize(path, requireExisting: false) ?? path)
                .ToList();
            if (folders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            folders.Add(normalized);
            await SaveSettingsAsync(_settingsService.Current with { LibraryFolders = folders });
            ReplaceFolders(folders);
        }
        finally
        {
            _folderMutationGate.Release();
        }

        await RescanAsync(cancellationToken);
    }

    public Task RemoveFolderAsync(string folder) => RemoveFolderAsync(folder, CancellationToken.None);

    public async Task RemoveFolderAsync(string folder, CancellationToken cancellationToken)
    {
        await _folderMutationGate.WaitAsync(cancellationToken);
        try
        {
            var normalized = LibraryFolderPath.Normalize(folder, requireExisting: false) ?? folder;
            var folders = _settingsService.Current.LibraryFolders
                .Where(item => !PathsEqual(
                    LibraryFolderPath.Normalize(item, requireExisting: false) ?? item,
                    normalized))
                .ToList();
            await SaveSettingsAsync(_settingsService.Current with { LibraryFolders = folders });
            ReplaceFolders(folders);
        }
        finally
        {
            _folderMutationGate.Release();
        }

        await RescanAsync(cancellationToken);
    }

    public Task RescanAsync() => RescanAsync(CancellationToken.None);

    public async Task RescanAsync(CancellationToken cancellationToken)
    {
        var (revision, scanCancellation) = BeginScan(cancellationToken);
        try
        {
            IsScanning = true;
            Error = null;
            ReplaceFolders(_settingsService.Current.LibraryFolders);
            RefreshConfiguredFolderStatuses();
            ScanProgress = new LibraryScanProgress(revision, LibraryScanPhase.Enumerating, 0, 0, null);
            Notify();

            if (_folders.Count == 0)
            {
                if (IsCurrentScan(revision))
                {
                    _folderStatuses.Clear();
                    var onlineTracks = new List<TrackModel>();
                    MergeOnlineTracks(onlineTracks, _settingsService.Current.OnlineLibraryTracks);
                    ReplaceTracks(onlineTracks, []);
                    IsScanning = false;
                    ScanProgress = new LibraryScanProgress(revision, LibraryScanPhase.Completed, 0, 0, null);
                    Notify();
                }
                return;
            }

            var settings = _settingsService.Current;
            StartupLog.Write($"library.scan.start: folders={_folders.Count}");
            var customCovers = settings.CustomCoverPaths
                ?? DecodeStringMap(settings.Migration.Values, "library.customCoverPaths");
            var progress = new InlineProgress<LibraryScanProgress>(value =>
            {
                if (!IsCurrentScan(revision))
                {
                    return;
                }

                ScanProgress = value with { Revision = revision };
                Notify();
            });
            var scanResult = await _scanner.ScanAsync(
                _folders.ToList(),
                customCovers,
                progress,
                scanCancellation.Token);
            if (!IsCurrentScan(revision))
            {
                return;
            }

            ReplaceFolderStatuses(scanResult.FolderStatuses);
            if (scanResult.FatalError is not null)
            {
                Error = scanResult.FatalError;
                ScanProgress = new LibraryScanProgress(
                    revision,
                    LibraryScanPhase.Failed,
                    ScanProgress.DiscoveredFiles,
                    ScanProgress.ProcessedFiles,
                    null);
                StartupLog.Write($"library.scan.failed: {scanResult.FatalError}");
                return;
            }

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

            MergeOnlineTracks(tracks, settings.OnlineLibraryTracks);
            ReplaceTracks(tracks, favoriteOrder);
            Error = scanResult.Warnings.FirstOrDefault();
            ScanProgress = new LibraryScanProgress(
                revision,
                LibraryScanPhase.Completed,
                ScanProgress.DiscoveredFiles,
                tracks.Count,
                null);
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
        catch (OperationCanceledException) when (scanCancellation.IsCancellationRequested)
        {
            if (IsCurrentScan(revision))
            {
                ScanProgress = LibraryScanProgress.Idle with { Revision = revision };
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentScan(revision))
            {
                Error = $"Library scan failed: {exception.Message}";
                ScanProgress = new LibraryScanProgress(
                    revision,
                    LibraryScanPhase.Failed,
                    ScanProgress.DiscoveredFiles,
                    ScanProgress.ProcessedFiles,
                    null);
                StartupLog.Write("library.scan.failed", exception);
            }
        }
        finally
        {
            if (IsCurrentScan(revision))
            {
                IsScanning = false;
                Notify();
            }

            EndScan(scanCancellation);
        }
    }

    public async Task ToggleFavoriteAsync(TrackModel track)
    {
        if (track.IsRemote)
        {
            await ToggleOnlineFavoriteAsync(track);
            return;
        }

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

    public async Task AddOnlineTrackAsync(TrackModel track)
    {
        if (!track.IsRemote || string.IsNullOrWhiteSpace(track.Path))
        {
            return;
        }

        var descriptor = track.Path;
        var entries = (_settingsService.Current.OnlineLibraryTracks ?? [])
            .Where(e => !PathsEqual(e.Path, descriptor))
            .ToList();
        entries.Add(ToEntry(track));

        await SaveSettingsAsync(_settingsService.Current with { OnlineLibraryTracks = entries });

        var existingIndex = _tracks.FindIndex(t => PathsEqual(t.Path, descriptor));
        if (existingIndex >= 0)
        {
            _tracks[existingIndex] = track;
        }
        else
        {
            _tracks.Add(track);
        }

        RebuildDerivedCollections(_settingsService.Current.FavoriteOrderPaths);
        StartupLog.Write($"library.online.add: provider={track.Provider}, title=\"{track.Title}\"");
        Notify();
    }

    public bool IsOnlineTrackInLibrary(string descriptor)
    {
        return _tracks.Any(t => t.IsRemote && PathsEqual(t.Path, descriptor));
    }

    private async Task ToggleOnlineFavoriteAsync(TrackModel track)
    {
        var descriptor = track.Path;
        var entries = (_settingsService.Current.OnlineLibraryTracks ?? []).ToList();
        var index = entries.FindIndex(e => PathsEqual(e.Path, descriptor));

        if (index < 0)
        {
            var entry = ToEntry(track) with { IsFavorite = true };
            entries.Add(entry);
            await SaveSettingsAsync(_settingsService.Current with { OnlineLibraryTracks = entries });

            var trackWithFavorite = track with { IsFavorite = true };
            var existingIndex = _tracks.FindIndex(t => PathsEqual(t.Path, descriptor));
            if (existingIndex >= 0)
            {
                _tracks[existingIndex] = trackWithFavorite;
            }
            else
            {
                _tracks.Add(trackWithFavorite);
            }
        }
        else
        {
            var existing = entries[index];
            entries[index] = existing with { IsFavorite = !existing.IsFavorite };
            await SaveSettingsAsync(_settingsService.Current with { OnlineLibraryTracks = entries });

            var trackIndex = _tracks.FindIndex(t => PathsEqual(t.Path, descriptor));
            if (trackIndex >= 0)
            {
                _tracks[trackIndex] = _tracks[trackIndex] with { IsFavorite = !_tracks[trackIndex].IsFavorite };
            }
        }

        RebuildDerivedCollections(_settingsService.Current.FavoriteOrderPaths);
        StartupLog.Write($"library.online.favorite: descriptor={descriptor}");
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
        if (track.IsRemote)
        {
            var descriptor = track.Path;
            var entries = (_settingsService.Current.OnlineLibraryTracks ?? [])
                .Where(e => !PathsEqual(e.Path, descriptor))
                .ToList();
            await SaveSettingsAsync(_settingsService.Current with { OnlineLibraryTracks = entries });
            _tracks.RemoveAll(t => PathsEqual(t.Path, descriptor));
            RebuildDerivedCollections(_settingsService.Current.FavoriteOrderPaths);
            StartupLog.Write($"library.online.remove: descriptor={descriptor}");
            Notify();
            return;
        }

        if (string.IsNullOrWhiteSpace(track.Path))
        {
            return;
        }

        if (deleteSourceFile)
        {
            try
            {
                await Task.Run(() => DeleteTrackSourceFiles(track.Path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Error = $"Could not delete '{track.Title}': {exception.Message}";
                StartupLog.Write($"library.remove.failed: path={track.Path}, error={exception.Message}");
                Notify();
                return;
            }
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
        Error = null;
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
        var byPath = _tracks
            .GroupBy(track => track.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
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
            .GroupBy(track => track.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
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
            .Select(path => LibraryFolderPath.Normalize(path, requireExisting: false) ?? path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void ReplaceFolderStatuses(IReadOnlyList<LibraryFolderStatus> statuses)
    {
        _folderStatuses.Clear();
        _folderStatuses.AddRange(statuses);
    }

    private void RefreshConfiguredFolderStatuses()
    {
        _folderStatuses.Clear();
        foreach (var folder in _folders)
        {
            var available = Directory.Exists(folder);
            _folderStatuses.Add(new LibraryFolderStatus(
                folder,
                available,
                available ? null : $"Music folder is unavailable: {folder}"));
        }
    }

    private Task CompleteEmptyInitializationAsync()
    {
        _folderStatuses.Clear();
        var onlineTracks = new List<TrackModel>();
        MergeOnlineTracks(onlineTracks, _settingsService.Current.OnlineLibraryTracks);
        ReplaceTracks(onlineTracks, []);
        Error = null;
        IsScanning = false;
        ScanProgress = LibraryScanProgress.Idle;
        Notify();
        return Task.CompletedTask;
    }

    public async Task<bool> RefreshTrackAsync(TrackModel track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var index = -1;
        for (var candidate = 0; candidate < _tracks.Count; candidate++)
        {
            if (PathsEqual(_tracks[candidate].Path, track.Path))
            {
                index = candidate;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        var settings = _settingsService.Current;
        string? customCover = null;
        settings.CustomCoverPaths?.TryGetValue(track.Path, out customCover);
        var refreshed = await Task.Run(() => _scanner.ScanFile(track.Path, customCover));
        var identity = TrackCoverIdentity.CreateKey(refreshed.Title, refreshed.Artist);
        if (identity.Length > 0
            && settings.CustomCoverPaths is not null
            && settings.CustomCoverPaths.TryGetValue(identity, out var identityCover)
            && File.Exists(identityCover))
        {
            refreshed = refreshed with { CoverPath = Path.GetFullPath(identityCover) };
        }

        // 保留原位置的排序与收藏标记，仅替换该单曲元数据。
        refreshed = refreshed with { IsFavorite = _tracks[index].IsFavorite };
        var tracks = _tracks.ToList();
        tracks[index] = refreshed;
        ReplaceTracks(tracks, settings.FavoriteOrderPaths);
        StartupLog.Write($"library.track.refreshed: path={track.Path}, title=\"{refreshed.Title}\"");
        Notify();
        return true;
    }

    private (long Revision, CancellationTokenSource Cancellation) BeginScan(CancellationToken cancellationToken)
    {
        lock (_scanSync)
        {
            _activeScanCancellation?.Cancel();
            _activeScanCancellation?.Dispose();
            _activeScanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return (++_scanRevision, _activeScanCancellation);
        }
    }

    private bool IsCurrentScan(long revision)
    {
        lock (_scanSync)
        {
            return revision == _scanRevision;
        }
    }

    private void EndScan(CancellationTokenSource cancellation)
    {
        lock (_scanSync)
        {
            if (!ReferenceEquals(_activeScanCancellation, cancellation))
            {
                return;
            }

            _activeScanCancellation = null;
            cancellation.Dispose();
        }
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

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count
            && left.Zip(right).All(pair => PathsEqual(pair.First, pair.Second));
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static OnlineLibraryTrackEntry ToEntry(TrackModel track) => new(
        track.Provider,
        track.OnlineProviderTrackId ?? string.Empty,
        track.Path,
        track.Title,
        track.Artist,
        track.Album,
        track.Duration,
        track.CoverPath,
        track.PlaybackUrl,
        track.DurationSeconds,
        track.IsFavorite);

    private static TrackModel FromEntry(OnlineLibraryTrackEntry entry)
    {
        var path = !string.IsNullOrWhiteSpace(entry.Path)
            ? entry.Path
            : $"online://{entry.Provider.ToLowerInvariant()}/{Uri.EscapeDataString(entry.ProviderTrackId)}";
        return new TrackModel(
        $"{entry.Provider}:{entry.ProviderTrackId}",
        path,
        entry.Title,
        entry.Artist,
        entry.Album,
        entry.Duration,
        entry.CoverUrl,
        IsRemote: true,
        Provider: entry.Provider,
        PlaybackUrl: entry.PlaybackUrl,
        DurationSeconds: entry.DurationSeconds,
        IsFavorite: entry.IsFavorite,
        OnlineProviderTrackId: entry.ProviderTrackId);
    }

    private static void MergeOnlineTracks(
        List<TrackModel> tracks,
        IReadOnlyList<OnlineLibraryTrackEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return;
        }

        var existingPaths = tracks
            .Select(track => track.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var path = !string.IsNullOrWhiteSpace(entry.Path)
                ? entry.Path
                : $"online://{entry.Provider.ToLowerInvariant()}/{Uri.EscapeDataString(entry.ProviderTrackId)}";
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!existingPaths.Contains(path))
            {
                tracks.Add(FromEntry(entry));
                existingPaths.Add(path);
            }
        }
    }

    private static string AlbumIdOf(TrackModel track)
    {
        return $"{NormalizeArtist(track.Artist)}{AlbumKeySeparator}{NormalizeAlbum(track.Album)}";
    }

    private static string NormalizeAlbum(string? value) => FirstNonEmpty(value, "Unknown Album");

    private static string NormalizeArtist(string? value) => FirstNonEmpty(value, "Unknown Artist");

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string InitialOf(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "?" : value.Trim();
        return text[..1].ToUpperInvariant();
    }

    private void Notify()
    {
        _uiDispatcher.Enqueue(() => LibraryChanged?.Invoke(this, EventArgs.Empty));
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Enqueue(Action action) => action();
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
