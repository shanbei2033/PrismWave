using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.ViewModels.Settings;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlineAccountSettingsViewModelTests
{
    [Fact]
    public void Constructor_LoadsBothProviderSnapshots()
    {
        var account = new FakeAccountService();
        account.Set(new OnlineAccountSnapshot(
            "netease",
            OnlineProviderAuthState.Authenticated,
            "Cloud user",
            "https://example.test/avatar.png"));
        using var viewModel = new OnlineAccountSettingsViewModel(account);

        Assert.Equal("Cloud user", viewModel.Netease.DisplayName);
        Assert.True(viewModel.Netease.IsAuthenticated);
        Assert.Equal(OnlineProviderAuthState.Disconnected, viewModel.Qq.State);
    }

    [Fact]
    public void AccountChanged_UpdatesMatchingCardOnly()
    {
        var account = new FakeAccountService();
        using var viewModel = new OnlineAccountSettingsViewModel(account);

        account.Set(new OnlineAccountSnapshot(
            "qq",
            OnlineProviderAuthState.Scanned,
            StatusMessage: "Scanned on phone"));

        Assert.Equal(OnlineProviderAuthState.Scanned, viewModel.Qq.State);
        Assert.Equal("Scanned on phone", viewModel.Qq.StatusText);
        Assert.Equal(OnlineProviderAuthState.Disconnected, viewModel.Netease.State);
    }

    [Fact]
    public async Task SignOutCommand_UsesAccountServiceEvenWhenLoginIsDisabled()
    {
        var account = new FakeAccountService();
        account.Set(new OnlineAccountSnapshot("netease", OnlineProviderAuthState.Authenticated));
        using var viewModel = new OnlineAccountSettingsViewModel(account)
        {
            IsLoginEnabled = false,
        };

        await viewModel.SignOutCommand.ExecuteAsync("netease");

        Assert.Equal("netease", account.LastSignedOutProvider);
        Assert.Equal(OnlineProviderAuthState.Disconnected, viewModel.Netease.State);
        Assert.True(viewModel.Netease.CanSignOut);
    }

    [Fact]
    public void AvatarUrl_RemovesCredentialsAndQueryValues()
    {
        var account = new FakeAccountService();
        account.Set(new OnlineAccountSnapshot(
            "qq",
            OnlineProviderAuthState.Authenticated,
            AvatarUrl: "https://user:secret@example.test/avatar.png?token=secret#fragment"));
        using var viewModel = new OnlineAccountSettingsViewModel(account);

        Assert.Equal("https://example.test/avatar.png", viewModel.Qq.AvatarUrl);

        account.Set(new OnlineAccountSnapshot(
            "qq",
            OnlineProviderAuthState.Authenticated,
            AvatarUrl: "data:image/png;base64,secret"));
        Assert.Null(viewModel.Qq.AvatarUrl);
    }

    [Fact]
    public async Task RefreshAccountsAsync_RestoresBothStoredSessionsInParallel()
    {
        var account = new FakeAccountService();
        var entered = 0;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        account.SessionLoader = async (providerKey, cancellationToken) =>
        {
            if (Interlocked.Increment(ref entered) == 2)
            {
                bothEntered.TrySetResult();
            }

            await bothEntered.Task.WaitAsync(cancellationToken);
            account.SetSilently(new OnlineAccountSnapshot(
                providerKey,
                OnlineProviderAuthState.Authenticated,
                $"{providerKey} user"));
            return new OnlineProviderSession(providerKey, new Dictionary<string, string>());
        };
        using var viewModel = new OnlineAccountSettingsViewModel(account);

        var refresh = viewModel.RefreshAccountsAsync();
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await refresh;

        Assert.Equal(2, account.SessionRequests.Count);
        Assert.True(viewModel.Netease.IsAuthenticated);
        Assert.True(viewModel.Qq.IsAuthenticated);
    }

    [Fact]
    public async Task RefreshAccountsAsync_NoStoredCredentialRemainsDisconnected()
    {
        var account = new FakeAccountService();
        using var viewModel = new OnlineAccountSettingsViewModel(account);

        await viewModel.RefreshAccountsAsync();

        Assert.Equal(OnlineProviderAuthState.Disconnected, viewModel.Netease.State);
        Assert.Equal(OnlineProviderAuthState.Disconnected, viewModel.Qq.State);
        Assert.True(viewModel.Netease.CanSignOut);
        Assert.True(viewModel.Qq.CanSignOut);
    }

    [Fact]
    public async Task RefreshAccountsAsync_ProviderExceptionDoesNotBlockOtherProvider()
    {
        var account = new FakeAccountService();
        account.SessionLoader = (providerKey, _) =>
            {
                if (providerKey == "netease")
                {
                    throw new InvalidOperationException("cookie=do-not-log");
                }

                account.SetSilently(new OnlineAccountSnapshot(
                    providerKey,
                    OnlineProviderAuthState.Authenticated));
                return Task.FromResult<OnlineProviderSession?>(
                    new OnlineProviderSession(providerKey, new Dictionary<string, string>()));
            };
        using var viewModel = new OnlineAccountSettingsViewModel(account);

        var error = await Record.ExceptionAsync(() => viewModel.RefreshAccountsAsync());

        Assert.Null(error);
        Assert.Equal(OnlineProviderAuthState.Disconnected, viewModel.Netease.State);
        Assert.True(viewModel.Qq.IsAuthenticated);
    }

    [Fact]
    public async Task RefreshAccountsAsync_StaleCompletionCannotOverwriteLatestRefresh()
    {
        var account = new FakeAccountService();
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestNumber = 0;
        account.SessionLoader = async (providerKey, _) =>
        {
            var request = Interlocked.Increment(ref requestNumber);
            if (request <= 2)
            {
                await firstRelease.Task;
                account.SetSilently(new OnlineAccountSnapshot(
                    providerKey,
                    OnlineProviderAuthState.Disconnected,
                    "stale"));
                return null;
            }

            account.SetSilently(new OnlineAccountSnapshot(
                providerKey,
                OnlineProviderAuthState.Authenticated,
                "latest"));
            return new OnlineProviderSession(providerKey, new Dictionary<string, string>());
        };
        using var viewModel = new OnlineAccountSettingsViewModel(account);

        var first = viewModel.RefreshAccountsAsync();
        while (Volatile.Read(ref requestNumber) < 2)
        {
            await Task.Yield();
        }

        await viewModel.RefreshAccountsAsync();
        firstRelease.TrySetResult();
        await first;

        Assert.Equal("latest", viewModel.Netease.DisplayName);
        Assert.Equal("latest", viewModel.Qq.DisplayName);
    }

    private sealed class FakeAccountService : IOnlineAccountService
    {
        private readonly Dictionary<string, OnlineAccountSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<OnlineAccountSnapshot>? AccountChanged;

        public string? LastSignedOutProvider { get; private set; }

        public List<string> SessionRequests { get; } = [];

        public Func<string, CancellationToken, Task<OnlineProviderSession?>>? SessionLoader { get; set; }

        public void Set(OnlineAccountSnapshot snapshot)
        {
            _snapshots[snapshot.ProviderKey] = snapshot;
            AccountChanged?.Invoke(this, snapshot);
        }

        public void SetSilently(OnlineAccountSnapshot snapshot) => _snapshots[snapshot.ProviderKey] = snapshot;

        public OnlineAccountSnapshot GetSnapshot(string providerKey) =>
            _snapshots.TryGetValue(providerKey, out var snapshot)
                ? snapshot
                : new OnlineAccountSnapshot(providerKey, OnlineProviderAuthState.Disconnected);

        public Task SignOutAsync(string providerKey, CancellationToken cancellationToken)
        {
            LastSignedOutProvider = providerKey;
            Set(new OnlineAccountSnapshot(providerKey, OnlineProviderAuthState.Disconnected));
            return Task.CompletedTask;
        }

        public Task<OnlineLoginChallenge> CreateChallengeAsync(string providerKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OnlineAccountSnapshot> PollAsync(string providerKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OnlineProviderSession?> GetSessionAsync(string providerKey, CancellationToken cancellationToken)
        {
            lock (SessionRequests)
            {
                SessionRequests.Add(providerKey);
            }

            return SessionLoader?.Invoke(providerKey, cancellationToken)
                ?? Task.FromResult<OnlineProviderSession?>(null);
        }

        public Task<OnlineProviderSession?> HandleAuthenticationFailureAsync(string providerKey, CancellationToken cancellationToken) =>
            Task.FromResult<OnlineProviderSession?>(null);

        public Task InvalidateSessionAsync(string providerKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
