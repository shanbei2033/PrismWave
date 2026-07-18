using System.Collections.ObjectModel;
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
    private string _language = "zh-CN";
    private bool _experimentalFeaturesEnabled;
    private bool _onlineModeEnabled = true;
    private OnlineQualityPreference _onlineQualityPreference = OnlineQualityPreference.Lossless;
    private bool _lowEffects;
    private string _appearanceStyle = AppearanceStyleIds.Mica;
    private string _audioOutputMode = AudioOutputPolicy.WasapiSharedId;
    private string _audioOutputDevice = "auto";
    private string _windowsDsdDevice = "auto";
    private bool _fadeEnabled = true;
    private int _fadeDurationMs = 220;
    private bool _isRefreshingDsdDevices;
    private string _developerLogText = string.Empty;
    private string _developerLogCount = "0 entries";

    public SettingsViewModel(
        ISettingsService settingsService,
        LibraryFolderManagerViewModel libraryFolders,
        IPlaybackService playbackService,
        IThemeService themeService,
        IDeveloperLogService developerLogService,
        IOnlineAccountService onlineAccountService,
        IMusicFolderPicker folderPicker,
        IOnlineAudioCache onlineAudioCache)
    {
        _settingsService = settingsService;
        _developerLogService = developerLogService;
        _playbackService = playbackService;
        _folderPicker = folderPicker;
        _onlineAudioCache = onlineAudioCache;
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
        _lowEffects = settings.LowEffects;
        _appearanceStyle = AppearanceStyleIds.Normalize(settings.AppearanceStyle);
        _ = themeService;
        _audioOutputMode = settings.AudioOutputMode;
        _audioOutputDevice = settings.AudioOutputDevice;
        _windowsDsdDevice = settings.WindowsDsdDevice;
        _fadeEnabled = settings.FadeEnabled;
        _fadeDurationMs = settings.FadeDurationMs;
        MigrationSource = settings.Migration.SourceFile;
        MigrationStatus = settings.Migration.SourceFound
            ? $"Migrated {settings.Migration.MigratedKeyCount} keys"
            : "No previous Flutter preference file found";

        RefreshLogs();
        _ = RefreshWindowsDsdDevicesAsync();
    }

    public IReadOnlyList<string> LanguageOptions { get; } = new[] { "zh-CN", "zh-TW", "en-US" };
    public IReadOnlyList<AudioOutputModeOptionModel> AudioOutputModeOptions =>
    [
        new(AudioOutputPolicy.CompatibilityId, Text.MpvName, Text.MpvDescription),
        new(AudioOutputPolicy.WasapiSharedId, Text.WasapiSharedName, Text.WasapiSharedDescription),
        new(AudioOutputPolicy.WasapiExclusiveId, Text.WasapiExclusiveName, Text.WasapiExclusiveDescription)
    ];
    public IReadOnlyList<LocalizedOnlineQualityOption> OnlineQualityOptions =>
    [
        new(OnlineQualityPreference.Lossless, Text.Lossless),
        new(OnlineQualityPreference.High, Text.HighQuality),
        new(OnlineQualityPreference.Standard, Text.StandardQuality)
    ];
    public IReadOnlyList<LocalizedAppearanceStyleOption> AppearanceStyleOptions =>
    [
        new(AppearanceStyleIds.Solid, Text.SolidAppearance, Text.SolidAppearanceDescription),
        new(AppearanceStyleIds.Mica, Text.MicaAppearance, Text.MicaAppearanceDescription),
        new(AppearanceStyleIds.Acrylic, Text.AcrylicAppearance, Text.AcrylicAppearanceDescription)
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
                OnPropertyChanged(nameof(Text));
                OnPropertyChanged(nameof(AudioOutputModeOptions));
                OnPropertyChanged(nameof(OnlineQualityOptions));
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
                Save();
            }
        }
    }

    public bool LowEffects
    {
        get => _lowEffects;
        set
        {
            if (SetProperty(ref _lowEffects, value))
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

    public string WindowsDsdDevice
    {
        get => _windowsDsdDevice;
        set
        {
            if (SetProperty(ref _windowsDsdDevice, value))
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
    public string MigrationSource { get; }
    public string MigrationStatus { get; }
    public ObservableCollection<WindowsDsdDeviceModel> WindowsDsdDevices { get; } = new();
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
    public bool IsRefreshingDsdDevices
    {
        get => _isRefreshingDsdDevices;
        private set => SetProperty(ref _isRefreshingDsdDevices, value);
    }

    public string WindowsDsdStatus => !_playbackService.WindowsDsdAvailable
        ? "BASS DSD runtime unavailable"
        : WindowsDsdDevices.Count <= 1
            ? "Runtime ready · no ASIO device detected"
            : $"Runtime ready · {WindowsDsdDevices.Count - 1} ASIO device(s)";

    public string? WindowsDsdOutputStatus => _playbackService.WindowsDsdOutputModeLabel;
    public string? WindowsDsdActiveDevice => _playbackService.WindowsDsdActiveDeviceName;
    public string? WindowsDsdFallbackReason => _playbackService.WindowsDsdFallbackReason;

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

    [RelayCommand]
    private async Task RefreshWindowsDsdDevicesAsync()
    {
        if (IsRefreshingDsdDevices)
        {
            return;
        }

        IsRefreshingDsdDevices = true;
        try
        {
            await _playbackService.RefreshWindowsDsdDevicesAsync();
            WindowsDsdDevices.Clear();
            foreach (var device in _playbackService.WindowsDsdDevices)
            {
                WindowsDsdDevices.Add(device);
            }

            if (!WindowsDsdDevices.Any(device => device.Id == WindowsDsdDevice))
            {
                WindowsDsdDevice = "auto";
            }

            RefreshPlaybackStatus();
        }
        finally
        {
            IsRefreshingDsdDevices = false;
        }
    }

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
            LowEffects = LowEffects,
            AppearanceStyle = AppearanceStyle,
            AudioOutputMode = AudioOutputMode,
            AudioOutputDevice = AudioOutputDevice,
            WindowsDsdDevice = WindowsDsdDevice,
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
        OnPropertyChanged(nameof(WindowsDsdStatus));
        OnPropertyChanged(nameof(WindowsDsdOutputStatus));
        OnPropertyChanged(nameof(WindowsDsdActiveDevice));
        OnPropertyChanged(nameof(WindowsDsdFallbackReason));
        OnPropertyChanged(nameof(ActiveAudioOutputMode));
        OnPropertyChanged(nameof(AudioOutputFallbackReason));
    }

    private void RefreshOnlineAccountAvailability()
    {
        OnlineAccounts.IsLoginEnabled = true;
    }
}
