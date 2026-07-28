using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Library;

namespace PrismWave_WinUI.ViewModels.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDeveloperLogService _developerLogService;
    private readonly IPlaybackService _playbackService;
    private readonly IMusicFolderPicker _folderPicker;
    private readonly IOnlineAudioCache _onlineAudioCache;
    private readonly IUpdateService _updateService;
    private string _language = "zh-CN";
    private bool _experimentalFeaturesEnabled;
    private bool _onlineModeEnabled = true;
    private OnlineQualityPreference _onlineQualityPreference = OnlineQualityPreference.Lossless;
    private string _appearanceStyle = AppearanceStyleIds.Mica;
    private string _audioOutputMode = AudioOutputPolicy.WasapiSharedId;
    private string _audioOutputDevice = "auto";
    private bool _fadeEnabled = true;
    private int _fadeDurationMs = 220;
    private string _developerLogText = string.Empty;
    private string _developerLogCount = "0 entries";
    private IReadOnlyList<LocalizedOnlineQualityOption> _onlineQualityOptions = [];

    public SettingsViewModel(
        ISettingsService settingsService,
        LibraryFolderManagerViewModel libraryFolders,
        IPlaybackService playbackService,
        IThemeService themeService,
        IDeveloperLogService developerLogService,
        IOnlineAccountService onlineAccountService,
        IMusicFolderPicker folderPicker,
        IOnlineAudioCache onlineAudioCache,
        IUpdateService updateService)
    {
        _settingsService = settingsService;
        _developerLogService = developerLogService;
        _playbackService = playbackService;
        _folderPicker = folderPicker;
        _onlineAudioCache = onlineAudioCache;
        _updateService = updateService;
        _onlineAudioCache.CacheChanged += (_, _) => RefreshCacheStatus();
        LibraryFolders = libraryFolders;
        OnlineAccounts = new OnlineAccountSettingsViewModel(onlineAccountService);
        _developerLogService.LogsChanged += (_, _) => RefreshLogs();
        _playbackService.StateChanged += (_, _) => RefreshPlaybackStatus();
        var settings = settingsService.Current;
        _language = settings.Language;
        _experimentalFeaturesEnabled = settings.ExperimentalFeaturesEnabled;
        _onlineModeEnabled = settings.OnlineModeEnabled;
        _onlineQualityPreference = settings.OnlineQualityPreference;
        // Account sign-in is managed from the Online tab and must remain discoverable
        // even before experimental playback is enabled.
        OnlineAccounts.IsLoginEnabled = true;
        _appearanceStyle = AppearanceStyleIds.Normalize(settings.AppearanceStyle);
        _ = themeService;
        _audioOutputMode = settings.AudioOutputMode;
        _audioOutputDevice = settings.AudioOutputDevice;
        _fadeEnabled = settings.FadeEnabled;
        _fadeDurationMs = settings.FadeDurationMs;

        _onlineQualityOptions = BuildOnlineQualityOptions();
        RefreshLogs();
    }

    public IReadOnlyList<string> LanguageOptions { get; } = new[] { "zh-CN", "zh-TW", "en-US" };
    public IReadOnlyList<AudioOutputModeOptionModel> AudioOutputModeOptions =>
    [
        new(AudioOutputPolicy.CompatibilityId, Text.MpvName, Text.MpvDescription),
        new(AudioOutputPolicy.WasapiSharedId, Text.WasapiSharedName, Text.WasapiSharedDescription),
        new(AudioOutputPolicy.WasapiExclusiveId, Text.WasapiExclusiveName, Text.WasapiExclusiveDescription)
    ];
    public IReadOnlyList<LocalizedOnlineQualityOption> OnlineQualityOptions => _onlineQualityOptions;

    private IReadOnlyList<LocalizedOnlineQualityOption> BuildOnlineQualityOptions() =>
    [
        new(OnlineQualityPreference.Lossless, Text.Lossless),
        new(OnlineQualityPreference.High, Text.HighQuality),
        new(OnlineQualityPreference.Standard, Text.StandardQuality)
    ];
    public IReadOnlyList<LocalizedAppearanceStyleOption> AppearanceStyleOptions =>
    [
        new(AppearanceStyleIds.Solid, Text.SolidAppearance, Text.SolidAppearanceDescription),
        new(AppearanceStyleIds.Mica, Text.MicaAppearance, Text.MicaAppearanceDescription)
    ];
    public OnlineAccountSettingsViewModel OnlineAccounts { get; }
    public LibraryFolderManagerViewModel LibraryFolders { get; }
    public SettingsText Text => SettingsText.For(Language);

    public string Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, value))
            {
                _onlineQualityOptions = BuildOnlineQualityOptions();
                OnPropertyChanged(nameof(Text));
                OnPropertyChanged(nameof(AudioOutputModeOptions));
                OnPropertyChanged(nameof(OnlineQualityOptions));
                OnPropertyChanged(nameof(SelectedOnlineQualityOption));
                OnPropertyChanged(nameof(AppearanceStyleOptions));
                OnPropertyChanged(nameof(AppearanceStyleDescription));
                OnPropertyChanged(nameof(AudioOutputModeDescription));
                OnPropertyChanged(nameof(ThemeName));
                RefreshLogs();
                Save();
            }
        }
    }

    public bool ExperimentalFeaturesEnabled
    {
        get => _experimentalFeaturesEnabled;
        set
        {
            if (SetProperty(ref _experimentalFeaturesEnabled, value))
            {
                RefreshOnlineAccountAvailability();
                Save();
            }
        }
    }

    public async Task SetExperimentalFeaturesEnabledAsync(bool value)
    {
        if (!SetProperty(ref _experimentalFeaturesEnabled, value, nameof(ExperimentalFeaturesEnabled)))
        {
            return;
        }

        RefreshOnlineAccountAvailability();
        await SaveAsync();
    }

    public bool OnlineModeEnabled
    {
        get => _onlineModeEnabled;
        set
        {
            if (SetProperty(ref _onlineModeEnabled, value))
            {
                RefreshOnlineAccountAvailability();
                Save();
            }
        }
    }

    public OnlineQualityPreference OnlineQualityPreference
    {
        get => _onlineQualityPreference;
        set
        {
            if (SetProperty(ref _onlineQualityPreference, value))
            {
                OnPropertyChanged(nameof(SelectedOnlineQualityOption));
                Save();
            }
        }
    }

    public LocalizedOnlineQualityOption? SelectedOnlineQualityOption
    {
        get => OnlineQualityOptions.FirstOrDefault(o => o.Value == _onlineQualityPreference);
        set
        {
            if (value is not null && SetProperty(ref _onlineQualityPreference, value.Value, nameof(OnlineQualityPreference)))
            {
                Save();
            }
        }
    }

    public string AppearanceStyle
    {
        get => _appearanceStyle;
        set
        {
            var normalized = AppearanceStyleIds.Normalize(value);
            if (SetProperty(ref _appearanceStyle, normalized))
            {
                OnPropertyChanged(nameof(AppearanceStyleDescription));
                Save();
            }
        }
    }

    public string AppearanceStyleDescription => AppearanceStyleOptions
        .First(option => option.Value == AppearanceStyle)
        .Description;

    public string ThemeName => Text.ThemeName;

    public double OnlineCacheMaximumGigabytes
    {
        get => _settingsService.Current.OnlineCacheMaximumBytes / (1024d * 1024d * 1024d);
        set
        {
            var normalized = Math.Clamp(value, 0.5, 1024);
            var bytes = (long)Math.Round(normalized * 1024 * 1024 * 1024, MidpointRounding.AwayFromZero);
            if (_settingsService.Current.OnlineCacheMaximumBytes == bytes)
            {
                return;
            }

            _ = _settingsService.SaveAsync(_settingsService.Current with { OnlineCacheMaximumBytes = bytes });
            RefreshCacheStatus();
        }
    }

    public string OnlineCacheDirectory => _onlineAudioCache.Status.DirectoryPath;
    public string OnlineCacheStatus => Text.CacheStatus(
        _onlineAudioCache.Status.CurrentGigabytes,
        _onlineAudioCache.Status.MaximumGigabytes,
        _onlineAudioCache.Status.FileCount);

    public string AudioOutputMode
    {
        get => _audioOutputMode;
        set
        {
            if (SetProperty(ref _audioOutputMode, value))
            {
                OnPropertyChanged(nameof(AudioOutputModeDescription));
                OnPropertyChanged(nameof(ActiveAudioOutputMode));
                Save();
            }
        }
    }

    public string AudioOutputDevice
    {
        get => _audioOutputDevice;
        set
        {
            if (SetProperty(ref _audioOutputDevice, value))
            {
                Save();
            }
        }
    }

    public bool FadeEnabled
    {
        get => _fadeEnabled;
        set
        {
            if (SetProperty(ref _fadeEnabled, value))
            {
                Save();
            }
        }
    }

    public int FadeDurationMs
    {
        get => _fadeDurationMs;
        set
        {
            var normalized = Math.Clamp(value, 0, 2000);
            if (SetProperty(ref _fadeDurationMs, normalized))
            {
                OnPropertyChanged(nameof(FadeDuration));
                Save();
            }
        }
    }

    public string FadeDuration => $"{FadeDurationMs} ms";
    public string AudioOutputModeDescription =>
        AudioOutputModeOptions.FirstOrDefault(option => option.Id == AudioOutputMode)?.Description
        ?? AudioOutputPolicy.Options[1].Description;
    public string ActiveAudioOutputMode =>
        string.IsNullOrWhiteSpace(_playbackService.ActiveAudioOutputModeLabel)
            ? AudioOutputPolicy.GetRouteDisplayName(
                AudioOutputPolicy.BuildFallbackChain(AudioOutputMode)[0])
            : _playbackService.ActiveAudioOutputModeLabel;
    public string? AudioOutputFallbackReason => _playbackService.AudioOutputFallbackReason;
    public string DeveloperLogPath => _developerLogService.FilePath;
    public string DeveloperLogText
    {
        get => _developerLogText;
        private set => SetProperty(ref _developerLogText, value);
    }

    public string DeveloperLogCount
    {
        get => _developerLogCount;
        private set => SetProperty(ref _developerLogCount, value);
    }

    [RelayCommand]
    private void ClearDeveloperLogs()
    {
        _developerLogService.Clear();
    }

    [RelayCommand]
    private void OpenDeveloperLog()
    {
        _developerLogService.OpenLogFile();
    }

    [RelayCommand]
    private async Task ChangeOnlineCacheDirectoryAsync()
    {
        var result = await _folderPicker.PickAsync();
        if (result.Status != MusicFolderPickStatus.Selected || string.IsNullOrWhiteSpace(result.Path))
        {
            return;
        }

        await _settingsService.SaveAsync(_settingsService.Current with
        {
            OnlineCacheDirectory = Path.GetFullPath(result.Path)
        });
        RefreshCacheStatus();
    }

    [RelayCommand]
    private Task ClearOnlineCacheAsync() => _onlineAudioCache.ClearAsync();

    private void Save()
    {
        _ = SaveAsync();
    }

    private Task SaveAsync()
    {
        var current = _settingsService.Current;
        return _settingsService.SaveAsync(current with
        {
            Language = Language,
            ExperimentalFeaturesEnabled = ExperimentalFeaturesEnabled,
            OnlineModeEnabled = OnlineModeEnabled,
            OnlineQualityPreference = OnlineQualityPreference,
            AppearanceStyle = AppearanceStyle,
            AudioOutputMode = AudioOutputMode,
            AudioOutputDevice = AudioOutputDevice,
            FadeEnabled = FadeEnabled,
            FadeDurationMs = FadeDurationMs,
            OnlineCacheMaximumBytes = (long)Math.Round(OnlineCacheMaximumGigabytes * 1024 * 1024 * 1024),
            OnlineCacheDirectory = _settingsService.Current.OnlineCacheDirectory
        });
    }

    private void RefreshCacheStatus()
    {
        OnPropertyChanged(nameof(OnlineCacheDirectory));
        OnPropertyChanged(nameof(OnlineCacheMaximumGigabytes));
        OnPropertyChanged(nameof(OnlineCacheStatus));
    }

    private void RefreshLogs()
    {
        var lines = _developerLogService.Lines;
        DeveloperLogText = string.Join(Environment.NewLine, lines.TakeLast(500));
        DeveloperLogCount = $"{lines.Count} {Text.Entries}";
    }

    private void RefreshPlaybackStatus()
    {
        OnPropertyChanged(nameof(ActiveAudioOutputMode));
        OnPropertyChanged(nameof(AudioOutputFallbackReason));
    }

    private void RefreshOnlineAccountAvailability()
    {
        OnlineAccounts.IsLoginEnabled = true;
    }

    // --- Version checking ---

    public string CurrentVersion => _updateService.CurrentVersion;

    public string LatestVersionDisplay => _updateService.LatestVersion ?? "未检测";

    public bool HasUpdate => _updateService.HasUpdate;

    private bool _isCheckingUpdate;
    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set
        {
            if (SetProperty(ref _isCheckingUpdate, value))
            {
                OnPropertyChanged(nameof(UpdateButtonText));
                OnPropertyChanged(nameof(CanCheckUpdate));
            }
        }
    }

    private bool _isUpToDate;
    public bool IsUpToDate
    {
        get => _isUpToDate;
        private set
        {
            if (SetProperty(ref _isUpToDate, value))
            {
                OnPropertyChanged(nameof(UpdateButtonText));
            }
        }
    }

    public bool CanCheckUpdate => !IsCheckingUpdate;

    public string UpdateButtonText => HasUpdate ? "下载" : IsCheckingUpdate ? "检测中..." : IsUpToDate ? "已是最新" : "检测版本";

    public bool AutoCheckUpdate
    {
        get => _settingsService.Current.AutoCheckUpdate;
        set
        {
            _ = _settingsService.SaveAsync(_settingsService.Current with { AutoCheckUpdate = value });
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckUpdate))]
    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        IsUpToDate = false;
        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            OnPropertyChanged(nameof(LatestVersionDisplay));
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(UpdateButtonText));
            if (!result.HasUpdate)
            {
                IsUpToDate = true;
                await Task.Delay(2000);
                IsUpToDate = false;
            }
        }
        finally
        {
            IsCheckingUpdate = false;
            CheckUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        var url = _updateService.LatestDownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }
}
