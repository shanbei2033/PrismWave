using PrismWave_WinUI.Infrastructure.Online;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlineLoginDialogCoordinatorTests
{
    [Fact]
    public async Task RunAsync_PollsEveryTwoSecondsUntilAuthenticated()
    {
        var account = new FakeAccountService(
            OnlineProviderAuthState.Scanned,
            OnlineProviderAuthState.Authenticated);
        var delays = new List<TimeSpan>();
        using var coordinator = new OnlineLoginDialogCoordinator(
            account,
            async (interval, cancellationToken) =>
            {
                delays.Add(interval);
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            });
        var states = new List<OnlineProviderAuthState>();
        coordinator.SnapshotChanged += snapshot => states.Add(snapshot.State);

        var started = await coordinator.RunAsync("netease");

        Assert.True(started);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)], delays);
        Assert.Equal([OnlineProviderAuthState.Scanned, OnlineProviderAuthState.Authenticated], states);
        Assert.False(coordinator.IsRunning);
    }

    [Fact]
    public async Task RunAsync_RejectsDuplicateWhileAChallengeIsActive()
    {
        var account = new FakeAccountService(OnlineProviderAuthState.Authenticated);
        var enteredDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new OnlineLoginDialogCoordinator(
            account,
            async (_, cancellationToken) =>
            {
                enteredDelay.TrySetResult();
                await releaseDelay.Task.WaitAsync(cancellationToken);
            });

        var firstRun = coordinator.RunAsync("qq");
        await enteredDelay.Task;

        Assert.False(await coordinator.RunAsync("qq"));

        releaseDelay.TrySetResult();
        Assert.True(await firstRun);
    }

    [Fact]
    public async Task Cancel_ImmediatelyCancelsPendingPoll()
    {
        var account = new FakeAccountService(OnlineProviderAuthState.Authenticated);
        var enteredDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new OnlineLoginDialogCoordinator(
            account,
            async (_, cancellationToken) =>
            {
                enteredDelay.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        var run = coordinator.RunAsync("netease");
        await enteredDelay.Task;
        coordinator.Cancel();

        Assert.True(await run);
        Assert.False(coordinator.IsRunning);
        Assert.Equal(0, account.PollCount);
    }

    [Fact]
    public async Task ExpiredRun_AllowsRefreshToCreateANewChallenge()
    {
        var account = new FakeAccountService(
            OnlineProviderAuthState.Expired,
            OnlineProviderAuthState.Authenticated);
        using var coordinator = new OnlineLoginDialogCoordinator(
            account,
            static (_, _) => Task.CompletedTask);

        Assert.True(await coordinator.RunAsync("netease"));
        Assert.True(await coordinator.RunAsync("netease"));

        Assert.Equal(2, account.CreateCount);
    }

    private sealed class FakeAccountService(params OnlineProviderAuthState[] states) : IOnlineAccountService
    {
        private readonly Queue<OnlineProviderAuthState> _states = new(states);

        public event EventHandler<OnlineAccountSnapshot>? AccountChanged;

        public int CreateCount { get; private set; }

        public int PollCount { get; private set; }

        public Task<OnlineLoginChallenge> CreateChallengeAsync(string providerKey, CancellationToken cancellationToken)
        {
            CreateCount++;
            return Task.FromResult(new OnlineLoginChallenge(
                providerKey,
                "qr-payload",
                null,
                DateTimeOffset.UtcNow.AddMinutes(2),
                CreateCount));
        }

        public Task<OnlineAccountSnapshot> PollAsync(string providerKey, CancellationToken cancellationToken)
        {
            PollCount++;
            var state = _states.Count > 0 ? _states.Dequeue() : OnlineProviderAuthState.WaitingForScan;
            var snapshot = new OnlineAccountSnapshot(providerKey, state);
            AccountChanged?.Invoke(this, snapshot);
            return Task.FromResult(snapshot);
        }

        public OnlineAccountSnapshot GetSnapshot(string providerKey) =>
            new(providerKey, OnlineProviderAuthState.Disconnected);

        public Task<OnlineProviderSession?> GetSessionAsync(string providerKey, CancellationToken cancellationToken) =>
            Task.FromResult<OnlineProviderSession?>(null);

        public Task<OnlineProviderSession?> HandleAuthenticationFailureAsync(string providerKey, CancellationToken cancellationToken) =>
            Task.FromResult<OnlineProviderSession?>(null);

        public Task InvalidateSessionAsync(string providerKey, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SignOutAsync(string providerKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
