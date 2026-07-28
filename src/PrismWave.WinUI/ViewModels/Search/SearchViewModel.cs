using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Search;

public sealed partial class SearchViewModel : ObservableObject
{
    private const int HistoryLimit = 15;
    private readonly IOnlineSearchService _searchService;
    private readonly IPlaybackService _playbackService;
    private readonly ISettingsService _settingsService;
    private readonly ILibraryService? _libraryService;
    private readonly Dictionary<string, SearchSourceState> _sourceStates = new(StringComparer.OrdinalIgnoreCase);
    private string _query = string.Empty;
    private bool _isSearching;
    private string? _error;
    private int _searchRevision;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _coverEnrichmentCancellation;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private string? _currentTrackId;

    private bool _hasSubmittedSearch;

    public SearchViewModel(
        IOnlineSearchService searchService,
        IPlaybackService playbackService,
        ISettingsService settingsService,
        IOnlineAccountService accountService,
        ILibraryService? libraryService = null)
    {
        _searchService = searchService;
        _playbackService = playbackService;
        _settingsService = settingsService;
        _ = accountService;
        _libraryService = libraryService;
        _playbackService.StateChanged += (_, _) => RefreshCurrentTrackState();
        foreach (var item in settingsService.Current.SearchHistory.Take(HistoryLimit))
        {
            History.Add(item);
        }
    }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
            {
                OnPropertyChanged(nameof(HasQuery));
            }
        }
    }

    public ObservableCollection<string> History { get; } = new();
    public ObservableCollection<SearchDisplayItemViewModel> DisplayItems { get; } = new();

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool HasResults => _sourceStates.Values.Any(state => state.Results.Count > 0);
    public bool HasHistory => History.Count > 0;
    public bool HasSubmittedSearch
    {
        get => _hasSubmittedSearch;
        private set
        {
            if (SetProperty(ref _hasSubmittedSearch, value))
            {
                OnPropertyChanged(nameof(ShowHistory));
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
    }

    public bool ShowHistory => !HasSubmittedSearch;
    public bool ShowNoResults => HasSubmittedSearch && !IsSearching && !HasResults;
    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    [RelayCommand]
    private async Task RunSearchAsync()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        await ExecuteSearchAsync(addToHistory: true, _searchCancellation.Token);
    }

    private async Task ExecuteSearchAsync(bool addToHistory, CancellationToken cancellationToken)
    {
        var revision = ++_searchRevision;
        var cleaned = Query.Trim();
        if (cleaned.Length == 0)
        {
            return;
        }

        HasSubmittedSearch = true;
        if (addToHistory)
        {
            await AddHistoryAsync(cleaned);
        }
        IsSearching = true;
        Error = null;
        _sourceStates.Clear();
        AddSource("local", "本地音乐");
        AddSource("online", "在线音乐");
        RebuildDisplayItems();
        try
        {
            var tasks = _sourceStates.Keys
                .Select(source => LoadSourceAsync(source, cleaned, revision, cancellationToken))
                .ToList();
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            if (revision == _searchRevision)
            {
                IsSearching = false;
                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
    }

    [RelayCommand]
    private async Task SelectHistoryAsync(string value)
    {
        Query = value;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        await ExecuteSearchAsync(addToHistory: true, _searchCancellation.Token);
    }

    [RelayCommand]
    private void RemoveHistory(string value)
    {
        History.Remove(value);
        OnPropertyChanged(nameof(HasHistory));
        _ = SaveHistoryAsync();
    }

    [RelayCommand]
    private void PlaySearchResult(SearchResultModel result)
    {
        var track = CreateTrack(result);
        var queue = DisplayItems
            .Where(item => item.IsTrack && item.Result is not null)
            .Select(item => CreateTrack(item.Result!))
            .ToList();
        if (queue.Count == 0)
        {
            queue.Add(track);
        }

        _playbackService.Play(track, queue);
    }

    [RelayCommand]
    private void AddToQueue(SearchResultModel result) => _playbackService.AddToQueue(CreateTrack(result));

    [RelayCommand]
    private void PlayNext(SearchResultModel result) => _playbackService.PlayNext(CreateTrack(result));

    [RelayCommand]
    private async Task ToggleFavoriteAsync(SearchResultModel result)
    {
        if (_libraryService is not null)
        {
            await _libraryService.ToggleFavoriteAsync(CreateTrack(result));
        }
    }

    [RelayCommand]
    private async Task AddToLibraryAsync(SearchResultModel result)
    {
        if (_libraryService is not null)
        {
            await _libraryService.AddOnlineTrackAsync(CreateTrack(result));
        }
    }

    private async Task AddHistoryAsync(string value)
    {
        var existing = History.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            History.Remove(existing);
        }

        History.Insert(0, value);
        while (History.Count > HistoryLimit)
        {
            History.RemoveAt(History.Count - 1);
        }

        OnPropertyChanged(nameof(HasHistory));
        await SaveHistoryAsync();
    }

    private Task SaveHistoryAsync()
    {
        return _settingsService.SaveAsync(_settingsService.Current with
        {
            SearchHistory = History.ToList()
        });
    }

    private void AddSource(string key, string title)
    {
        _sourceStates[key] = new SearchSourceState(title);
    }

    private async Task EnrichCoversAsync(
        string source,
        IReadOnlyList<SearchResultModel> results,
        int revision,
        SynchronizationContext? uiContext)
    {
        // Only enrich online results that are missing covers
        var tracksNeedingCovers = results
            .Select((result, index) => (result, index))
            .Where(item => !item.result.IsLocal && string.IsNullOrWhiteSpace(item.result.CoverPath))
            .ToList();

        if (tracksNeedingCovers.Count == 0)
        {
            return;
        }

        // Cancel any previous enrichment for this source
        _coverEnrichmentCancellation?.Cancel();
        _coverEnrichmentCancellation = new CancellationTokenSource();
        var token = _coverEnrichmentCancellation.Token;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var updated = false;
        var resultList = results.ToList();

        await Task.WhenAll(tracksNeedingCovers.Select(async item =>
        {
            try
            {
                var cover = await _searchService.ResolveCoverAsync(
                    item.result.Title,
                    item.result.Artist,
                    timeout.Token);

                if (!string.IsNullOrWhiteSpace(cover) && !token.IsCancellationRequested)
                {
                    resultList[item.index] = item.result with { CoverPath = cover };
                    updated = true;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Enrichment cancelled
            }
            catch
            {
                // Cover resolution failed for this track — skip
            }
        }));

        if (updated && revision == _searchRevision && !token.IsCancellationRequested)
        {
            _sourceStates[source].Results = resultList;
            uiContext?.Post(static state => ((SearchViewModel)state!).RebuildDisplayItems(), this);
        }
    }

    private async Task LoadSourceAsync(
        string source,
        string query,
        int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = source == "local"
                ? await _searchService.SearchLocalAsync(query, cancellationToken)
                : (await _searchService.SearchAsync(query, cancellationToken))
                    .Where(result => !result.IsLocal)
                    .ToList();
            if (revision != _searchRevision || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _sourceStates[source].Results = results;
            _sourceStates[source].IsLoading = false;
            RebuildDisplayItems();

            // Asynchronously enrich missing covers in the background (non-blocking)
            _ = EnrichCoversAsync(source, results, revision, _uiContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (revision != _searchRevision)
            {
                return;
            }

            var state = _sourceStates[source];
            state.IsLoading = false;
            state.Error = FriendlySourceError(source, exception);
            RebuildDisplayItems();
        }
    }

    private void RebuildDisplayItems()
    {
        DisplayItems.Clear();
        foreach (var source in new[] { "local", "online" })
        {
            if (!_sourceStates.TryGetValue(source, out var state))
            {
                continue;
            }

            DisplayItems.Add(SearchDisplayItemViewModel.CreateHeader(source, state.Title));
            foreach (var result in state.Results)
            {
                var item = SearchDisplayItemViewModel.CreateTrack(source, result);
                item.IsCurrent = string.Equals(CreateTrack(result).Id, _playbackService.CurrentTrack?.Id, StringComparison.Ordinal);
                DisplayItems.Add(item);
            }

            if (state.IsLoading)
            {
                DisplayItems.Add(SearchDisplayItemViewModel.CreateStatus(source, "正在搜索…"));
            }
            else if (!string.IsNullOrWhiteSpace(state.Error))
            {
                DisplayItems.Add(SearchDisplayItemViewModel.CreateStatus(source, state.Error, true));
            }
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    private static TrackModel CreateTrack(SearchResultModel result)
    {
        var source = result.Source ?? (result.IsLocal
            ? result.Title
            : $"online://{result.ProviderKey ?? result.Provider}/{Uri.EscapeDataString(result.ProviderTrackId ?? result.Title)}");
        return new TrackModel(
            result.IsLocal ? source : $"{result.ProviderKey}:{result.ProviderTrackId ?? source}",
            source,
            result.Title,
            result.Artist,
            result.Album,
            result.Duration,
            result.CoverPath,
            !result.IsLocal,
            result.ProviderKey ?? result.Provider,
            result.IsLocal ? null : result.PlaybackUrl,
            DurationSeconds: ParseDuration(result.Duration),
            IsFavorite: result.IsFavorite,
            OnlineProviderTrackId: result.ProviderTrackId);
    }

    private static double ParseDuration(string value) =>
        TimeSpan.TryParse(value, out var duration) ? duration.TotalSeconds : 0;

    private static string FriendlySourceError(string source, Exception exception) => source switch
    {
        "netease" => $"网易云音乐暂时不可用：{exception.Message}",
        "qq" => $"QQ音乐暂时不可用：{exception.Message}",
        "online" => $"在线音乐暂时不可用：{exception.Message}",
        _ => $"本地音乐搜索失败：{exception.Message}"
    };

    private void RefreshCurrentTrackState()
    {
        var currentTrackId = _playbackService.CurrentTrack?.Id;
        if (string.Equals(_currentTrackId, currentTrackId, StringComparison.Ordinal))
        {
            return;
        }

        _currentTrackId = currentTrackId;
        foreach (var item in DisplayItems.Where(item => item.IsTrack && item.Result is not null))
        {
            item.IsCurrent = string.Equals(CreateTrack(item.Result!).Id, currentTrackId, StringComparison.Ordinal);
        }
    }

    private sealed class SearchSourceState(string title)
    {
        public string Title { get; } = title;
        public IReadOnlyList<SearchResultModel> Results { get; set; } = Array.Empty<SearchResultModel>();
        public bool IsLoading { get; set; } = true;
        public string? Error { get; set; }
    }
}
