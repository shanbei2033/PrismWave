using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class RemotePlaybackRecoveryPolicyTests
{
    [Fact]
    public void OpeningSourceFailures_AllowAtMostThreeDistinctSourceAttempts()
    {
        var policy = new RemotePlaybackRecoveryPolicy();
        policy.BeginTrack("rock-0");
        Assert.True(policy.BeginSourceAttempt("rock-0", "netease:one"));

        Assert.Equal(
            RemotePlaybackRecoveryAction.ResolveNextSource,
            policy.DecideFailure("rock-0", isRemote: true, OnlinePlaybackFailureKind.Source));
        Assert.True(policy.BeginSourceAttempt("rock-0", "qq:two"));
        Assert.Equal(
            RemotePlaybackRecoveryAction.ResolveNextSource,
            policy.DecideFailure("rock-0", isRemote: true, OnlinePlaybackFailureKind.Source));
        Assert.True(policy.BeginSourceAttempt("rock-0", "audius:three"));
        Assert.Equal(
            RemotePlaybackRecoveryAction.None,
            policy.DecideFailure("rock-0", isRemote: true, OnlinePlaybackFailureKind.Source));

        Assert.Equal(
            new[] { "audius:three", "netease:one", "qq:two" },
            policy.ExcludedCandidateKeys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void AudioOutputFailure_RetriesOutputOnceWithoutExcludingOrSwitchingSource()
    {
        var policy = new RemotePlaybackRecoveryPolicy();
        policy.BeginTrack("rock-0");
        Assert.True(policy.BeginSourceAttempt("rock-0", "netease:one"));

        var first = policy.DecideFailure(
            "rock-0",
            isRemote: true,
            OnlinePlaybackFailureKind.AudioOutput);
        var second = policy.DecideFailure(
            "rock-0",
            isRemote: true,
            OnlinePlaybackFailureKind.AudioOutput);

        Assert.Equal(RemotePlaybackRecoveryAction.RetryAudioOutput, first);
        Assert.Equal(RemotePlaybackRecoveryAction.None, second);
        Assert.Empty(policy.ExcludedCandidateKeys);
    }

    [Fact]
    public void OpenedSourceFailure_ResolvesOnceWithResumeAndDoesNotChainOpeningRecovery()
    {
        var policy = new RemotePlaybackRecoveryPolicy();
        policy.BeginTrack("rock-0");
        Assert.True(policy.BeginSourceAttempt("rock-0", "netease:one"));
        policy.MarkOpened("rock-0");

        Assert.Equal(
            RemotePlaybackRecoveryAction.ResolveNextSourceAndResume,
            policy.DecideFailure("rock-0", isRemote: true, OnlinePlaybackFailureKind.Source));
        Assert.True(policy.BeginSourceAttempt("rock-0", "qq:two"));
        Assert.Equal(
            RemotePlaybackRecoveryAction.None,
            policy.DecideFailure("rock-0", isRemote: true, OnlinePlaybackFailureKind.Source));
        Assert.Equal(
            new[] { "netease:one", "qq:two" },
            policy.ExcludedCandidateKeys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(OnlinePlaybackFailureKind.Unknown, true)]
    [InlineData(OnlinePlaybackFailureKind.Source, false)]
    public void UnknownOrLocalFailure_DoesNotRecover(
        OnlinePlaybackFailureKind failureKind,
        bool isRemote)
    {
        var policy = new RemotePlaybackRecoveryPolicy();
        policy.BeginTrack("rock-0");
        Assert.True(policy.BeginSourceAttempt("rock-0", "netease:one"));

        Assert.Equal(
            RemotePlaybackRecoveryAction.None,
            policy.DecideFailure("rock-0", isRemote, failureKind));
        Assert.Empty(policy.ExcludedCandidateKeys);
    }

    [Fact]
    public void NewTrack_RejectsStaleRecoveryAndResetsBudgets()
    {
        var policy = new RemotePlaybackRecoveryPolicy();
        policy.BeginTrack("rock-0");
        Assert.True(policy.BeginSourceAttempt("rock-0", "netease:one"));
        Assert.Equal(
            RemotePlaybackRecoveryAction.RetryAudioOutput,
            policy.DecideFailure("rock-0", isRemote: true, OnlinePlaybackFailureKind.AudioOutput));

        policy.BeginTrack("rock-1");

        Assert.Equal(
            RemotePlaybackRecoveryAction.None,
            policy.DecideFailure("rock-0", isRemote: true, OnlinePlaybackFailureKind.Source));
        Assert.True(policy.BeginSourceAttempt("rock-1", "qq:one"));
        Assert.Equal(
            RemotePlaybackRecoveryAction.RetryAudioOutput,
            policy.DecideFailure("rock-1", isRemote: true, OnlinePlaybackFailureKind.AudioOutput));
        Assert.Empty(policy.ExcludedCandidateKeys);
    }

    [Fact]
    public void DuplicateCandidate_IsRejectedWithoutConsumingAnotherAttempt()
    {
        var policy = new RemotePlaybackRecoveryPolicy();
        policy.BeginTrack("rock-0");

        Assert.True(policy.BeginSourceAttempt("rock-0", "netease:one"));
        Assert.False(policy.BeginSourceAttempt("rock-0", "NETEASE:ONE"));
        Assert.Equal(1, policy.SourceAttemptCount);
    }

    [Fact]
    public void SourceFailure_ExcludesBothCandidateIdentityAndNormalizedUrl()
    {
        var policy = new RemotePlaybackRecoveryPolicy();
        policy.BeginTrack("rock-0");
        Assert.True(policy.BeginSourceAttempt(
            "rock-0",
            "netease:one",
            "HTTPS://Audio.Test:443/song.mp3?token=secret#fragment"));

        Assert.Equal(
            RemotePlaybackRecoveryAction.ResolveNextSource,
            policy.DecideFailure("rock-0", isRemote: true, OnlinePlaybackFailureKind.Source));

        Assert.True(policy.Exclusions.ContainsCandidate("netease:one"));
        Assert.True(policy.Exclusions.ContainsPlaybackUrl(
            "https://audio.test/song.mp3?token=secret"));
        Assert.DoesNotContain("secret", policy.Exclusions.ToString(), StringComparison.Ordinal);
    }
}
