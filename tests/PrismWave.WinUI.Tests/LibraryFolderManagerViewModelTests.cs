using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Library;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class LibraryFolderManagerViewModelTests
{
    [Fact]
    public async Task SelectedFolder_IsAddedToLibrary()
    {
        var library = new FakeLibraryService();
        var viewModel = new LibraryFolderManagerViewModel(
            library,
            new FakePicker(MusicFolderPickResult.Selected(@"C:\Music")));

        await viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Music", library.AddedFolder);
    }

    [Fact]
    public async Task CanceledPicker_DoesNotCallLibrary()
    {
        var library = new FakeLibraryService();
        var viewModel = new LibraryFolderManagerViewModel(
            library,
            new FakePicker(MusicFolderPickResult.Canceled()));

        await viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Null(library.AddedFolder);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task FailedPicker_PublishesReadableError()
    {
        var library = new FakeLibraryService();
        var viewModel = new LibraryFolderManagerViewModel(
            library,
            new FakePicker(MusicFolderPickResult.Failed("Picker failed")));

        await viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal("Picker failed", viewModel.Error);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public async Task RemoveAndRescanCommands_ForwardToLibrary()
    {
        var library = new FakeLibraryService();
        var viewModel = new LibraryFolderManagerViewModel(
            library,
            new FakePicker(MusicFolderPickResult.Canceled()));

        await viewModel.RemoveFolderCommand.ExecuteAsync(@"C:\Music");
        await viewModel.RescanCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Music", library.RemovedFolder);
        Assert.Equal(1, library.RescanCount);
    }

    [Fact]
    public void LibraryChanged_RefreshesFoldersAndProgress()
    {
        var library = new FakeLibraryService();
        var viewModel = new LibraryFolderManagerViewModel(
            library,
            new FakePicker(MusicFolderPickResult.Canceled()));
        library.SetState(
            [new LibraryFolderStatus(@"C:\Music", true, null)],
            new LibraryScanProgress(4, LibraryScanPhase.ReadingMetadata, 10, 3, @"C:\Music\song.flac"),
            true,
            null);

        library.RaiseChanged();

        Assert.Equal(@"C:\Music", Assert.Single(viewModel.Folders).Path);
        Assert.True(viewModel.IsScanning);
        Assert.Equal("Scanning 3 / 10", viewModel.StatusText);
    }

    private sealed class FakePicker(MusicFolderPickResult result) : IMusicFolderPicker
    {
        public Task<MusicFolderPickResult> PickAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeLibraryService : ILibraryService
    {
        private IReadOnlyList<LibraryFolderStatus> _folderStatuses = [];

        public IReadOnlyList<TrackModel> Tracks => [];
        public IReadOnlyList<string> Folders => _folderStatuses.Select(status => status.Path).ToList();
        public IReadOnlyList<AlbumModel> Albums => [];
        public IReadOnlyList<ArtistModel> Artists => [];
        public IReadOnlyList<TrackModel> Favorites => [];
        public IReadOnlyList<LibraryFolderStatus> FolderStatuses => _folderStatuses;
        public LibraryScanProgress ScanProgress { get; private set; } = LibraryScanProgress.Idle;
        public bool IsScanning { get; private set; }
        public string? Error { get; private set; }
        public string? AddedFolder { get; private set; }
        public string? RemovedFolder { get; private set; }
        public int RescanCount { get; private set; }
        public event EventHandler? LibraryChanged;

        public void SetState(
            IReadOnlyList<LibraryFolderStatus> statuses,
            LibraryScanProgress progress,
            bool isScanning,
            string? error)
        {
            _folderStatuses = statuses;
            ScanProgress = progress;
            IsScanning = isScanning;
            Error = error;
        }

        public void RaiseChanged() => LibraryChanged?.Invoke(this, EventArgs.Empty);

        public Task AddFolderAsync(string folder)
        {
            AddedFolder = folder;
            return Task.CompletedTask;
        }

        public Task AddFolderAsync(string folder, CancellationToken cancellationToken)
        {
            AddedFolder = folder;
            return Task.CompletedTask;
        }

        public Task RemoveFolderAsync(string folder)
        {
            RemovedFolder = folder;
            return Task.CompletedTask;
        }

        public Task RemoveFolderAsync(string folder, CancellationToken cancellationToken)
        {
            RemovedFolder = folder;
            return Task.CompletedTask;
        }

        public Task RescanAsync()
        {
            RescanCount++;
            return Task.CompletedTask;
        }

        public Task RescanAsync(CancellationToken cancellationToken)
        {
            RescanCount++;
            return Task.CompletedTask;
        }

        public Task ToggleFavoriteAsync(TrackModel track) => Task.CompletedTask;
        public Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile) => Task.CompletedTask;
        public IReadOnlyList<TrackModel> GetAlbumTracks(string albumId) => [];
        public IReadOnlyList<TrackModel> GetArtistTracks(string artistName) => [];
    }
}
