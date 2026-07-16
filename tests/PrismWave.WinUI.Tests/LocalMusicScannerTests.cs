using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Implementations;
using PrismWave_WinUI.Tests.TestSupport;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class LocalMusicScannerTests
{
    [Fact]
    public async Task EmptyFolderList_ReturnsNoSyntheticTracks()
    {
        var result = await CreateScanner().ScanAsync([], EmptyCovers, null, CancellationToken.None);

        Assert.Empty(result.Tracks);
        Assert.Empty(result.FolderStatuses);
        Assert.Null(result.FatalError);
    }

    [Fact]
    public async Task RecursiveScan_FindsSupportedAudioFilesOnly()
    {
        using var library = new TemporaryMusicLibrary();
        var first = library.CreateWave("first.wav");
        var second = library.CreateWave("nested/second.wav");
        library.CreateFile("nested/readme.txt");

        var result = await CreateScanner().ScanAsync([library.Root], EmptyCovers, null, CancellationToken.None);

        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal(
            new[] { Path.GetFullPath(first), Path.GetFullPath(second) }.OrderBy(path => path),
            result.Tracks.Select(track => track.Path).OrderBy(path => path));
    }

    [Fact]
    public async Task OverlappingRoots_DoNotDuplicateTracks()
    {
        using var library = new TemporaryMusicLibrary();
        var nested = library.CreateDirectory("nested");
        var file = library.CreateWave("nested/song.wav");

        var result = await CreateScanner().ScanAsync([library.Root, nested], EmptyCovers, null, CancellationToken.None);

        var track = Assert.Single(result.Tracks);
        Assert.Equal(Path.GetFullPath(file), track.Path);
    }

    [Fact]
    public async Task ValidAndUnavailableRoots_ReturnTracksAndFolderStatuses()
    {
        using var library = new TemporaryMusicLibrary();
        library.CreateWave("song.wav");
        var missing = Path.Combine(library.Root, "missing");

        var result = await CreateScanner().ScanAsync([library.Root, missing], EmptyCovers, null, CancellationToken.None);

        Assert.Single(result.Tracks);
        Assert.Contains(result.FolderStatuses, status => status.Path == library.Root && status.IsAvailable);
        Assert.Contains(result.FolderStatuses, status => status.Path == missing && !status.IsAvailable);
        Assert.Null(result.FatalError);
    }

    [Fact]
    public async Task AllUnavailableRoots_ReturnFatalError()
    {
        using var library = new TemporaryMusicLibrary();
        var first = Path.Combine(library.Root, "missing-one");
        var second = Path.Combine(library.Root, "missing-two");

        var result = await CreateScanner().ScanAsync([first, second], EmptyCovers, null, CancellationToken.None);

        Assert.Empty(result.Tracks);
        Assert.Equal(2, result.FolderStatuses.Count);
        Assert.All(result.FolderStatuses, status => Assert.False(status.IsAvailable));
        Assert.NotNull(result.FatalError);
    }

    [Fact]
    public async Task WavInfoMetadata_IsUsedForTitleArtistAndAlbum()
    {
        using var library = new TemporaryMusicLibrary();
        library.CreateWave("metadata.wav", "Real title", "Real artist", "Real album");

        var result = await CreateScanner().ScanAsync([library.Root], EmptyCovers, null, CancellationToken.None);

        var track = Assert.Single(result.Tracks);
        Assert.Equal("Real title", track.Title);
        Assert.Equal("Real artist", track.Artist);
        Assert.Equal("Real album", track.Album);
    }

    [Fact]
    public async Task CorruptSupportedFile_UsesRealFileNameFallback()
    {
        using var library = new TemporaryMusicLibrary();
        var file = library.CreateFile("Actual file.mp3");

        var result = await CreateScanner().ScanAsync([library.Root], EmptyCovers, null, CancellationToken.None);

        var track = Assert.Single(result.Tracks);
        Assert.Equal("Actual file", track.Title);
        Assert.Equal(Path.GetFullPath(file), track.PlaybackSource);
    }

    [Fact]
    public async Task MetadataFailure_DoesNotAbortOtherFiles()
    {
        using var library = new TemporaryMusicLibrary();
        library.CreateFile("broken.mp3", [0x00, 0x01, 0x02]);
        library.CreateWave("valid.wav", "Valid title", "Valid artist", "Valid album");

        var result = await CreateScanner().ScanAsync([library.Root], EmptyCovers, null, CancellationToken.None);

        Assert.Equal(2, result.Tracks.Count);
        Assert.Contains(result.Tracks, track => track.Title == "broken");
        Assert.Contains(result.Tracks, track => track.Title == "Valid title");
        Assert.Null(result.FatalError);
    }

    [Fact]
    public async Task SidecarCover_IsApplied()
    {
        using var library = new TemporaryMusicLibrary();
        library.CreateWave("album/song.wav");
        var cover = library.CreateFile("album/cover.jpg", [0xff, 0xd8, 0xff, 0xd9]);

        var result = await CreateScanner().ScanAsync([library.Root], EmptyCovers, null, CancellationToken.None);

        Assert.Equal(Path.GetFullPath(cover), Assert.Single(result.Tracks).CoverPath);
    }

    [Fact]
    public async Task CustomCover_TakesPriorityOverSidecar()
    {
        using var library = new TemporaryMusicLibrary();
        var song = library.CreateWave("album/song.wav");
        library.CreateFile("album/cover.jpg", [0xff, 0xd8, 0xff, 0xd9]);
        var custom = library.CreateFile("custom.png", [0x89, 0x50, 0x4e, 0x47]);
        var covers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(song)] = Path.GetFullPath(custom)
        };

        var result = await CreateScanner().ScanAsync([library.Root], covers, null, CancellationToken.None);

        Assert.Equal(Path.GetFullPath(custom), Assert.Single(result.Tracks).CoverPath);
    }

    [Fact]
    public async Task Cancellation_StopsScan()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateScanner().ScanAsync([], EmptyCovers, null, cancellation.Token));
    }

    private static LocalMusicScanner CreateScanner() => new();

    private static IReadOnlyDictionary<string, string> EmptyCovers { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
