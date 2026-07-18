using PrismWave_WinUI.Infrastructure.Persistence;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using PrismWave_WinUI.ViewModels.Hits;
using PrismWave_WinUI.ViewModels.Home;
using PrismWave_WinUI.ViewModels.Library;
using PrismWave_WinUI.ViewModels.Player;
using PrismWave_WinUI.ViewModels.Search;
using PrismWave_WinUI.ViewModels.Settings;
using PrismWave_WinUI.ViewModels.Shell;

namespace PrismWave_WinUI.Infrastructure;

public sealed class AppServices
{
    private AppServices()
    {
    }

    public required ISettingsService SettingsService { get; init; }
    public required ILibraryService LibraryService { get; init; }
    public required IPlaybackService PlaybackService { get; init; }
    public required IOnlineProviderService OnlineProviderService { get; init; }
    public required IOnlinePlaybackResolver OnlinePlaybackResolver { get; init; }
    public required IOnlineAudioCache OnlineAudioCache { get; init; }
    public required IOnlineAccountService OnlineAccountService { get; init; }
    public required IOnlineHomeService OnlineHomeService { get; init; }
    public required IOnlineSearchService OnlineSearchService { get; init; }
    public required IHitsService HitsService { get; init; }
    public required ILyricsService LyricsService { get; init; }
    public required ICoverService CoverService { get; init; }
    public required IThemeService ThemeService { get; init; }
    public required IDeveloperLogService DeveloperLogService { get; init; }

    public required PlaybackViewModel Playback { get; init; }
    public required ShellViewModel Shell { get; init; }
    public required HomeViewModel Home { get; init; }
    public required SearchViewModel Search { get; init; }
    public required LibraryViewModel Library { get; init; }
    public required LibraryFolderManagerViewModel LibraryFolders { get; init; }
    public required AlbumsViewModel Albums { get; init; }
    public required ArtistsViewModel Artists { get; init; }
    public required FavoritesViewModel Favorites { get; init; }
    public required SettingsViewModel Settings { get; init; }
    public required HitsStatusViewModel Hits { get; init; }

    public static AppServices Create()
    {
        var migration = new FlutterPreferencesMigrationService();
        var developerLogService = new DeveloperLogService();
        var settingsService = new SettingsService(migration);
        var coverService = new CoverService(settingsService);
        var uiDispatcher = new WinUiDispatcher(App.DispatcherQueue);
        var localMusicScanner = new LocalMusicScanner();
        var musicFolderPicker = new WindowsMusicFolderPicker(() => App.WindowHandle);
        var libraryService = new LibraryService(settingsService, coverService, localMusicScanner, uiDispatcher);
        var onlineAccountService = new OnlineAccountService(new PasswordVaultCredentialStore());
        var onlineProviderService = new OnlineProviderService(
            accountService: onlineAccountService,
            qualityPreference: () => settingsService.Current.OnlineQualityPreference);
        var onlinePlaybackResolver = new OnlinePlaybackResolver(onlineProviderService);
        var onlineAudioCache = new OnlineAudioCache(settingsService);
        var playbackService = new PlaybackService(settingsService, onlinePlaybackResolver, onlineAudioCache);
        IHitsPlaybackSession hitsPlaybackSession = playbackService;
        var onlineHomeService = new OnlineHomeService();
        var onlineSearchService = new OnlineSearchService(libraryService, onlineProviderService);
        var hitsService = new HitsService();
        var lyricsService = new LyricsService(settingsService);
        var themeService = new ThemeService(settingsService);

        var playback = new PlaybackViewModel(
            playbackService,
            lyricsService,
            coverService,
            libraryService);
        var shell = new ShellViewModel(settingsService, libraryService, playback);
        var home = new HomeViewModel(onlineHomeService, playbackService, coverService);
        var search = new SearchViewModel(
            onlineSearchService,
            playbackService,
            settingsService,
            onlineAccountService,
            libraryService);
        var libraryFolders = new LibraryFolderManagerViewModel(libraryService, musicFolderPicker, settingsService);
        var library = new LibraryViewModel(libraryService, playbackService, libraryFolders);
        var albums = new AlbumsViewModel(libraryService, playbackService);
        var artists = new ArtistsViewModel(libraryService, playbackService);
        var favorites = new FavoritesViewModel(libraryService, playbackService);
        // Keep the shared settings construction explicit; the cache and folder picker are
        // additional services owned by this same SettingsViewModel instance.
        // new SettingsViewModel(settingsService, libraryFolders, playbackService, themeService, developerLogService, onlineAccountService)
        var settings = new SettingsViewModel(settingsService, libraryFolders, playbackService, themeService, developerLogService, onlineAccountService, musicFolderPicker, onlineAudioCache);
        var hits = new HitsStatusViewModel(hitsService, hitsPlaybackSession);

        return new AppServices
        {
            SettingsService = settingsService,
            LibraryService = libraryService,
            PlaybackService = playbackService,
            OnlineProviderService = onlineProviderService,
            OnlinePlaybackResolver = onlinePlaybackResolver,
            OnlineAudioCache = onlineAudioCache,
            OnlineAccountService = onlineAccountService,
            OnlineHomeService = onlineHomeService,
            OnlineSearchService = onlineSearchService,
            HitsService = hitsService,
            LyricsService = lyricsService,
            CoverService = coverService,
            ThemeService = themeService,
            DeveloperLogService = developerLogService,
            Playback = playback,
            Shell = shell,
            Home = home,
            Search = search,
            Library = library,
            LibraryFolders = libraryFolders,
            Albums = albums,
            Artists = artists,
            Favorites = favorites,
            Settings = settings,
            Hits = hits
        };
    }
}
