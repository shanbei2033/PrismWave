using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Infrastructure.Navigation;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave_WinUI.ViewModels.Shell;

public sealed partial class ShellViewModel : ObservableObject
{
    private static readonly HashSet<string> NestedRoutes = new(StringComparer.Ordinal)
    {
        "FullPlay",
        "Hits",
        "AlbumDetail",
        "LocalAlbumDetail",
        "ArtistDetail",
        "TopPlaylist"
    };
    private readonly Stack<string> _backStack = new();
    private readonly ISettingsService _settingsService;
    private readonly ILibraryService _libraryService;
    private string _selectedRoute = "Library";
    private string _searchQuery = string.Empty;
    private bool _isQueuePaneOpen;
    private bool _isOnlineNavigationVisible;
    private string _migrationSummary = string.Empty;

    public ShellViewModel(
        ISettingsService settingsService,
        ILibraryService libraryService,
        PlaybackViewModel playback)
    {
        _settingsService = settingsService;
        _libraryService = libraryService;
        var settings = settingsService.Current;
        _settingsService.SettingsChanged += (_, _) => RefreshSettingsState();
        _libraryService.LibraryChanged += (_, _) => RefreshLibraryStats();
        Playback = playback;
        IsOnlineNavigationVisible = settings.ExperimentalFeaturesEnabled && settings.OnlineModeEnabled;
        SelectedRoute = IsOnlineNavigationVisible ? "Home" : "Library";
        MigrationSummary = settings.Migration.SourceFound
            ? $"Migrated {settings.Migration.MigratedKeyCount} Flutter settings"
            : "No Flutter settings file found";
    }

    public PlaybackViewModel Playback { get; }
    public int FolderCount => _libraryService.Folders.Count;
    public int TrackCount => _libraryService.Tracks.Count;
    public int FavoriteCount => _libraryService.Favorites.Count;

    public string SelectedRoute
    {
        get => _selectedRoute;
        private set => SetProperty(ref _selectedRoute, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public bool IsQueuePaneOpen
    {
        get => _isQueuePaneOpen;
        private set => SetProperty(ref _isQueuePaneOpen, value);
    }

    public bool IsOnlineNavigationVisible
    {
        get => _isOnlineNavigationVisible;
        private set => SetProperty(ref _isOnlineNavigationVisible, value);
    }

    public string MigrationSummary
    {
        get => _migrationSummary;
        private set => SetProperty(ref _migrationSummary, value);
    }

    public bool CanGoBack => _backStack.Count > 0;

    public event EventHandler<ShellNavigationRequest>? NavigationRequested;

    public void RollbackNavigation(string currentRoute, ShellNavigationRequest failedRequest)
    {
        if (string.IsNullOrWhiteSpace(currentRoute))
        {
            return;
        }

        SelectedRoute = currentRoute;
        if (failedRequest.Kind == ShellNavigationKind.Nested &&
            _backStack.Count > 0 &&
            string.Equals(_backStack.Peek(), currentRoute, StringComparison.Ordinal))
        {
            _backStack.Pop();
        }
        else if (failedRequest.Kind == ShellNavigationKind.Back &&
                 !string.IsNullOrWhiteSpace(failedRequest.Route) &&
                 (_backStack.Count == 0 ||
                  !string.Equals(_backStack.Peek(), failedRequest.Route, StringComparison.Ordinal)))
        {
            _backStack.Push(failedRequest.Route);
        }

        OnPropertyChanged(nameof(CanGoBack));
    }

    [RelayCommand]
    public void Navigate(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return;
        }

        if (string.Equals(route, SelectedRoute, StringComparison.Ordinal))
        {
            return;
        }

        var kind = NestedRoutes.Contains(route)
            ? ShellNavigationKind.Nested
            : ShellNavigationKind.Primary;
        if (kind == ShellNavigationKind.Nested && !string.IsNullOrWhiteSpace(SelectedRoute))
        {
            _backStack.Push(SelectedRoute);
        }
        else
        {
            _backStack.Clear();
        }

        NavigateCore(route, kind);
        OnPropertyChanged(nameof(CanGoBack));
    }

    [RelayCommand]
    private void GoBack()
    {
        var route = _backStack.Count > 0
            ? _backStack.Pop()
            : IsOnlineNavigationVisible ? "Home" : "Library";
        NavigateCore(route, ShellNavigationKind.Back);
        OnPropertyChanged(nameof(CanGoBack));
    }

    [RelayCommand]
    private void ToggleQueuePane()
    {
        IsQueuePaneOpen = !IsQueuePaneOpen;
    }

    [RelayCommand]
    private void CloseQueuePane()
    {
        IsQueuePaneOpen = false;
    }

    [RelayCommand]
    private void OpenFullPlay()
    {
        Navigate("FullPlay");
    }

    [RelayCommand]
    private void SubmitSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        Navigate(IsOnlineNavigationVisible ? "Search" : "Library");
    }

    private void RefreshSettingsState()
    {
        var settings = _settingsService.Current;
        IsOnlineNavigationVisible = settings.ExperimentalFeaturesEnabled && settings.OnlineModeEnabled;
        if (!IsOnlineNavigationVisible && (SelectedRoute is "Home" or "Search"))
        {
            Navigate("Library");
        }
    }

    private void RefreshLibraryStats()
    {
        OnPropertyChanged(nameof(FolderCount));
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(FavoriteCount));
    }

    private void NavigateCore(string route, ShellNavigationKind kind)
    {
        SelectedRoute = route;
        NavigationRequested?.Invoke(this, new ShellNavigationRequest(route, kind));
    }
}
