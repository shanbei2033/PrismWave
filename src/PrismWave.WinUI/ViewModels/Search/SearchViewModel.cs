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
    private string _query = string.Empty;
    private bool _isSearching;
    private string? _error;
    private int _searchRevision;
    private CancellationTokenSource? _searchCancellation;

    public SearchViewModel(IOnlineSearchService searchService, IPlaybackService playbackService, ISettingsService settingsService)
    {
        _searchService = searchService;
        _playbackService = playbackService;
        _settingsService = settingsService;
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
                _ = ScheduleSearchAsync();
            }
        }
    }

    public ObservableCollection<string> History { get; } = new();
    public ObservableCollection<SearchResultModel> Results { get; } = new();

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool HasResults => Results.Count > 0;
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

    private async Task ScheduleSearchAsync()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        try
        {
            await Task.Delay(350, cancellation.Token);
            await ExecuteSearchAsync(addToHistory: false, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ExecuteSearchAsync(bool addToHistory, CancellationToken cancellationToken)
    {
        var revision = ++_searchRevision;
        Results.Clear();
        var cleaned = Query.Trim();
        if (cleaned.Length == 0)
        {
            OnPropertyChanged(nameof(HasResults));
            return;
        }

        if (addToHistory)
        {
            AddHistory(cleaned);
        }
        IsSearching = true;
        Error = null;
        try
        {
            var results = await _searchService.SearchAsync(cleaned, cancellationToken);
            if (revision != _searchRevision || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            foreach (var result in results)
            {
                Results.Add(result);
            }
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
            }
        }
    }

    [RelayCommand]
    private void SelectHistory(string value)
    {
        Query = value;
    }

    [RelayCommand]
    private void RemoveHistory(string value)
    {
        History.Remove(value);
        SaveHistory();
    }

    [RelayCommand]
    private void Clear()
    {
        Query = string.Empty;
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    private void PlaySearchResult(SearchResultModel result)
    {
        var track = new TrackModel(
            result.Source ?? result.Title,
            result.Source ?? (result.IsLocal ? $"local://{result.Title}" : $"online://{result.Provider}/{result.Title}"),
            result.Title,
            result.Artist,
            result.Album,
            result.Duration,
            result.CoverPath,
            !result.IsLocal,
            result.Provider,
            result.IsLocal ? null : result.Source);
        _playbackService.Play(track, new[] { track });
    }

    private void AddHistory(string value)
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

        SaveHistory();
    }

    private void SaveHistory()
    {
        _ = _settingsService.SaveAsync(_settingsService.Current with
        {
            SearchHistory = History.ToList()
        });
    }
}
