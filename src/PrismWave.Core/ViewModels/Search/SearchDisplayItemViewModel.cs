using CommunityToolkit.Mvvm.ComponentModel;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.ViewModels.Search;

public enum SearchDisplayItemKind
{
    Header,
    Track,
    Status
}

public sealed partial class SearchDisplayItemViewModel : ObservableObject
{
    private SearchDisplayItemViewModel(
        SearchDisplayItemKind kind,
        string sourceKey,
        string? header = null,
        SearchResultModel? result = null,
        string? statusMessage = null,
        bool isError = false)
    {
        Kind = kind;
        SourceKey = sourceKey;
        Header = header;
        Result = result;
        StatusMessage = statusMessage;
        IsError = isError;
    }

    public SearchDisplayItemKind Kind { get; }
    public string SourceKey { get; }
    public string? Header { get; }
    public SearchResultModel? Result { get; }
    public string? StatusMessage { get; }
    public bool IsError { get; }
    public bool IsHeader => Kind == SearchDisplayItemKind.Header;
    public bool IsTrack => Kind == SearchDisplayItemKind.Track;
    public bool IsStatus => Kind == SearchDisplayItemKind.Status;
    public bool IsLoadingStatus => IsStatus && !IsError;
    public bool IsErrorStatus => IsStatus && IsError;

    private bool _isCurrent;

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public static SearchDisplayItemViewModel CreateHeader(string sourceKey, string title) =>
        new(SearchDisplayItemKind.Header, sourceKey, header: title);

    public static SearchDisplayItemViewModel CreateTrack(string sourceKey, SearchResultModel result) =>
        new(SearchDisplayItemKind.Track, sourceKey, result: result);

    public static SearchDisplayItemViewModel CreateStatus(
        string sourceKey,
        string message,
        bool isError = false) =>
        new(SearchDisplayItemKind.Status, sourceKey, statusMessage: message, isError: isError);
}
