using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Player;

public sealed partial class CoverSearchViewModel : ObservableObject
{
    private readonly ICoverService _coverService;
    private readonly TrackModel _track;
    private CancellationTokenSource? _searchCancellation;
    private string _query;
    private bool _isSearching;
    private bool _isApplying;
    private string _status = "Search Apple Music, Deezer and MusicBrainz artwork.";

    public CoverSearchViewModel(ICoverService coverService, TrackModel track)
    {
        _coverService = coverService;
        _track = track;
        _query = string.Join(
            ' ',
            new[] { track.Title, track.Artist }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public event EventHandler? CoverApplied;

    public ObservableCollection<CoverSearchResultModel> Results { get; } = new();

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
            Status = "Enter a track, artist or album name.";
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var operation = new CancellationTokenSource();
        _searchCancellation = operation;
        IsSearching = true;
        Status = "Searching online covers...";
        Results.Clear();
        try
        {
            var results = await _coverService.SearchOnlineCoversAsync(
                _track,
                query,
                operation.Token);
            if (operation.IsCancellationRequested)
            {
                return;
            }

            foreach (var result in results)
            {
                Results.Add(result);
            }

            Status = Results.Count == 0
                ? "No matching covers found. Try a shorter title."
                : $"{Results.Count} cover(s) · select one to apply";
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
    private async Task SelectResultAsync(CoverSearchResultModel? result)
    {
        if (result is null || IsApplying)
        {
            return;
        }

        IsApplying = true;
        Status = $"Applying {result.SourceLabel} cover...";
        try
        {
            await _coverService.ApplyOnlineCoverAsync(_track, result);
            Status = "Cover updated.";
            CoverApplied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Status = $"Could not apply cover · {exception.Message}";
        }
        finally
        {
            IsApplying = false;
        }
    }
}
