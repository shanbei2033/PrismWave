using PrismWave_WinUI.Models;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlinePlaybackResolutionTests
{
    [Fact]
    public void ExistingConstructor_RemainsCompatibleWithFailoverDefaults()
    {
        var resolution = new OnlinePlaybackResolution(
            "https://audio.test/song.mp3",
            "netease");

        Assert.Null(resolution.CandidateKey);
        Assert.Equal(OnlineQualityPreference.Lossless, resolution.Quality);
        Assert.Null(resolution.ExpiresAt);
        Assert.Equal(1, resolution.Attempt);
    }

    [Fact]
    public void CandidateKey_HashesDirectUrlsWithoutLeakingQuerySecrets()
    {
        var key = OnlinePlaybackCandidateKey.Create(
            "online",
            providerTrackId: null,
            "https://audio.test/song.mp3?token=super-secret");

        Assert.StartsWith("url:", key, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", key, StringComparison.Ordinal);
        Assert.Equal(
            key,
            OnlinePlaybackCandidateKey.Create(
                "online",
                providerTrackId: null,
                "https://audio.test/song.mp3?token=super-secret"));
    }

    [Fact]
    public void CandidateKey_UsesOriginalDescriptorIdentityWhenTrackHasTemporaryUrl()
    {
        var track = new TrackModel(
            "song",
            "online://netease/provider-id",
            "Song",
            "Artist",
            "Album",
            "02:00",
            null,
            true,
            "netease",
            "https://audio.test/song.mp3?token=temporary");

        Assert.Equal("netease:provider-id", OnlinePlaybackCandidateKey.Create(track));
    }

    [Fact]
    public void Exclusions_MatchCandidateKeysAndNormalizedPlaybackUrls()
    {
        var exclusions = new OnlinePlaybackExclusions(
            new[] { "netease:failed" },
            new[] { "HTTPS://Audio.Test:443/music/song.mp3?token=secret#fragment" });

        Assert.True(exclusions.ContainsCandidate("NETEASE:FAILED"));
        Assert.True(exclusions.ContainsPlaybackUrl(
            "https://audio.test/music/song.mp3?token=secret"));
        Assert.False(exclusions.ContainsPlaybackUrl(
            "https://audio.test/music/other.mp3?token=secret"));
        Assert.DoesNotContain("secret", exclusions.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyResolution_PersistsActualCandidateIdentityOnTrack()
    {
        var original = new TrackModel(
            "song",
            "https://search.test/song.mp3",
            "Song",
            "Artist",
            "Album",
            "02:00",
            null,
            IsRemote: true,
            Provider: "online",
            PlaybackUrl: "https://search.test/song.mp3");
        var resolution = new OnlinePlaybackResolution(
            "https://audio.test/resolved.mp3",
            "netease",
            ProviderTrackId: "actual-id",
            CandidateKey: "netease:actual-id");

        var resolvedTrack = OnlinePlaybackTrack.ApplyResolution(original, resolution);

        Assert.Equal("netease:actual-id", resolvedTrack.OnlineCandidateKey);
        Assert.Equal("actual-id", resolvedTrack.OnlineProviderTrackId);
        Assert.Equal("netease:actual-id", OnlinePlaybackCandidateKey.Create(resolvedTrack));
        Assert.Equal("https://search.test/song.mp3", resolvedTrack.Path);
        Assert.Equal("https://audio.test/resolved.mp3", resolvedTrack.PlaybackUrl);
    }
}
