using PrismWave_WinUI.Models;
using PrismWave_WinUI.Infrastructure.Navigation;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Player;
using PrismWave_WinUI.ViewModels.Shell;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class PlaybackViewModelLyricsTests
{
    [Fact]
    public async Task PlaybackProgress_UpdatesCurrentLineAndKaraokeProgress()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track) { PositionSeconds = 1.75 };
        var document = new LyricsDocumentModel(
            new[]
            {
                new LyricLineModel(1, "00:01", "HiYo", new[]
                {
                    new LyricSegmentModel(1, 1.5, "Hi"),
                    new LyricSegmentModel(1.5, 2, "Yo")
                }),
                new LyricLineModel(3, "00:03", "Next")
            },
            "local",
            "sidecar",
            true);
        var lyrics = new FakeLyricsService(document);
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 2);

        Assert.Equal(0, viewModel.CurrentLyricIndex);
        Assert.True(viewModel.Lyrics[0].IsCurrent);
        Assert.Equal(0.75, viewModel.Lyrics[0].WordProgress, 3);

        playback.PositionSeconds = 3.2;
        playback.RaiseStateChanged();

        Assert.Equal(1, viewModel.CurrentLyricIndex);
        Assert.False(viewModel.Lyrics[0].IsCurrent);
        Assert.True(viewModel.Lyrics[1].IsCurrent);
    }

    [Fact]
    public async Task HighFrequencyPresentation_PreventsRawPlaybackSampleFromReversingTheActiveLine()
    {
        var playback = new FakePlaybackService(CreateTrack()) { PositionSeconds = 9.96 };
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[]
            {
                new LyricLineModel(9, "00:09", "Previous"),
                new LyricLineModel(10, "00:10", "Current")
            },
            "online",
            "lrclib",
            true));
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 2);
        var begin = typeof(PlaybackViewModel).GetMethod("BeginLyricsPresentationUpdates");
        var end = typeof(PlaybackViewModel).GetMethod("EndLyricsPresentationUpdates");
        Assert.NotNull(begin);
        Assert.NotNull(end);

        begin.Invoke(viewModel, null);
        viewModel.UpdateLyricsPresentationPosition(10.02);
        playback.PositionSeconds = 9.98;
        playback.RaiseStateChanged();

        Assert.Equal(1, viewModel.CurrentLyricIndex);
        end.Invoke(viewModel, null);
        Assert.Equal(0, viewModel.CurrentLyricIndex);
    }

    [Fact]
    public async Task ExplicitLyricSeek_UsesRapidTransitionKind()
    {
        var playback = new FakePlaybackService(CreateTrack()) { PositionSeconds = 1 };
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[]
            {
                new LyricLineModel(1, "00:01", "First"),
                new LyricLineModel(8, "00:08", "Target")
            },
            "local",
            "sidecar",
            true));
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 2);

        viewModel.SeekToLyric(1);

        var transition = viewModel.Lyrics[1].GetType().GetProperty("TransitionKind")?.GetValue(viewModel.Lyrics[1]);
        Assert.NotNull(transition);
        Assert.Equal("Rapid", transition.ToString());
    }

    [Fact]
    public async Task WordSyncedProgress_DoesNotCountWhitespaceAsPaintableCharacters()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track) { PositionSeconds = 2 };
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[]
            {
                new LyricLineModel(1, "00:01", "Hi Yo", new[]
                {
                    new LyricSegmentModel(1, 2, "Hi "),
                    new LyricSegmentModel(2, 3, "Yo")
                })
            },
            "online",
            "qqmusic",
            true));

        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 1);

        Assert.Equal(0.5, viewModel.Lyrics[0].WordProgress, 3);
    }

    [Fact]
    public async Task LineSyncedLyrics_UseCurrentLineSpanForFallbackCharacterProgress()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track) { PositionSeconds = 1 };
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[]
            {
                new LyricLineModel(1, "00:01", "AB CD"),
                new LyricLineModel(5, "00:05", "Next")
            },
            "online",
            "lrclib",
            true));
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 2);
        var update = typeof(PlaybackViewModel).GetMethod("UpdateLyricsPresentationPosition");

        Assert.NotNull(update);
        update.Invoke(viewModel, new object[] { 3d });

        Assert.Equal(0, viewModel.CurrentLyricIndex);
        Assert.Equal(0.5, viewModel.Lyrics[0].WordProgress, 3);
    }

    [Fact]
    public async Task LastLineFallbackProgress_UsesThreeSecondDefaultSpan()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track) { PositionSeconds = 5 };
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[]
            {
                new LyricLineModel(1, "00:01", "First"),
                new LyricLineModel(5, "00:05", "Last")
            },
            "online",
            "lrclib",
            true));
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 2);
        var update = typeof(PlaybackViewModel).GetMethod("UpdateLyricsPresentationPosition");

        Assert.NotNull(update);
        update.Invoke(viewModel, new object[] { 6.5d });

        Assert.Equal(1, viewModel.CurrentLyricIndex);
        Assert.Equal(0.5, viewModel.Lyrics[1].WordProgress, 3);
    }

    [Fact]
    public async Task AutoLineSyncedLyrics_UpgradeToWordSyncedDocumentInBackground()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track);
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[] { new LyricLineModel(0, "00:00", "Line synced") },
            "online",
            "lrclib",
            true))
        {
            WordSyncedUpgrade = new LyricsDocumentModel(
                new[]
                {
                    new LyricLineModel(0, "00:00", "Word synced", new[]
                    {
                        new LyricSegmentModel(0, 1, "Word "),
                        new LyricSegmentModel(1, 2, "synced")
                    })
                },
                "online",
                "qqmusic",
                true)
        };

        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.LyricsProvider == "qqmusic");

        Assert.True(lyrics.UpgradeRequested);
        Assert.Equal("Word synced", Assert.Single(viewModel.Lyrics).Text);
        Assert.NotEmpty(viewModel.Lyrics[0].Segments);
    }

    [Fact]
    public async Task ManualLineSyncedLyrics_DoNotRequestAutomaticWordUpgrade()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track);
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[] { new LyricLineModel(0, "00:00", "Pinned line") },
            "online",
            "lrclib",
            true,
            SelectionKind: LyricsSelectionKind.Manual));

        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 1);
        await Task.Delay(25);

        Assert.False(lyrics.UpgradeRequested);
        Assert.Equal("Pinned line", Assert.Single(viewModel.Lyrics).Text);
    }

    [Fact]
    public async Task LocalLineSyncedLyrics_DoNotRequestOnlineWordUpgrade()
    {
        var track = CreateTrack();
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[] { new LyricLineModel(0, "00:00", "Local sidecar") },
            "local",
            "sidecar",
            true));

        var viewModel = new PlaybackViewModel(new FakePlaybackService(track), lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 1);
        await Task.Delay(25);

        Assert.False(lyrics.UpgradeRequested);
    }

    [Fact]
    public void LyricsTimeline_UsesStableBoundarySelection()
    {
        var type = typeof(PlaybackViewModel).Assembly.GetType(
            "PrismWave_WinUI.ViewModels.Player.LyricsTimeline");
        Assert.NotNull(type);
        var method = type.GetMethod(
            "FindActiveIndex",
            new[] { typeof(IReadOnlyList<double>), typeof(double) });
        Assert.NotNull(method);
        IReadOnlyList<double> starts = new[] { 1d, 3d, 7d, 12d };

        Assert.Equal(0, method.Invoke(null, new object[] { starts, 0d }));
        Assert.Equal(0, method.Invoke(null, new object[] { starts, 2.999d }));
        Assert.Equal(1, method.Invoke(null, new object[] { starts, 3d }));
        Assert.Equal(3, method.Invoke(null, new object[] { starts, 99d }));
    }

    [Fact]
    public async Task ActiveLyric_IsTheOnlyFullyOpaqueLine()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track) { PositionSeconds = 7.1 };
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[]
            {
                new LyricLineModel(1, "00:01", "Far previous"),
                new LyricLineModel(3, "00:03", "Previous"),
                new LyricLineModel(7, "00:07", "Current"),
                new LyricLineModel(12, "00:12", "Next"),
                new LyricLineModel(18, "00:18", "Far next")
            },
            "local",
            "sidecar",
            true));
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 5);

        Assert.Equal(2, viewModel.CurrentLyricIndex);
        Assert.Single(viewModel.Lyrics, line => line.IsCurrent);
        Assert.Equal(1, viewModel.Lyrics[2].TextOpacity);
        Assert.True(viewModel.Lyrics[1].TextOpacity > viewModel.Lyrics[0].TextOpacity);
        Assert.True(viewModel.Lyrics[3].TextOpacity > viewModel.Lyrics[4].TextOpacity);
    }

    [Fact]
    public async Task ManualLyricsBrowse_MakesAllLinesClearAndClickSeekAppliesOffset()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track) { PositionSeconds = 1 };
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[]
            {
                new LyricLineModel(1, "00:01", "First"),
                new LyricLineModel(3, "00:03", "Second")
            },
            "local",
            "sidecar",
            true))
        {
            OffsetSeconds = 0.5
        };
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 2);
        var begin = typeof(PlaybackViewModel).GetMethod("BeginManualLyricsBrowse");
        var seek = typeof(PlaybackViewModel).GetMethod("SeekToLyric");
        var manualProperty = typeof(PlaybackViewModel).GetProperty("IsManualScrolling");

        Assert.NotNull(begin);
        Assert.NotNull(seek);
        Assert.NotNull(manualProperty);
        begin.Invoke(viewModel, null);

        Assert.True((bool)manualProperty.GetValue(viewModel)!);
        Assert.All(viewModel.Lyrics, line => Assert.Equal(1, line.TextOpacity));

        seek.Invoke(viewModel, new object[] { 1 });

        Assert.False((bool)manualProperty.GetValue(viewModel)!);
        Assert.Equal(3.5, playback.PositionSeconds, 3);
        Assert.Equal(1, viewModel.CurrentLyricIndex);
    }

    [Fact]
    public async Task PositiveOffset_DelaysLyricSelection()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track) { PositionSeconds = 3.2 };
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[]
            {
                new LyricLineModel(1, "00:01", "First"),
                new LyricLineModel(3, "00:03", "Second")
            },
            "local",
            "sidecar",
            true))
        {
            OffsetSeconds = 0.5
        };
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 2);

        Assert.Equal(0, viewModel.CurrentLyricIndex);
        Assert.Equal("+0.5 s", viewModel.LyricsOffsetLabel);

        playback.PositionSeconds = 3.6;
        playback.RaiseStateChanged();

        Assert.Equal(1, viewModel.CurrentLyricIndex);
    }

    [Fact]
    public async Task ToggleLyricsSource_PersistsAndReloadsSelectedSource()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track);
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[] { new LyricLineModel(0, "00:00", "Local") },
            "local",
            "sidecar",
            true));
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 1);

        await viewModel.ToggleLyricsSourceCommand.ExecuteAsync(null);

        Assert.Equal("online", lyrics.PreferredSource);
        Assert.Equal("Online", viewModel.LyricsSourceLabel);
        Assert.Equal("Online", Assert.Single(viewModel.Lyrics).Text);
    }

    [Fact]
    public async Task ToggleLyricsSource_WhenTargetIsMissing_PreservesCurrentLyricsAndSource()
    {
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[] { new LyricLineModel(0, "00:00", "Keep local") },
            "local",
            "sidecar",
            true))
        {
            OnlineMissing = true
        };
        var viewModel = new PlaybackViewModel(new FakePlaybackService(CreateTrack()), lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 1);

        await viewModel.ToggleLyricsSourceCommand.ExecuteAsync(null);

        Assert.Equal("local", lyrics.PreferredSource);
        Assert.Equal("Local", viewModel.LyricsSourceLabel);
        Assert.Equal("Keep local", Assert.Single(viewModel.Lyrics).Text);
    }

    [Fact]
    public async Task ApplyLyricsOffset_ValidatesPersistsAndDoesNotSeekPlayback()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track) { PositionSeconds = 24.5 };
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[] { new LyricLineModel(20, "00:20", "Line") },
            "online",
            "lrclib",
            true));
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 1);

        Assert.True(await viewModel.ApplyLyricsOffsetAsync("+1.2"));
        Assert.Equal(1.2, lyrics.OffsetSeconds, 3);
        Assert.Equal("+1.2 s", viewModel.LyricsOffsetLabel);
        Assert.Equal(24.5, playback.PositionSeconds, 3);

        Assert.False(await viewModel.ApplyLyricsOffsetAsync("+1.23"));
        Assert.False(await viewModel.ApplyLyricsOffsetAsync(".5"));
        Assert.Equal(1.2, lyrics.OffsetSeconds, 3);
        Assert.Equal(24.5, playback.PositionSeconds, 3);
    }

    [Fact]
    public async Task LyricsEmptyState_DistinguishesNoTrackFromMissingLyrics()
    {
        var noTrackPlayback = new FakePlaybackService(CreateTrack()) { CurrentTrack = null };
        var noTrack = new PlaybackViewModel(noTrackPlayback, new FakeLyricsService(LyricsDocumentModel.Empty()));

        Assert.True(noTrack.ShowLyricsEmptyState);
        Assert.Equal("选择一首歌曲后显示歌词", noTrack.LyricsEmptyMessage);

        var missing = new PlaybackViewModel(
            new FakePlaybackService(CreateTrack()),
            new FakeLyricsService(LyricsDocumentModel.Empty()));
        await WaitUntilAsync(() => !missing.IsLyricsLoading);

        Assert.True(missing.ShowLyricsEmptyState);
        Assert.Equal("暂未找到歌词", missing.LyricsEmptyMessage);
    }

    [Fact]
    public async Task TrackChange_CancelsPreviousLyricsLoad()
    {
        var first = CreateTrack() with { Id = "first" };
        var second = CreateTrack() with { Id = "second", Path = @"C:\Music\Second.flac" };
        var playback = new FakePlaybackService(first);
        var lyrics = new DelayedLyricsService();
        _ = new PlaybackViewModel(playback, lyrics);
        await lyrics.FirstLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        playback.CurrentTrack = second;
        playback.RaiseStateChanged();

        await WaitUntilAsync(() => lyrics.CancellationObserved);
    }

    [Fact]
    public async Task SearchAndApplyLyricsResult_ReplacesDisplayedDocument()
    {
        var track = CreateTrack();
        var playback = new FakePlaybackService(track);
        var result = new LyricsSearchResultModel(
            "result",
            "Song",
            "Artist",
            "Album",
            120,
            "[00:04]Selected line",
            null,
            "lrclib");
        var lyrics = new FakeLyricsService(new LyricsDocumentModel(
            new[] { new LyricLineModel(0, "00:00", "Initial") },
            "local",
            "sidecar",
            true))
        {
            SearchResults = new[] { result }
        };
        var viewModel = new PlaybackViewModel(playback, lyrics);
        await WaitUntilAsync(() => viewModel.Lyrics.Count == 1);

        var results = await viewModel.SearchOnlineLyricsAsync("Song Artist");
        await viewModel.ApplyLyricsSearchResultAsync(Assert.Single(results));

        Assert.Equal("Selected line", Assert.Single(viewModel.Lyrics).Text);
        Assert.Equal("Online", viewModel.LyricsSourceLabel);
        Assert.Equal("lrclib", viewModel.LyricsProvider);
    }

    [Fact]
    public async Task LyricsSearchViewModel_SearchesAndRaisesAppliedEvent()
    {
        var track = CreateTrack();
        var result = new LyricsSearchResultModel(
            "result",
            "Song",
            "Artist",
            "Album",
            120,
            "[00:04]Selected line",
            null,
            "lrclib");
        var lyrics = new FakeLyricsService(LyricsDocumentModel.Empty())
        {
            SearchResults = new[] { result }
        };
        var playback = new PlaybackViewModel(new FakePlaybackService(track), lyrics);
        var search = new LyricsSearchViewModel(playback);
        var applied = false;
        search.ResultApplied += (_, _) => applied = true;

        await search.SearchCommand.ExecuteAsync(null);
        await search.SelectResultCommand.ExecuteAsync(result);

        Assert.Equal("Song Artist", search.Query);
        Assert.Equal(result, Assert.Single(search.Results));
        Assert.True(applied);
        Assert.Equal("Selected line", Assert.Single(playback.Lyrics).Text);
    }

    [Fact]
    public async Task LyricsSearchViewModel_PrioritizesWordThenLineThenPlainLyrics()
    {
        var track = CreateTrack();
        var lyrics = new FakeLyricsService(LyricsDocumentModel.Empty())
        {
            SearchResults = new[]
            {
                new LyricsSearchResultModel("plain", "Song", "Artist", "", 120, null, "Plain lyric", "lrclib"),
                new LyricsSearchResultModel("line", "Song", "Artist", "", 120, "[00:01.00]Line lyric", null, "lrclib"),
                new LyricsSearchResultModel("word", "Song", "Artist", "", 120, "[0,1000]Hi(0,500) there(500,500)", null, "qqmusic")
            }
        };
        var playback = new PlaybackViewModel(new FakePlaybackService(track), lyrics);
        var search = new LyricsSearchViewModel(playback);

        await search.SearchCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "word", "line", "plain" }, search.Results.Select(result => result.Id));
        Assert.Equal("逐字", search.Results[0].LyricsKindLabel);
        Assert.Equal("逐行", search.Results[1].LyricsKindLabel);
        Assert.Equal("纯文本", search.Results[2].LyricsKindLabel);
    }

    [Fact]
    public void CoverChange_RefreshesEffectivePlayerCoverWithoutReplacingPlaybackTrack()
    {
        var track = CreateTrack() with { CoverPath = @"C:\Covers\Embedded.jpg" };
        var covers = new FakeCoverService();
        var viewModel = new PlaybackViewModel(
            new FakePlaybackService(track),
            new FakeLyricsService(LyricsDocumentModel.Empty()),
            covers);

        Assert.Equal(track.CoverPath, viewModel.CurrentCoverPath);

        covers.SetCover(track, @"C:\Covers\Custom.png");

        Assert.Equal(@"C:\Covers\Custom.png", viewModel.CurrentCoverPath);
        Assert.Same(track, viewModel.CurrentTrack);
    }

    [Fact]
    public void CoverChangeForSameTitleAndArtist_RefreshesPlayerAcrossDifferentTrackIdentity()
    {
        var track = CreateTrack() with { CoverPath = @"C:\Covers\Embedded.jpg" };
        var covers = new FakeCoverService();
        var viewModel = new PlaybackViewModel(
            new FakePlaybackService(track),
            new FakeLyricsService(LyricsDocumentModel.Empty()),
            covers);
        var equivalent = track with
        {
            Id = "copy",
            Path = @"D:\Music\Song.mp3"
        };

        covers.SetCover(equivalent, @"C:\Covers\Shared.png");

        Assert.Equal(@"C:\Covers\Shared.png", viewModel.CurrentCoverPath);
        Assert.Equal(@"C:\Covers\Shared.png", Assert.Single(viewModel.Queue).CoverPath);
    }

    [Fact]
    public void PlayerMetadata_ExposesArtistAndAlbumSeparatelyForFullPlay()
    {
        var viewModel = new PlaybackViewModel(
            new FakePlaybackService(CreateTrack()),
            new FakeLyricsService(LyricsDocumentModel.Empty()));

        var artist = typeof(PlaybackViewModel).GetProperty("CurrentArtist");
        var album = typeof(PlaybackViewModel).GetProperty("CurrentAlbum");

        Assert.NotNull(artist);
        Assert.NotNull(album);
        Assert.Equal("Artist", artist.GetValue(viewModel));
        Assert.Equal("Album", album.GetValue(viewModel));
    }

    [Fact]
    public void PlaybackModeGlyph_TracksLoopSingleAndShuffleModes()
    {
        var service = new FakePlaybackService(CreateTrack());
        var viewModel = new PlaybackViewModel(
            service,
            new FakeLyricsService(LyricsDocumentModel.Empty()));

        Assert.Equal("\uE8EE", viewModel.ModeGlyph);

        service.Mode = PlaybackMode.Single;
        service.RaiseStateChanged();
        Assert.Equal("\uE8ED", viewModel.ModeGlyph);

        service.Mode = PlaybackMode.Shuffle;
        service.RaiseStateChanged();
        Assert.Equal("\uE8B1", viewModel.ModeGlyph);
    }

    [Fact]
    public async Task DisablingBeta_HidesOnlineNavigationAndReturnsToLibrary()
    {
        var settings = new FakeShellSettingsService();
        var playback = new PlaybackViewModel(
            new FakePlaybackService(CreateTrack()),
            new FakeLyricsService(LyricsDocumentModel.Empty()));
        var shell = new ShellViewModel(settings, new FakeShellLibraryService(), playback);

        Assert.Equal("Home", shell.SelectedRoute);
        Assert.True(shell.IsOnlineNavigationVisible);

        await settings.SaveAsync(settings.Current with { ExperimentalFeaturesEnabled = false });

        Assert.False(shell.IsOnlineNavigationVisible);
        Assert.Equal("Library", shell.SelectedRoute);

        var restarted = new ShellViewModel(settings, new FakeShellLibraryService(), playback);
        Assert.Equal("Library", restarted.SelectedRoute);
    }

    [Fact]
    public void ShellGoBack_RestoresRouteBeforeNestedPages()
    {
        var playback = new PlaybackViewModel(
            new FakePlaybackService(CreateTrack()),
            new FakeLyricsService(LyricsDocumentModel.Empty()));
        var shell = new ShellViewModel(new FakeShellSettingsService(), new FakeShellLibraryService(), playback);

        shell.Navigate("Search");
        shell.Navigate("FullPlay");
        shell.GoBackCommand.Execute(null);

        Assert.Equal("Search", shell.SelectedRoute);

        shell.Navigate("Home");
        shell.Navigate("AlbumDetail");
        shell.Navigate("FullPlay");
        shell.GoBackCommand.Execute(null);
        Assert.Equal("AlbumDetail", shell.SelectedRoute);
        shell.GoBackCommand.Execute(null);
        Assert.Equal("Home", shell.SelectedRoute);
    }

    [Fact]
    public void ShellNavigationRequests_DistinguishPrimaryNestedAndBackNavigation()
    {
        var playback = new PlaybackViewModel(
            new FakePlaybackService(CreateTrack()),
            new FakeLyricsService(LyricsDocumentModel.Empty()));
        var shell = new ShellViewModel(new FakeShellSettingsService(), new FakeShellLibraryService(), playback);
        var requests = new List<ShellNavigationRequest>();
        shell.NavigationRequested += (_, request) => requests.Add(request);

        shell.Navigate("Search");
        shell.Navigate("FullPlay");
        shell.GoBackCommand.Execute(null);

        Assert.Collection(
            requests,
            request => Assert.Equal(new ShellNavigationRequest("Search", ShellNavigationKind.Primary), request),
            request => Assert.Equal(new ShellNavigationRequest("FullPlay", ShellNavigationKind.Nested), request),
            request => Assert.Equal(new ShellNavigationRequest("Search", ShellNavigationKind.Back), request));
    }

    [Fact]
    public void FailedBackNavigation_RestoresPoppedRouteForRetry()
    {
        var playback = new PlaybackViewModel(
            new FakePlaybackService(CreateTrack()),
            new FakeLyricsService(LyricsDocumentModel.Empty()));
        var shell = new ShellViewModel(new FakeShellSettingsService(), new FakeShellLibraryService(), playback);
        var requests = new List<ShellNavigationRequest>();
        shell.NavigationRequested += (_, request) => requests.Add(request);

        shell.Navigate("AlbumDetail");
        shell.GoBackCommand.Execute(null);
        var failedBack = requests[^1];
        shell.RollbackNavigation("AlbumDetail", failedBack);

        Assert.Equal("AlbumDetail", shell.SelectedRoute);
        Assert.True(shell.CanGoBack);

        shell.GoBackCommand.Execute(null);

        Assert.Equal(new ShellNavigationRequest("Home", ShellNavigationKind.Back), requests[^1]);
    }

    private static TrackModel CreateTrack()
    {
        return new TrackModel("track", @"C:\Music\Song.flac", "Song", "Artist", "Album", "02:00", null, DurationSeconds: 120);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeLyricsService(LyricsDocumentModel localDocument) : ILyricsService
    {
        public string PreferredSource { get; private set; } = "local";
        public double OffsetSeconds { get; set; }
        public bool OnlineMissing { get; set; }
        public IReadOnlyList<LyricsSearchResultModel> SearchResults { get; set; } = Array.Empty<LyricsSearchResultModel>();
        public LyricsDocumentModel? WordSyncedUpgrade { get; set; }
        public bool UpgradeRequested { get; private set; }

        public Task<IReadOnlyList<LyricLineModel>> LoadLyricsAsync(TrackModel track, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(localDocument.Lines);
        }

        public Task<LyricsDocumentModel> LoadLyricsDocumentAsync(TrackModel track, string? sourceOverride = null, bool forceOnline = false, CancellationToken cancellationToken = default)
        {
            var source = sourceOverride ?? PreferredSource;
            if (source == "online")
            {
                if (OnlineMissing)
                {
                    return Task.FromResult(LyricsDocumentModel.Empty("online"));
                }

                return Task.FromResult(new LyricsDocumentModel(
                    new[] { new LyricLineModel(0, "00:00", "Online") },
                    "online",
                    "lrclib",
                    true));
            }

            return Task.FromResult(localDocument);
        }

        public Task<IReadOnlyList<LyricsSearchResultModel>> SearchOnlineLyricsAsync(TrackModel track, string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SearchResults);
        }

        public Task<LyricsDocumentModel> LoadSearchResultAsync(TrackModel track, LyricsSearchResultModel result, CancellationToken cancellationToken = default)
        {
            PreferredSource = "online";
            return Task.FromResult(new LyricsDocumentModel(
                new[] { new LyricLineModel(4, "00:04", "Selected line") },
                "online",
                result.Provider,
                true));
        }

        public Task<LyricsDocumentModel?> TryLoadWordSyncedLyricsDocumentAsync(
            TrackModel track,
            CancellationToken cancellationToken = default)
        {
            UpgradeRequested = true;
            return Task.FromResult(WordSyncedUpgrade);
        }

        public string GetPreferredSource(TrackModel track) => PreferredSource;
        public double GetOffsetSeconds(TrackModel track) => OffsetSeconds;

        public Task SetPreferredSourceAsync(TrackModel track, string source)
        {
            PreferredSource = source;
            return Task.CompletedTask;
        }

        public Task SetOffsetSecondsAsync(TrackModel track, double seconds)
        {
            OffsetSeconds = seconds;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlaybackService(TrackModel track) : IPlaybackService
    {
        public TrackModel? CurrentTrack { get; set; } = track;
        public IReadOnlyList<TrackModel> Queue { get; } = new[] { track };
        public PlaybackMode Mode { get; set; } = PlaybackMode.Loop;
        public PlaybackStatus Status => PlaybackStatus.Playing;
        public double Volume => 0.8;
        public double PositionSeconds { get; set; }
        public double DurationSeconds => 120;
        public bool IsLoading => false;
        public bool IsPlaying => true;
        public string? Error => null;
        public IReadOnlyList<WindowsDsdDeviceModel> WindowsDsdDevices => Array.Empty<WindowsDsdDeviceModel>();
        public bool WindowsDsdAvailable => false;
        public string? WindowsDsdOutputModeLabel => null;
        public string? WindowsDsdActiveDeviceName => null;
        public string? WindowsDsdFallbackReason => null;
        public event EventHandler? StateChanged;

        public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
        public void Play(TrackModel value, IReadOnlyList<TrackModel>? queue = null) { }
        public void Stop() { }
        public void TogglePlayPause() { }
        public void Next() { }
        public void Previous() { }
        public void CycleMode() { }
        public void SetVolume(double volume) { }
        public void Seek(double seconds) => PositionSeconds = seconds;
        public void PlayFromQueue(TrackModel value) { }
        public void ReorderQueue(IReadOnlyList<TrackModel> tracks) { }
        public void RemoveFromQueue(TrackModel value) { }
        public void ClearQueue() { }
        public Task RefreshWindowsDsdDevicesAsync() => Task.CompletedTask;
    }

    private sealed class FakeCoverService : ICoverService
    {
        private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<CoverChangedEventArgs>? CoverChanged;

        public string? ResolveCoverPath(TrackModel track)
        {
            var key = TrackCoverIdentity.CreateKey(track.Title, track.Artist);
            return _paths.TryGetValue(key, out var path) ? path : track.CoverPath;
        }

        public Task<IReadOnlyList<CoverSearchResultModel>> SearchOnlineCoversAsync(
            TrackModel track,
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CoverSearchResultModel>>(Array.Empty<CoverSearchResultModel>());
        }

        public Task<string> ApplyOnlineCoverAsync(
            TrackModel track,
            CoverSearchResultModel result,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void SetCover(TrackModel track, string path)
        {
            _paths[TrackCoverIdentity.CreateKey(track.Title, track.Artist)] = path;
            CoverChanged?.Invoke(this, new CoverChangedEventArgs(
                track.Id,
                track.Path,
                path,
                track.Title,
                track.Artist));
        }
    }

    private sealed class DelayedLyricsService : ILyricsService
    {
        public TaskCompletionSource FirstLoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public Task<IReadOnlyList<LyricLineModel>> LoadLyricsAsync(TrackModel track, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LyricLineModel>>(Array.Empty<LyricLineModel>());
        }

        public async Task<LyricsDocumentModel> LoadLyricsDocumentAsync(TrackModel track, string? sourceOverride = null, bool forceOnline = false, CancellationToken cancellationToken = default)
        {
            if (track.Id != "first")
            {
                return LyricsDocumentModel.Empty();
            }

            FirstLoadStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            return LyricsDocumentModel.Empty();
        }

        public Task<IReadOnlyList<LyricsSearchResultModel>> SearchOnlineLyricsAsync(TrackModel track, string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LyricsSearchResultModel>>(Array.Empty<LyricsSearchResultModel>());

        public Task<LyricsDocumentModel> LoadSearchResultAsync(TrackModel track, LyricsSearchResultModel result, CancellationToken cancellationToken = default) =>
            Task.FromResult(LyricsDocumentModel.Empty());

        public string GetPreferredSource(TrackModel track) => "local";
        public double GetOffsetSeconds(TrackModel track) => 0;
        public Task SetPreferredSourceAsync(TrackModel track, string source) => Task.CompletedTask;
        public Task SetOffsetSecondsAsync(TrackModel track, double seconds) => Task.CompletedTask;
    }

    private sealed class FakeShellSettingsService : ISettingsService
    {
        public SettingsSnapshot Current { get; private set; } = new(
            "zh-CN",
            true,
            true,
            false,
            "wasapi_shared",
            "auto",
            "auto",
            true,
            220,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new FlutterPreferencesMigrationResult(
                string.Empty,
                false,
                0,
                DateTimeOffset.MinValue,
                new Dictionary<string, object?>()));

        public event EventHandler? SettingsChanged;

        public Task SaveAsync(SettingsSnapshot snapshot)
        {
            Current = snapshot;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeShellLibraryService : ILibraryService
    {
        public IReadOnlyList<TrackModel> Tracks { get; } = Array.Empty<TrackModel>();
        public IReadOnlyList<string> Folders { get; } = Array.Empty<string>();
        public IReadOnlyList<AlbumModel> Albums { get; } = Array.Empty<AlbumModel>();
        public IReadOnlyList<ArtistModel> Artists { get; } = Array.Empty<ArtistModel>();
        public IReadOnlyList<TrackModel> Favorites { get; } = Array.Empty<TrackModel>();
        public bool IsScanning => false;
        public string? Error => null;

        public event EventHandler? LibraryChanged;

        public Task AddFolderAsync(string folder) => Task.CompletedTask;
        public Task RemoveFolderAsync(string folder) => Task.CompletedTask;
        public Task RescanAsync() => Task.CompletedTask;
        public Task ToggleFavoriteAsync(TrackModel track) => Task.CompletedTask;
        public Task PersistTrackOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task PersistFavoriteOrderAsync(IReadOnlyList<TrackModel> visibleTracks) => Task.CompletedTask;
        public Task RemoveTrackAsync(TrackModel track, bool deleteSourceFile) => Task.CompletedTask;
        public IReadOnlyList<TrackModel> GetAlbumTracks(string albumId) => Array.Empty<TrackModel>();
        public IReadOnlyList<TrackModel> GetArtistTracks(string artistName) => Array.Empty<TrackModel>();

        public void RaiseChanged() => LibraryChanged?.Invoke(this, EventArgs.Empty);
    }
}
