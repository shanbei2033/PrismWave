using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Library;

public sealed partial class LibraryFolderManagerViewModel : ObservableObject
{
    private readonly ILibraryService _libraryService;
    private readonly IMusicFolderPicker _folderPicker;
    private bool _isScanning;
    private LibraryScanProgress _scanProgress = LibraryScanProgress.Idle;
    private string? _error;
    private string? _interactionError;

    public LibraryFolderManagerViewModel(
        ILibraryService libraryService,
        IMusicFolderPicker folderPicker)
    {
        _libraryService = libraryService;
        _folderPicker = folderPicker;
        _libraryService.LibraryChanged += LibraryService_LibraryChanged;
        Refresh();
    }

    public ObservableCollection<LibraryFolderStatus> Folders { get; } = new();

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
        LibraryScanPhase.Enumerating => $"Discovering music · {ScanProgress.DiscoveredFiles}",
        LibraryScanPhase.ReadingMetadata => $"Scanning {ScanProgress.ProcessedFiles} / {ScanProgress.DiscoveredFiles}",
        LibraryScanPhase.Completed when ScanProgress.ProcessedFiles > 0 => $"{ScanProgress.ProcessedFiles} tracks ready",
        LibraryScanPhase.Failed => "Scan failed",
        _ => Folders.Count == 0 ? "No music folders added" : "Ready"
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
                _interactionError = result.Error ?? "The music folder picker could not be opened.";
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

    private void Refresh()
    {
        Folders.Clear();
        foreach (var folder in _libraryService.FolderStatuses)
        {
            Folders.Add(folder);
        }

        IsScanning = _libraryService.IsScanning;
        ScanProgress = _libraryService.ScanProgress;
        Error = _interactionError ?? _libraryService.Error;
        OnPropertyChanged(nameof(StatusText));
    }
}
