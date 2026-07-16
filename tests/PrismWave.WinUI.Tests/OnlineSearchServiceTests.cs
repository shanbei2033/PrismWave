using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlineSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_PrefersLocalAndSuppressesMatchingOnlineDuplicate()
    {
        var local = new TrackModel(
            "local-song",
            @"C:\Music\Song.flac",
            "Song",
            "Artist",
            "Local Album",
            "02:00",
            @"C:\Covers\Song.jpg");
        var library = new FakeLibraryService(new[] { local });
        var online = new FakeProviderService(new[]
        {
            new OnlineProviderTrackModel(
                "netease",
                "123",
                "Song",
                "Artist",
                "Online Album",
                120,
                "https://cover.test/duplicate.jpg"),
            new OnlineProviderTrackModel(
                "qq",
                "qq-2",
                "Another Song",
                "Other Artist",
                "Online Album",
                180,
                "https://cover.test/other.jpg")
        });
        var service = new OnlineSearchService(library, online);

        var results = await service.SearchAsync("Song");

        Assert.Equal(2, results.Count);
        Assert.True(results[0].IsLocal);
        Assert.Equal(local.Path, results[0].Source);
        Assert.Equal("QQ Music", results[1].Provider);
        Assert.Equal("online://qq/qq-2", results[1].Source);
        Assert.DoesNotContain(results, result => result.Provider == "NetEase");
    }

    private sealed class FakeProviderService(IReadOnlyList<OnlineProviderTrackModel> results) : IOnlineProviderService
    {
        public IReadOnlyList<string> SearchProviders { get; } = Array.Empty<string>();

        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(results);
        }

        public Task<OnlinePlaybackResolution?> ResolveAsync(
            string provider,
            string providerTrackId,
            string? coverUrl = null,
            double durationSeconds = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<OnlinePlaybackResolution?> SearchAndResolveAsync(
            TrackModel track,
            string? preferredProvider = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void InvalidatePlaybackUrl(string playbackUrl)
        {
        }
    }

    private sealed class FakeLibraryService(IReadOnlyList<TrackModel> tracks) : ILibraryService
    {
        public IReadOnlyList<TrackModel> Tracks { get; } = tracks;
        public IReadOnlyList<string> Folders { get; } = Array.Empty<string>();
        public IReadOnlyList<AlbumModel> Albums { get; } = Array.Empty<AlbumModel>();
        public IReadOnlyList<ArtistModel> Artists { get; } = Array.Empty<ArtistModel>();
        public IReadOnlyList<TrackModel> Favorites { get; } = Array.Empty<TrackModel>();
        public bool IsScanning => false;
        public string? Error => null;
        public event EventHandler? LibraryChanged
        {
            add { }
            remove { }
        }

        public Task AddFolderAsync(string folder) => Task.CompletedTask;
        public Task RemoveFolderAsync(string folder) => Task.CompletedTask;
        public Task RescanAsync() => Task.CompletedTask;
        public Task ToggleFavoriteAsync(TrackModel track) => Task.CompletedTask;
        public Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile) => Task.CompletedTask;
        public IReadOnlyList<TrackModel> GetAlbumTracks(string albumId) => Array.Empty<TrackModel>();
        public IReadOnlyList<TrackModel> GetArtistTracks(string artistName) => Array.Empty<TrackModel>();
    }
}
