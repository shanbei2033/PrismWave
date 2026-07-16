using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Player;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class CoverSearchViewModelTests
{
    [Fact]
    public async Task SearchAndSelectResult_AppliesCoverAndRaisesCompletion()
    {
        var result = new CoverSearchResultModel(
            "apple:1",
            "Song",
            "Artist",
            "Album",
            "https://example.com/thumb.jpg",
            "https://example.com/full.jpg",
            "apple",
            120);
        var service = new FakeCoverService(result);
        var track = new TrackModel(
            "track",
            @"C:\Music\Song.flac",
            "Song",
            "Artist",
            "Album",
            "02:00",
            null);
        var viewModel = new CoverSearchViewModel(service, track);
        var applied = false;
        viewModel.CoverApplied += (_, _) => applied = true;

        await viewModel.SearchCommand.ExecuteAsync(null);
        await viewModel.SelectResultCommand.ExecuteAsync(result);

        Assert.Equal("Song Artist", viewModel.Query);
        Assert.Equal(result, Assert.Single(viewModel.Results));
        Assert.Equal("Song Artist", service.LastQuery);
        Assert.Equal(result, service.AppliedResult);
        Assert.True(applied);
        Assert.Equal("Cover updated.", viewModel.Status);
    }

    private sealed class FakeCoverService(CoverSearchResultModel result) : ICoverService
    {
        public string? LastQuery { get; private set; }
        public CoverSearchResultModel? AppliedResult { get; private set; }
        public event EventHandler<CoverChangedEventArgs>? CoverChanged;

        public string? ResolveCoverPath(TrackModel track) => track.CoverPath;

        public Task<IReadOnlyList<CoverSearchResultModel>> SearchOnlineCoversAsync(
            TrackModel track,
            string query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<CoverSearchResultModel>>(new[] { result });
        }

        public Task<string> ApplyOnlineCoverAsync(
            TrackModel track,
            CoverSearchResultModel selected,
            CancellationToken cancellationToken = default)
        {
            AppliedResult = selected;
            const string path = @"C:\Covers\Custom.jpg";
            CoverChanged?.Invoke(this, new CoverChangedEventArgs(track.Id, track.Path, path));
            return Task.FromResult(path);
        }
    }
}
