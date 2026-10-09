namespace PrismWave_WinUI.ViewModels.Library;

public sealed record LibraryFolderEntryViewModel(
    string Path,
    int TrackCount,
    bool IsAvailable,
    string StatusText,
    string? Error);
