using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Search;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class SearchViewModelTests
{
    [Fact]
    public async Task EditingQuery_DoesNotSearchOrWriteHistory()
    {
        var search = new FakeSearchService();
        var settings = new FakeSettingsService(CreateSettings());
        var viewModel = CreateViewModel(search, settings);

        viewModel.Query = "Song";
        await Task.Delay(420);

        Assert.Empty(search.Calls);
        Assert.Empty(viewModel.History);
        Assert.False(viewModel.HasSubmittedSearch);
    }

    [Fact]
    public async Task SubmitSearch_UsesLocalThenAllOnlineSources()
    {
        var search = new FakeSearchService();
        var accounts = new FakeAccountService(
            new OnlineAccountSnapshot("netease", OnlineProviderAuthState.Disconnected),
            new OnlineAccountSnapshot("qq", OnlineProviderAuthState.Disconnected));
        var settings = new FakeSettingsService(CreateSettings());
        var viewModel = CreateViewModel(search, settings, accounts);
        viewModel.Query = "Song";

        await viewModel.RunSearchCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "local:Song", "online:Song" }, search.Calls.OrderBy(value => value).ToArray());
        Assert.Equal("Song", viewModel.History[0]);
        Assert.True(viewModel.HasSubmittedSearch);
        Assert.Contains(viewModel.DisplayItems, item => item.Header == "本地音乐");
        Assert.Contains(viewModel.DisplayItems, item => item.Header == "在线音乐");
    }

    [Fact]
    public async Task SelectingHistory_MovesItToFrontAndExecutesSearch()
    {
        var settings = new FakeSettingsService(CreateSettings(["First", "Second"]));
        var search = new FakeSearchService();
        var viewModel = CreateViewModel(search, settings);

        await viewModel.SelectHistoryCommand.ExecuteAsync("Second");

        Assert.Equal("Second", viewModel.Query);
        Assert.Equal(new[] { "Second", "First" }, viewModel.History);
        Assert.Contains("local:Second", search.Calls);
    }

    [Fact]
    public async Task NewSubmission_PreventsOlderResultsFromReplacingIt()
    {
        var search = new FakeSearchService { DelayFirstRequest = true };
        var viewModel = CreateViewModel(search, new FakeSettingsService(CreateSettings()));
        viewModel.Query = "First";
        var first = viewModel.RunSearchCommand.ExecuteAsync(null);
        await search.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Query = "Second";
        var second = viewModel.RunSearchCommand.ExecuteAsync(null);
        search.ReleaseFirstRequest.TrySetResult();

        await Task.WhenAll(first, second);

        Assert.DoesNotContain(viewModel.DisplayItems, item => item.Result?.Title == "First");
        Assert.Contains(viewModel.DisplayItems, item => item.Result?.Title == "Second");
    }

    [Fact]
    public async Task ProviderFailure_LeavesSuccessfulSourcesVisibleAndShowsInlineError()
    {
        var search = new FakeSearchService { FailingProvider = "netease" };
        var accounts = new FakeAccountService(
            new OnlineAccountSnapshot("netease", OnlineProviderAuthState.Disconnected));
        var viewModel = CreateViewModel(search, new FakeSettingsService(CreateSettings()), accounts);
        viewModel.Query = "Song";

        await viewModel.RunSearchCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.DisplayItems, item => item.Result?.Provider == "Local");
        Assert.Contains(viewModel.DisplayItems, item =>
            item.SourceKey == "online"
            && item.IsErrorStatus
            && item.StatusMessage?.Contains("暂时不可用", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task PlayingSearchResult_UsesLocalThenOnlineDisplayOrderAsQueue()
    {
        var search = new FakeSearchService();
        var playback = new FakePlaybackService();
        var viewModel = new SearchViewModel(
            search,
            playback,
            new FakeSettingsService(CreateSettings()),
            new FakeAccountService());
        viewModel.Query = "Song";
        await viewModel.RunSearchCommand.ExecuteAsync(null);
        var onlineResult = Assert.Single(
            viewModel.DisplayItems,
            item => item.IsTrack && item.Result is { IsLocal: false }).Result!;

        viewModel.PlaySearchResultCommand.Execute(onlineResult);

        Assert.Equal(2, playback.LastQueue.Count);
        Assert.True(playback.LastQueue[0].IsRemote == false);
        Assert.True(playback.LastQueue[1].IsRemote);
        Assert.Equal(onlineResult.Title, playback.LastPlayed?.Title);
    }

    private static SearchViewModel CreateViewModel(
        FakeSearchService search,
        FakeSettingsService settings,
        FakeAccountService? accounts = null) =>
        new(search, new FakePlaybackService(), settings, accounts ?? new FakeAccountService());

    private static SettingsSnapshot CreateSettings(IReadOnlyList<string>? history = null) => new(
        "zh-CN", true, true, false, "WasapiShared", string.Empty, string.Empty, true, 180,
        [], [], [], [], [], history ?? [],
        new FlutterPreferencesMigrationResult(string.Empty, false, 0, DateTimeOffset.MinValue,
            new Dictionary<string, object?>()));

    private sealed class FakeSearchService : IOnlineSearchService
    {
        public List<string> Calls { get; } = [];
        public bool DelayFirstRequest { get; init; }
        public string? FailingProvider { get; init; }
        public TaskCompletionSource FirstRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<SearchResultModel>> SearchLocalAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"local:{query}");
            if (DelayFirstRequest && query == "First")
            {
                FirstRequestStarted.TrySetResult();
                await ReleaseFirstRequest.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return [new SearchResultModel(query, "Artist", "Album", "Local", "03:00", true, query)];
        }

        public Task<IReadOnlyList<SearchResultModel>> SearchProviderAsync(
            string query,
            string provider,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"{provider}:{query}");
            if (string.Equals(FailingProvider, provider, StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpRequestException("provider unavailable");
            }

            return Task.FromResult<IReadOnlyList<SearchResultModel>>(
                [new SearchResultModel(query, "Artist", "Album", provider, "03:00", false, $"online://{provider}/1")]);
        }

        public Task<IReadOnlyList<SearchResultModel>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"online:{query}");
            if (DelayFirstRequest && query == "First")
            {
                FirstRequestStarted.TrySetResult();
                return WaitAndReturnAsync(query, cancellationToken);
            }

            if (string.Equals(FailingProvider, "netease", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromException<IReadOnlyList<SearchResultModel>>(
                    new HttpRequestException("provider unavailable"));
            }

            return Task.FromResult<IReadOnlyList<SearchResultModel>>(
                [new SearchResultModel(query, "Artist", "Album", "NetEase", "03:00", false, $"online://netease/1")]);
        }

        private async Task<IReadOnlyList<SearchResultModel>> WaitAndReturnAsync(
            string query,
            CancellationToken cancellationToken)
        {
            await ReleaseFirstRequest.Task.WaitAsync(cancellationToken);
            return [new SearchResultModel(query, "Artist", "Album", "NetEase", "03:00", false, $"online://netease/1")];
        }
    }

    private sealed class FakeSettingsService(SettingsSnapshot current) : ISettingsService
    {
        public SettingsSnapshot Current { get; private set; } = current;
        public event EventHandler? SettingsChanged;
        public Task SaveAsync(SettingsSnapshot snapshot)
        {
            Current = snapshot;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccountService(params OnlineAccountSnapshot[] snapshots) : IOnlineAccountService
    {
        private readonly Dictionary<string, OnlineAccountSnapshot> _snapshots = snapshots.ToDictionary(
            item => item.ProviderKey,
            StringComparer.OrdinalIgnoreCase);
        public event EventHandler<OnlineAccountSnapshot>? AccountChanged
        {
            add { }
            remove { }
        }
        public OnlineAccountSnapshot GetSnapshot(string providerKey) =>
            _snapshots.TryGetValue(providerKey, out var snapshot)
                ? snapshot
                : new OnlineAccountSnapshot(providerKey, OnlineProviderAuthState.Disconnected);
        public Task<OnlineLoginChallenge> CreateChallengeAsync(string providerKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlineAccountSnapshot> PollAsync(string providerKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlineProviderSession?> GetSessionAsync(string providerKey, CancellationToken cancellationToken) => Task.FromResult<OnlineProviderSession?>(null);
        public Task<OnlineProviderSession?> HandleAuthenticationFailureAsync(string providerKey, CancellationToken cancellationToken) => Task.FromResult<OnlineProviderSession?>(null);
        public Task InvalidateSessionAsync(string providerKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SignOutAsync(string providerKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public TrackModel? LastPlayed { get; private set; }
        public IReadOnlyList<TrackModel> LastQueue { get; private set; } = [];
        public TrackModel? CurrentTrack => null;
        public IReadOnlyList<TrackModel> Queue => [];
        public PlaybackMode Mode => PlaybackMode.Loop;
        public PlaybackStatus Status => PlaybackStatus.Idle;
        public double Volume => 0.5;
        public double PositionSeconds => 0;
        public double DurationSeconds => 0;
        public bool IsLoading => false;
        public bool IsPlaying => false;
        public string? Error => null;
        public IReadOnlyList<WindowsDsdDeviceModel> WindowsDsdDevices => [];
        public bool WindowsDsdAvailable => false;
        public string? WindowsDsdOutputModeLabel => null;
        public string? WindowsDsdActiveDeviceName => null;
        public string? WindowsDsdFallbackReason => null;
        public event EventHandler? StateChanged
        {
            add { }
            remove { }
        }
        public void Play(TrackModel track, IReadOnlyList<TrackModel>? queue = null)
        {
            LastPlayed = track;
            LastQueue = queue ?? [track];
        }
        public void Stop() { }
        public void TogglePlayPause() { }
        public void Next() { }
        public void Previous() { }
        public void CycleMode() { }
        public void SetVolume(double volume) { }
        public void Seek(double seconds) { }
        public void PlayFromQueue(TrackModel track) { }
        public void ReorderQueue(IReadOnlyList<TrackModel> tracks) { }
        public void RemoveFromQueue(TrackModel track) { }
        public void ClearQueue() { }
        public Task RefreshWindowsDsdDevicesAsync() => Task.CompletedTask;
    }
}
