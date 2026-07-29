using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlineSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_KeepsMatchingTracksFromDifferentSources()
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

        Assert.Equal(3, results.Count);
        Assert.True(results[0].IsLocal);
        Assert.Equal(local.Path, results[0].Source);
        Assert.Contains(results, result => result.Provider == "NetEase" && result.Source == "online://netease/123");
        Assert.Contains(results, result => result.Provider == "QQ Music" && result.Source == "online://qq/qq-2");
    }

    [Fact]
    public async Task SearchProviderAsync_RequestsOnlyTheSelectedProvider()
    {
        var library = new FakeLibraryService(Array.Empty<TrackModel>());
        var online = new FakeProviderService(new[]
        {
            new OnlineProviderTrackModel("netease", "1", "Song", "Artist", "Album", 120),
            new OnlineProviderTrackModel("qq", "2", "Song", "Artist", "Album", 121)
        });
        var service = new OnlineSearchService(library, online);

        var results = await service.SearchProviderAsync("Song", "qq");

        Assert.Equal("qq", online.LastDirectProvider);
        Assert.Empty(online.LastRequestedProviders);
        Assert.Single(results);
        Assert.Equal("QQ Music", results[0].Provider);
    }

    [Fact]
    public async Task SearchLocalAsync_DoesNotContactOnlineProviders()
    {
        var track = new TrackModel("1", @"C:\Music\Song.flac", "Song", "Artist", "Album", "02:00", null);
        var online = new FakeProviderService(Array.Empty<OnlineProviderTrackModel>());
        var service = new OnlineSearchService(new FakeLibraryService(new[] { track }), online);

        var results = await service.SearchLocalAsync("Song");

        Assert.Single(results);
        Assert.Empty(online.LastRequestedProviders);
    }

    private sealed class FakeProviderService(IReadOnlyList<OnlineProviderTrackModel> results) : IOnlineProviderService
    {
        public IReadOnlyList<string> SearchProviders { get; } = Array.Empty<string>();
        public IReadOnlyList<string> LastRequestedProviders { get; private set; } = Array.Empty<string>();
        public string? LastDirectProvider { get; private set; }

        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(results);
        }

        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchAsync(
            string query,
            IReadOnlyCollection<string> providers,
            CancellationToken cancellationToken = default)
        {
            LastRequestedProviders = providers.ToList();
            return Task.FromResult<IReadOnlyList<OnlineProviderTrackModel>>(
                results.Where(result => providers.Contains(result.Provider, StringComparer.OrdinalIgnoreCase)).ToList());
        }

        public Task<IReadOnlyList<OnlineProviderTrackModel>> SearchProviderAsync(
            string query,
            string provider,
            CancellationToken cancellationToken = default)
        {
            LastDirectProvider = provider;
            return Task.FromResult<IReadOnlyList<OnlineProviderTrackModel>>(
                results.Where(result => string.Equals(
                    result.Provider,
                    provider,
                    StringComparison.OrdinalIgnoreCase)).ToList());
        }

        public Task<OnlinePlaybackResolution?> ResolveAsync(
            string provider,
            string providerTrackId,
            string? coverUrl = null,
            double durationSeconds = 0,
            CancellationToken cancellationToken = default,
            bool requiresVip = false)
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

        public Task<string?> ResolveCoverFromDeezerAsync(
            string title,
            string artist,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
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
