using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Home;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class HomeViewModelPlaybackTests
{
    [Fact]
    public void PlayHomeTrack_UsesSameQueueIdentityAsPlaySelectedPlaylist()
    {
        var track = CreateRockTrack();
        var section = new HomeSectionModel("style-rock", "摇滚", string.Empty, new[] { track });
        var playback = new FakePlaybackService();
        var viewModel = new HomeViewModel(new FakeOnlineHomeService(section), playback);
        viewModel.SelectHomeSectionCommand.Execute(section);

        viewModel.PlaySelectedPlaylistCommand.Execute(null);
        playback.IsPlaying = false;
        playback.Status = PlaybackStatus.Idle;
        viewModel.PlayHomeTrackCommand.Execute(track);

        Assert.Equal(2, playback.PlayedTracks.Count);
        Assert.Equal("section-style-rock-0", playback.PlayedTracks[0].Id);
        Assert.Equal(playback.PlayedTracks[0].Id, playback.PlayedTracks[1].Id);
    }

    [Fact]
    public void PlayHomeTrack_DoesNotReloadCurrentlyPlayingQueueTrack()
    {
        var track = CreateRockTrack();
        var section = new HomeSectionModel("style-rock", "摇滚", string.Empty, new[] { track });
        var playback = new FakePlaybackService();
        var viewModel = new HomeViewModel(new FakeOnlineHomeService(section), playback);
        viewModel.SelectHomeSectionCommand.Execute(section);

        viewModel.PlaySelectedPlaylistCommand.Execute(null);
        viewModel.PlayHomeTrackCommand.Execute(track);

        Assert.Single(playback.PlayedTracks);
        Assert.Equal("section-style-rock-0", playback.CurrentTrack?.Id);
        Assert.True(playback.IsPlaying);
    }

    [Fact]
    public void PlayHomeTrack_PrefersSelectedSectionWhenEqualTrackAppearsElsewhere()
    {
        var globalTrack = CreateRockTrack();
        var rockTrack = CreateRockTrack();
        var globalSection = new HomeSectionModel(
            "global-hot", "全球热门", string.Empty, new[] { globalTrack });
        var rockSection = new HomeSectionModel(
            "style-rock", "摇滚", string.Empty, new[] { rockTrack });
        var playback = new FakePlaybackService();
        var home = new FakeOnlineHomeService(
            globalSection,
            new[] { globalSection, rockSection });
        var viewModel = new HomeViewModel(home, playback);
        viewModel.SelectHomeSectionCommand.Execute(rockSection);

        viewModel.PlayHomeTrackCommand.Execute(rockTrack);

        var played = Assert.Single(playback.PlayedTracks);
        Assert.Equal("section-style-rock-0", played.Id);
    }

    [Fact]
    public void PlaybackCover_SynchronizesSameTitleAndArtistAcrossHomePlaylists()
    {
        var globalTrack = CreateRockTrack() with { CoverUrl = "https://cover.test/global.jpg" };
        var rockTrack = CreateRockTrack() with { CoverUrl = "https://cover.test/rock.jpg" };
        var otherArtist = CreateRockTrack() with
        {
            Artist = "Another Artist",
            CoverUrl = "https://cover.test/other.jpg"
        };
        var globalSection = new HomeSectionModel(
            "global-hot", "全球热门", string.Empty, new[] { globalTrack, otherArtist });
        var rockSection = new HomeSectionModel(
            "style-rock", "摇滚", string.Empty, new[] { rockTrack });
        var home = new FakeOnlineHomeService(
            globalSection,
            new[] { globalSection, rockSection });
        var playback = new FakePlaybackService();
        var cover = new FakeCoverService("https://cover.test/bar.jpg");
        var viewModel = CreateViewModel(home, playback, cover);
        viewModel.SelectHomeSectionCommand.Execute(rockSection);
        viewModel.SelectedAlbumTracks.Add(
            CreateRockTrack() with { CoverUrl = "https://cover.test/album.jpg" });

        playback.PublishState(CreateResolvedRockTrack(isRemote: true));

        Assert.Equal("https://cover.test/bar.jpg", viewModel.TopPlaylist.Tracks[0].CoverUrl);
        Assert.Equal(
            "https://cover.test/bar.jpg",
            viewModel.Sections.Single(section => section.Id == "style-rock").Tracks[0].CoverUrl);
        Assert.Equal("https://cover.test/bar.jpg", viewModel.SelectedPlaylist.Tracks[0].CoverUrl);
        Assert.Equal("https://cover.test/bar.jpg", viewModel.GlobalTrendingTracks[0].CoverUrl);
        Assert.Equal(
            "https://cover.test/bar.jpg",
            viewModel.GenreSections.Single(section => section.Id == "style-rock").Tracks[0].CoverUrl);
        Assert.Equal("https://cover.test/bar.jpg", viewModel.SelectedAlbumTracks[0].CoverUrl);
        Assert.Equal("https://cover.test/other.jpg", viewModel.TopPlaylist.Tracks[1].CoverUrl);
    }

    [Fact]
    public void PlaybackCover_IsReappliedAfterHomeRefresh()
    {
        var track = CreateRockTrack();
        var section = new HomeSectionModel("style-rock", "摇滚", string.Empty, new[] { track });
        var home = new FakeOnlineHomeService(section);
        var playback = new FakePlaybackService();
        var cover = new FakeCoverService("https://cover.test/bar.jpg");
        var viewModel = CreateViewModel(home, playback, cover);

        playback.PublishState(CreateResolvedRockTrack(isRemote: true));
        home.ReplaceCatalog(section, new[] { section });

        Assert.Equal("https://cover.test/bar.jpg", viewModel.TopPlaylist.Tracks[0].CoverUrl);
        Assert.Equal("https://cover.test/bar.jpg", viewModel.Sections[0].Tracks[0].CoverUrl);
    }

    [Fact]
    public void CoverChangeEvent_UpdatesMatchingHomeTracksWithoutCurrentPlaybackIdentity()
    {
        var track = CreateRockTrack();
        var section = new HomeSectionModel("style-rock", "摇滚", string.Empty, new[] { track });
        var playback = new FakePlaybackService();
        var cover = new FakeCoverService("https://cover.test/custom.jpg");
        var viewModel = CreateViewModel(new FakeOnlineHomeService(section), playback, cover);

        cover.RaiseChanged(new CoverChangedEventArgs(
            "another-id",
            "online://another/path",
            "https://cover.test/custom.jpg",
            track.Title,
            track.Artist));

        Assert.Equal("https://cover.test/custom.jpg", viewModel.TopPlaylist.Tracks[0].CoverUrl);
        Assert.Equal("https://cover.test/custom.jpg", viewModel.Sections[0].Tracks[0].CoverUrl);
    }

    [Fact]
    public void LocalPlayback_DoesNotOverrideHomePlaylistCover()
    {
        var track = CreateRockTrack();
        var section = new HomeSectionModel("style-rock", "摇滚", string.Empty, new[] { track });
        var playback = new FakePlaybackService();
        var cover = new FakeCoverService("https://cover.test/local-selected.jpg");
        var viewModel = CreateViewModel(new FakeOnlineHomeService(section), playback, cover);

        playback.PublishState(CreateResolvedRockTrack(isRemote: false));

        Assert.Equal("https://cover.test/rock.jpg", viewModel.TopPlaylist.Tracks[0].CoverUrl);
    }

    private static HomeViewModel CreateViewModel(
        IOnlineHomeService home,
        IPlaybackService playback,
        ICoverService cover)
    {
        var constructor = typeof(HomeViewModel).GetConstructor(new[]
        {
            typeof(IOnlineHomeService),
            typeof(IPlaybackService),
            typeof(ICoverService)
        });
        Assert.NotNull(constructor);
        return (HomeViewModel)constructor.Invoke(new object[] { home, playback, cover });
    }

    private static TrackModel CreateResolvedRockTrack(bool isRemote) => new(
        "section-style-rock-0",
        isRemote ? "online://online/Mr.%20Brightside" : "C:\\Music\\Mr Brightside.flac",
        "  Mr.   Brightside ",
        "the killers",
        "Direct Hits",
        "03:43",
        "https://cover.test/provider.jpg",
        isRemote,
        isRemote ? "netease" : "Local",
        isRemote ? "https://audio.test/mr-brightside.flac" : null);

    private static HomeTrackModel CreateRockTrack() => new(
        "Mr. Brightside",
        "The Killers",
        "Direct Hits",
        "03:43",
        "Online",
        "https://cover.test/rock.jpg");

    private sealed class FakeOnlineHomeService : IOnlineHomeService
    {
        public FakeOnlineHomeService(HomeSectionModel section)
            : this(section, new[] { section })
        {
        }

        public FakeOnlineHomeService(
            HomeSectionModel topPlaylist,
            IReadOnlyList<HomeSectionModel> sections)
        {
            TopPlaylist = topPlaylist;
            Sections = sections;
        }

        public HomeSectionModel TopPlaylist { get; private set; }
        public IReadOnlyList<HomeSectionModel> Sections { get; private set; }
        public IReadOnlyList<AlbumModel> Albums { get; } = Array.Empty<AlbumModel>();
        public DateTimeOffset GeneratedAt { get; } = DateTimeOffset.UtcNow;
        public bool RecommendationsUnavailable => false;
        public bool RecommendationsPendingGeneration => false;
        public bool IsRefreshing => false;
        public string? Error => null;
        public string? SourceDescription => "test";
        public event EventHandler? HomeChanged;

        public void ReplaceCatalog(
            HomeSectionModel topPlaylist,
            IReadOnlyList<HomeSectionModel> sections)
        {
            TopPlaylist = topPlaylist;
            Sections = sections;
            HomeChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<HomeTrackModel>> LoadAlbumTracksAsync(
            string albumId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HomeTrackModel>>(Array.Empty<HomeTrackModel>());
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public List<TrackModel> PlayedTracks { get; } = new();
        public TrackModel? CurrentTrack { get; private set; }
        public IReadOnlyList<TrackModel> Queue { get; private set; } = Array.Empty<TrackModel>();
        public PlaybackMode Mode => PlaybackMode.Loop;
        public PlaybackStatus Status { get; set; } = PlaybackStatus.Idle;
        public double Volume => 0.78;
        public double PositionSeconds => 0;
        public double DurationSeconds => 0;
        public bool IsLoading => false;
        public bool IsPlaying { get; set; }
        public string? Error => null;
        public event EventHandler? StateChanged;

        public void Play(TrackModel track, IReadOnlyList<TrackModel>? queue = null)
        {
            PlayedTracks.Add(track);
            CurrentTrack = track;
            Queue = queue ?? new[] { track };
            Status = PlaybackStatus.Playing;
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void PublishState(TrackModel track)
        {
            CurrentTrack = track;
            Queue = new[] { track };
            Status = PlaybackStatus.Playing;
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stop() { }
        public void TogglePlayPause() { }
        public void Next() { }
        public void Previous() { }
        public void CycleMode() { }
        public void SetVolume(double volume) { }
        public void Seek(double seconds) { }
        public void PlayFromQueue(TrackModel track) => Play(track, Queue);
        public void ReorderQueue(IReadOnlyList<TrackModel> tracks) => Queue = tracks;
        public void RemoveFromQueue(TrackModel track) { }
        public void ClearQueue() => Queue = Array.Empty<TrackModel>();
    }

    private sealed class FakeCoverService(string? resolvedCoverPath) : ICoverService
    {
        public event EventHandler<CoverChangedEventArgs>? CoverChanged;

        public void RaiseChanged(CoverChangedEventArgs args) => CoverChanged?.Invoke(this, args);

        public string? ResolveCoverPath(TrackModel track) => resolvedCoverPath ?? track.CoverPath;

        public Task<IReadOnlyList<CoverSearchResultModel>> SearchOnlineCoversAsync(
            TrackModel track,
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CoverSearchResultModel>>(Array.Empty<CoverSearchResultModel>());

        public Task<string> ApplyOnlineCoverAsync(
            TrackModel track,
            CoverSearchResultModel result,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result.FullImageUrl);
    }
}
