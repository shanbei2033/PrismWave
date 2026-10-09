namespace PrismWave_WinUI.Models;

public enum LibraryScanPhase
{
    Idle,
    Enumerating,
    ReadingMetadata,
    Completed,
    Failed
}

public sealed record LibraryFolderStatus(string Path, bool IsAvailable, string? Error)
{
    public string StatusText => IsAvailable ? "Ready" : "Unavailable";
}

public sealed record LibraryScanProgress(
    long Revision,
    LibraryScanPhase Phase,
    int DiscoveredFiles,
    int ProcessedFiles,
    string? CurrentPath)
{
    public static LibraryScanProgress Idle { get; } = new(0, LibraryScanPhase.Idle, 0, 0, null);
}

public sealed record LibraryScanResult(
    IReadOnlyList<TrackModel> Tracks,
    IReadOnlyList<LibraryFolderStatus> FolderStatuses,
    IReadOnlyList<string> Warnings,
    string? FatalError);

public enum MusicFolderPickStatus
{
    Selected,
    Canceled,
    Failed
}

public sealed record MusicFolderPickResult(MusicFolderPickStatus Status, string? Path, string? Error)
{
    public static MusicFolderPickResult Selected(string path) => new(MusicFolderPickStatus.Selected, path, null);
    public static MusicFolderPickResult Canceled() => new(MusicFolderPickStatus.Canceled, null, null);
    public static MusicFolderPickResult Failed(string error) => new(MusicFolderPickStatus.Failed, null, error);
}
