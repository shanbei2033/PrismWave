using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Infrastructure.Library;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Library;

public sealed partial class LibraryFolderManagerViewModel : ObservableObject
{
    private readonly ILibraryService _libraryService;
    private readonly IMusicFolderPicker _folderPicker;
    private readonly ISettingsService? _settingsService;
    private bool _isScanning;
    private LibraryScanProgress _scanProgress = LibraryScanProgress.Idle;
    private string? _error;
    private string? _interactionError;

    public LibraryFolderManagerViewModel(
        ILibraryService libraryService,
        IMusicFolderPicker folderPicker,
        ISettingsService? settingsService = null)
    {
        _libraryService = libraryService;
        _folderPicker = folderPicker;
        _settingsService = settingsService;
        _libraryService.LibraryChanged += LibraryService_LibraryChanged;
        if (_settingsService is not null)
        {
            _settingsService.SettingsChanged += SettingsService_SettingsChanged;
        }

        Refresh();
    }

    public ObservableCollection<LibraryFolderStatus> Folders { get; } = new();
    public ObservableCollection<LibraryFolderEntryViewModel> FolderEntries { get; } = new();

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    public LibraryScanProgress ScanProgress
    {
        get => _scanProgress;
        private set
        {
            if (SetProperty(ref _scanProgress, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
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

    public string StatusText => ScanProgress.Phase switch
    {
        LibraryScanPhase.Enumerating => $"{Localize("Discovering music", "正在查找音乐", "正在尋找音樂")} · {ScanProgress.DiscoveredFiles}",
        LibraryScanPhase.ReadingMetadata => $"{Localize("Scanning", "正在扫描", "正在掃描")} {ScanProgress.ProcessedFiles} / {ScanProgress.DiscoveredFiles}",
        LibraryScanPhase.Completed when ScanProgress.ProcessedFiles > 0 => Localize(
            $"{ScanProgress.ProcessedFiles} tracks ready",
            $"{ScanProgress.ProcessedFiles} 首歌曲已就绪",
            $"{ScanProgress.ProcessedFiles} 首歌曲已就緒"),
        LibraryScanPhase.Failed => Localize("Scan failed", "扫描失败", "掃描失敗"),
        _ => Folders.Count == 0
            ? Localize("No music folders added", "尚未添加音乐文件夹", "尚未新增音樂資料夾")
            : Localize("Ready", "就绪", "就緒")
    };

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task AddFolderAsync()
    {
        _interactionError = null;
        var result = await _folderPicker.PickAsync();
        switch (result.Status)
        {
            case MusicFolderPickStatus.Selected when !string.IsNullOrWhiteSpace(result.Path):
                await _libraryService.AddFolderAsync(result.Path);
                break;
            case MusicFolderPickStatus.Failed:
                _interactionError = result.Error ?? Localize(
                    "The music folder picker could not be opened.",
                    "无法打开音乐文件夹选择器。",
                    "無法開啟音樂資料夾選擇器。");
                break;
        }

        Refresh();
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(string folder)
    {
        _interactionError = null;
        await _libraryService.RemoveFolderAsync(folder);
        Refresh();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        _interactionError = null;
        await _libraryService.RescanAsync();
        Refresh();
    }

    private void LibraryService_LibraryChanged(object? sender, EventArgs e) => Refresh();

    private void SettingsService_SettingsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        Folders.Clear();
        FolderEntries.Clear();
        foreach (var folder in _libraryService.FolderStatuses)
        {
            Folders.Add(folder);
            FolderEntries.Add(new LibraryFolderEntryViewModel(
                folder.Path,
                _libraryService.Tracks.Count(track => IsTrackWithinFolder(track, folder.Path)),
                folder.IsAvailable,
                folder.IsAvailable
                    ? Localize("Ready", "就绪", "就緒")
                    : Localize("Unavailable", "不可用", "無法使用"),
                folder.Error));
        }

        IsScanning = _libraryService.IsScanning;
        ScanProgress = _libraryService.ScanProgress;
        Error = _interactionError ?? _libraryService.Error;
        OnPropertyChanged(nameof(StatusText));
    }

    private string Localize(string english, string simplified, string traditional) =>
        _settingsService?.Current.Language switch
        {
            "zh-CN" => simplified,
            "zh-TW" => traditional,
            _ => english
        };

    private static bool IsTrackWithinFolder(TrackModel track, string folder)
    {
        if (track.IsRemote || string.IsNullOrWhiteSpace(track.Path))
        {
            return false;
        }

        var root = LibraryFolderPath.Normalize(folder, requireExisting: false);
        if (root is null)
        {
            return false;
        }

        var normalizedTrackPath = LibraryFolderPath.Normalize(track.Path, requireExisting: false);
        if (normalizedTrackPath is null)
        {
            return false;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return normalizedTrackPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
