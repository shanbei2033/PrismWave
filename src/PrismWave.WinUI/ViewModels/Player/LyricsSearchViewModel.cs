using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.ViewModels.Player;

public sealed partial class LyricsSearchViewModel : ObservableObject
{
    private readonly PlaybackViewModel _playback;
    private CancellationTokenSource? _searchCancellation;
    private string _query;
    private bool _isSearching;
    private bool _isApplying;
    private string _status = "Search LRCLIB for another lyrics version.";

    public LyricsSearchViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        _query = string.Join(" ", new[]
        {
            playback.CurrentTrack?.Title,
            playback.CurrentTrack?.Artist
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public event EventHandler? ResultApplied;

    public ObservableCollection<LyricsSearchResultModel> Results { get; } = new();

    public string Query
    {
        get => _query;
        set => SetProperty(ref _query, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public bool IsApplying
    {
        get => _isApplying;
        private set => SetProperty(ref _isApplying, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = Query.Trim();
        if (query.Length == 0)
        {
            Status = "Enter a track or artist name.";
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var operation = new CancellationTokenSource();
        _searchCancellation = operation;
        IsSearching = true;
        Status = "Searching online lyrics...";
        Results.Clear();
        try
        {
            var results = await _playback.SearchOnlineLyricsAsync(query, operation.Token);
            if (operation.IsCancellationRequested)
            {
                return;
            }

            foreach (var result in results.OrderByDescending(result => result.LyricsQualityRank))
            {
                Results.Add(result);
            }

            Status = Results.Count == 0
                ? "No matching lyrics found."
                : $"{Results.Count} result(s)";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Status = $"Search failed · {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, operation))
            {
                IsSearching = false;
            }
        }
    }

    [RelayCommand]
    private async Task SelectResultAsync(LyricsSearchResultModel? result)
    {
        if (result is null || IsApplying)
        {
            return;
        }

        IsApplying = true;
        Status = $"Applying {result.Provider} lyrics...";
        try
        {
            if (await _playback.ApplyLyricsSearchResultAsync(result))
            {
                Status = "Lyrics applied.";
                ResultApplied?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Status = "The selected lyrics could not be applied.";
            }
        }
        finally
        {
            IsApplying = false;
        }
    }
}
