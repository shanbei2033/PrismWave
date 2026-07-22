using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using PrismWave_WinUI.Tests.TestSupport;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class LibraryServiceTests
{
    [Fact]
    public async Task InitializeWithoutFolders_ContainsNoPlaceholderData()
    {
        var scanner = new FakeScanner();
        var service = CreateService(CreateSettings(), scanner);

        await service.InitializeAsync();

        Assert.Empty(service.Tracks);
        Assert.Empty(service.Albums);
        Assert.Empty(service.Artists);
        Assert.Empty(service.Favorites);
        Assert.Equal(0, scanner.CallCount);
    }

    [Fact]
    public async Task AddFolder_NormalizesPersistsAndScans()
    {
        using var library = new TemporaryMusicLibrary();
        var settings = CreateSettings();
        var scanner = new FakeScanner((folders, _, _) => Task.FromResult(Success(
            folders,
            [CreateTrack(Path.Combine(folders[0], "song.wav"))])));
        var service = CreateService(settings, scanner);

        await service.AddFolderAsync(Path.Combine(library.Root, "."));

        Assert.Equal(Path.GetFullPath(library.Root), Assert.Single(settings.Current.LibraryFolders));
        Assert.Equal(Path.GetFullPath(library.Root), Assert.Single(service.Folders));
        Assert.Single(service.Tracks);
        Assert.Equal(1, scanner.CallCount);
    }

    [Fact]
    public async Task AddDuplicateFolder_DoesNotPersistOrScanTwice()
    {
        using var library = new TemporaryMusicLibrary();
        var settings = CreateSettings();
        var scanner = new FakeScanner((folders, _, _) => Task.FromResult(Success(folders, [])));
        var service = CreateService(settings, scanner);

        await service.AddFolderAsync(library.Root);
        await service.AddFolderAsync(library.Root.ToUpperInvariant());

        Assert.Single(service.Folders);
        Assert.Equal(1, scanner.CallCount);
        Assert.Equal(1, settings.SaveCount);
    }

    [Fact]
    public async Task AddFolder_PublishesFolderBeforeScanCompletes()
    {
        using var library = new TemporaryMusicLibrary();
        var pending = new TaskCompletionSource<LibraryScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner((_, _, _) => pending.Task);
        var service = CreateService(CreateSettings(), scanner);

        var adding = service.AddFolderAsync(library.Root);
        await scanner.WaitForCallsAsync(1);

        Assert.Equal(Path.GetFullPath(library.Root), Assert.Single(service.FolderStatuses).Path);
        Assert.True(service.IsScanning);

        pending.SetResult(Success([library.Root], []));
        await adding;
    }

    [Fact]
    public async Task InvalidFolder_SetsErrorWithoutChangingSettings()
    {
        using var library = new TemporaryMusicLibrary();
        var settings = CreateSettings();
        var scanner = new FakeScanner();
        var service = CreateService(settings, scanner);

        await service.AddFolderAsync(Path.Combine(library.Root, "missing"));

        Assert.Empty(settings.Current.LibraryFolders);
        Assert.Empty(service.Folders);
        Assert.NotNull(service.Error);
        Assert.Equal(0, scanner.CallCount);
    }

    [Fact]
    public async Task RemoveLastFolder_ClearsDerivedCollections()
    {
        using var library = new TemporaryMusicLibrary();
        var settings = CreateSettings([library.Root]);
        var scanner = new FakeScanner((folders, _, _) => Task.FromResult(Success(
            folders,
            [CreateTrack(Path.Combine(library.Root, "song.wav"))])));
        var service = CreateService(settings, scanner);
        await service.InitializeAsync();
        Assert.Single(service.Tracks);

        await service.RemoveFolderAsync(library.Root);

        Assert.Empty(service.Folders);
        Assert.Empty(service.Tracks);
        Assert.Empty(service.Albums);
        Assert.Empty(service.Artists);
        Assert.Empty(settings.Current.LibraryFolders);
    }

    [Fact]
    public async Task RemoveFolder_RemovesOnlyTracksUnderThatRoot()
    {
        using var firstLibrary = new TemporaryMusicLibrary();
        using var secondLibrary = new TemporaryMusicLibrary();
        var settings = CreateSettings([firstLibrary.Root, secondLibrary.Root]);
        var scanner = new FakeScanner((folders, _, _) => Task.FromResult(Success(
            folders,
            folders.Select(folder => CreateTrack(Path.Combine(folder, "song.wav"))).ToList())));
        var service = CreateService(settings, scanner);
        await service.InitializeAsync();

        await service.RemoveFolderAsync(firstLibrary.Root);

        var track = Assert.Single(service.Tracks);
        Assert.StartsWith(Path.GetFullPath(secondLibrary.Root), track.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.GetFullPath(secondLibrary.Root), Assert.Single(service.Folders));
    }

    [Fact]
    public async Task NewScan_CancelsPreviousScan()
    {
        using var library = new TemporaryMusicLibrary();
        var settings = CreateSettings([library.Root]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner();
        scanner.Enqueue(async (folders, _, token) =>
        {
            var waitingForCancellation = Task.Delay(Timeout.InfiniteTimeSpan, token);
            started.TrySetResult();
            try
            {
                await waitingForCancellation;
            }
            catch (OperationCanceledException)
            {
                canceled.TrySetResult();
                throw;
            }

            return Success(folders, []);
        });
        scanner.Enqueue((folders, _, _) => Task.FromResult(Success(
            folders,
            [CreateTrack(Path.Combine(library.Root, "latest.wav"))])));
        var service = CreateService(settings, scanner);

        var firstScan = service.RescanAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var latestScan = service.RescanAsync();

        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.WhenAll(firstScan, latestScan);
        Assert.Equal("latest", Assert.Single(service.Tracks).Title);
    }

    [Fact]
    public async Task CanceledScan_DoesNotPublishFailure()
    {
        using var library = new TemporaryMusicLibrary();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner(async (folders, _, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Success(folders, []);
        });
        var service = CreateService(CreateSettings([library.Root]), scanner);

        var scan = service.RescanAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await scan;

        Assert.Null(service.Error);
        Assert.False(service.IsScanning);
        Assert.Equal(LibraryScanPhase.Idle, service.ScanProgress.Phase);
    }

    [Fact]
    public async Task StaleRevision_CannotReplaceLatestTracks()
    {
        using var library = new TemporaryMusicLibrary();
        var settings = CreateSettings([library.Root]);
        var first = new TaskCompletionSource<LibraryScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner();
        scanner.Enqueue((folders, _, _) => first.Task);
        scanner.Enqueue((folders, _, _) => Task.FromResult(Success(
            folders,
            [CreateTrack(Path.Combine(library.Root, "latest.wav"))])));
        var service = CreateService(settings, scanner);

        var staleScan = service.RescanAsync();
        await scanner.WaitForCallsAsync(1);
        var latestScan = service.RescanAsync();
        await latestScan;
        first.SetResult(Success(
            [library.Root],
            [CreateTrack(Path.Combine(library.Root, "stale.wav"))]));
        await staleScan;

        Assert.Equal("latest", Assert.Single(service.Tracks).Title);
        Assert.Null(service.Error);
    }

    [Fact]
    public async Task FatalRescan_PreservesExistingLibrary()
    {
        using var library = new TemporaryMusicLibrary();
        var settings = CreateSettings([library.Root]);
        var scanner = new FakeScanner();
        scanner.Enqueue((folders, _, _) => Task.FromResult(Success(
            folders,
            [CreateTrack(Path.Combine(library.Root, "kept.wav"))])));
        scanner.Enqueue((folders, _, _) => Task.FromResult(new LibraryScanResult(
            [],
            folders.Select(path => new LibraryFolderStatus(path, false, "Unavailable")).ToList(),
            ["Unavailable"],
            "Unavailable")));
        var service = CreateService(settings, scanner);
        await service.InitializeAsync();

        await service.RescanAsync();

        Assert.Equal("kept", Assert.Single(service.Tracks).Title);
        Assert.Equal("Unavailable", service.Error);
    }

    [Fact]
    public async Task SuccessfulScan_RebuildsAlbumsArtistsAndFavorites()
    {
        using var library = new TemporaryMusicLibrary();
        var path = Path.Combine(library.Root, "favorite.wav");
        var settings = CreateSettings([library.Root], [path]);
        var scanner = new FakeScanner((folders, _, _) => Task.FromResult(Success(
            folders,
            [CreateTrack(path)])));
        var service = CreateService(settings, scanner);

        await service.InitializeAsync();

        var track = Assert.Single(service.Tracks);
        Assert.True(track.IsFavorite);
        Assert.Equal(track.Path, Assert.Single(service.Favorites).Path);
        Assert.Single(service.Albums);
        Assert.Single(service.Artists);
    }

    [Fact]
    public async Task LibraryChanged_IsDispatchedThroughUiDispatcher()
    {
        using var library = new TemporaryMusicLibrary();
        var settings = CreateSettings([library.Root]);
        var dispatcher = new RecordingDispatcher();
        var service = CreateService(settings, new FakeScanner((folders, _, _) =>
            Task.FromResult(Success(folders, []))), dispatcher);
        var changed = 0;
        service.LibraryChanged += (_, _) => changed++;

        await service.InitializeAsync();

        Assert.True(dispatcher.EnqueueCount > 0);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task RemoveTrack_WhenSourceIsLocked_PreservesLibraryAndReportsError()
    {
        using var library = new TemporaryMusicLibrary();
        var path = library.CreateFile("locked.mp3");
        var settings = CreateSettings([library.Root]);
        var scanner = new FakeScanner((folders, _, _) => Task.FromResult(Success(
            folders,
            [CreateTrack(path)])));
        var service = CreateService(settings, scanner);
        await service.InitializeAsync();
        var track = Assert.Single(service.Tracks);
        await using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        await service.RemoveTrackAsync(track, deleteSourceFile: true);

        Assert.True(File.Exists(path));
        Assert.Single(service.Tracks);
        Assert.NotNull(service.Error);
        Assert.Contains("Could not delete", service.Error, StringComparison.Ordinal);
    }

    private static LibraryService CreateService(
        FakeSettingsService settings,
        ILocalMusicScanner scanner,
        IUiDispatcher? dispatcher = null) =>
        new(settings, new FakeCoverService(), scanner, dispatcher ?? new RecordingDispatcher());

    private static FakeSettingsService CreateSettings(
        IReadOnlyList<string>? folders = null,
        IReadOnlyList<string>? favoritePaths = null) =>
        new(new SettingsSnapshot(
            "zh-CN",
            false,
            true,
            "wasapi_shared",
            "auto",
            true,
            220,
            folders ?? [],
            favoritePaths ?? [],
            [],
            [],
            [],
            [],
            new FlutterPreferencesMigrationResult(
                string.Empty,
                false,
                0,
                DateTimeOffset.MinValue,
                new Dictionary<string, object?>())));

    private static LibraryScanResult Success(
        IReadOnlyList<string> folders,
        IReadOnlyList<TrackModel> tracks) =>
        new(
            tracks,
            folders.Select(path => new LibraryFolderStatus(path, true, null)).ToList(),
            [],
            null);

    private static TrackModel CreateTrack(string path) =>
        new(path, path, Path.GetFileNameWithoutExtension(path), "Artist", "Album", "01:00", null);

    private sealed class FakeScanner : ILocalMusicScanner
    {
        private readonly Queue<Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string>, CancellationToken, Task<LibraryScanResult>>> _responses = new();
        private readonly Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string>, CancellationToken, Task<LibraryScanResult>>? _defaultResponse;
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeScanner(Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string>, CancellationToken, Task<LibraryScanResult>>? response = null)
        {
            _defaultResponse = response;
        }

        public int CallCount { get; private set; }

        public void Enqueue(Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string>, CancellationToken, Task<LibraryScanResult>> response) =>
            _responses.Enqueue(response);

        public Task<LibraryScanResult> ScanAsync(
            IReadOnlyList<string> folders,
            IReadOnlyDictionary<string, string> customCoverPaths,
            IProgress<LibraryScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            _called.TrySetResult();
            var response = _responses.Count > 0 ? _responses.Dequeue() : _defaultResponse;
            return response is null
                ? Task.FromResult(Success(folders, []))
                : response(folders, customCoverPaths, cancellationToken);
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (CallCount < count)
            {
                await _called.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await Task.Yield();
            }
        }
    }

    private sealed class FakeSettingsService(SettingsSnapshot current) : ISettingsService
    {
        public SettingsSnapshot Current { get; private set; } = current;
        public int SaveCount { get; private set; }
        public event EventHandler? SettingsChanged;

        public Task SaveAsync(SettingsSnapshot snapshot)
        {
            Current = snapshot;
            SaveCount++;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDispatcher : IUiDispatcher
    {
        public int EnqueueCount { get; private set; }

        public void Enqueue(Action action)
        {
            EnqueueCount++;
            action();
        }
    }

    private sealed class FakeCoverService : ICoverService
    {
        public event EventHandler<CoverChangedEventArgs>? CoverChanged
        {
            add { }
            remove { }
        }
        public string? ResolveCoverPath(TrackModel track) => track.CoverPath;
        public Task<IReadOnlyList<CoverSearchResultModel>> SearchOnlineCoversAsync(TrackModel track, string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CoverSearchResultModel>>([]);
        public Task<string> ApplyOnlineCoverAsync(TrackModel track, CoverSearchResultModel result, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
    }
}
