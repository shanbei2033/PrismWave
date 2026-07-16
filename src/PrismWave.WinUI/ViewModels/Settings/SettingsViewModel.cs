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
    private string _language = "zh-CN";
    private bool _experimentalFeaturesEnabled;
    private bool _onlineModeEnabled = true;
    private OnlineQualityPreference _onlineQualityPreference = OnlineQualityPreference.Lossless;
    private bool _lowEffects;
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
        IOnlineAccountService onlineAccountService)
    {
        _settingsService = settingsService;
        _developerLogService = developerLogService;
        _playbackService = playbackService;
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
        ThemeName = themeService.ThemeName;
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
    public IReadOnlyList<AudioOutputModeOptionModel> AudioOutputModeOptions { get; } =
        AudioOutputPolicy.Options;
    public IReadOnlyList<OnlineQualityPreference> OnlineQualityOptions { get; } =
        [OnlineQualityPreference.Lossless, OnlineQualityPreference.High, OnlineQualityPreference.Standard];
    public OnlineAccountSettingsViewModel OnlineAccounts { get; }
    public LibraryFolderManagerViewModel LibraryFolders { get; }

    public string Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, value))
            {
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

    public string ThemeName { get; }

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
            AudioOutputMode = AudioOutputMode,
            AudioOutputDevice = AudioOutputDevice,
            WindowsDsdDevice = WindowsDsdDevice,
            FadeEnabled = FadeEnabled,
            FadeDurationMs = FadeDurationMs
        });
    }

    private void RefreshLogs()
    {
        var lines = _developerLogService.Lines;
        DeveloperLogText = string.Join(Environment.NewLine, lines.TakeLast(500));
        DeveloperLogCount = $"{lines.Count} entries";
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
