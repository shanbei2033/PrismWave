using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Settings;

public sealed partial class OnlineAccountSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IOnlineAccountService _accountService;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly object _refreshGate = new();
    private OnlineAccountCardViewModel _netease;
    private OnlineAccountCardViewModel _qq;
    private CancellationTokenSource? _refreshCancellation;
    private long _refreshRevision;
    private bool _isLoginEnabled;
    private bool _disposed;

    public OnlineAccountSettingsViewModel(IOnlineAccountService accountService)
    {
        _accountService = accountService;
        _synchronizationContext = SynchronizationContext.Current;
        _netease = OnlineAccountCardViewModel.FromSnapshot(
            "NetEase Cloud Music",
            accountService.GetSnapshot("netease"));
        _qq = OnlineAccountCardViewModel.FromSnapshot(
            "QQ Music",
            accountService.GetSnapshot("qq"));
        _accountService.AccountChanged += AccountService_AccountChanged;
    }

    public OnlineAccountCardViewModel Netease
    {
        get => _netease;
        private set => SetProperty(ref _netease, value);
    }

    public OnlineAccountCardViewModel Qq
    {
        get => _qq;
        private set => SetProperty(ref _qq, value);
    }

    public bool IsLoginEnabled
    {
        get => _isLoginEnabled;
        set => SetProperty(ref _isLoginEnabled, value);
    }

    public async Task RefreshAccountsAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource refreshCancellation;
        CancellationTokenSource? previousCancellation;
        long revision;
        lock (_refreshGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            revision = ++_refreshRevision;
            refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            previousCancellation = _refreshCancellation;
            _refreshCancellation = refreshCancellation;
        }

        previousCancellation?.Cancel();

        try
        {
            await Task.WhenAll(
                RestoreProviderSessionAsync("netease", refreshCancellation.Token),
                RestoreProviderSessionAsync("qq", refreshCancellation.Token));

            lock (_refreshGate)
            {
                if (_disposed || revision != _refreshRevision || refreshCancellation.IsCancellationRequested)
                {
                    return;
                }
            }

            ApplySnapshot(_accountService.GetSnapshot("netease"));
            ApplySnapshot(_accountService.GetSnapshot("qq"));
        }
        catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_refreshGate)
            {
                if (ReferenceEquals(_refreshCancellation, refreshCancellation))
                {
                    _refreshCancellation = null;
                }
            }

            refreshCancellation.Dispose();
        }
    }

    [RelayCommand]
    private async Task SignOutAsync(string? providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return;
        }

        await _accountService.SignOutAsync(providerKey, CancellationToken.None);
        ApplySnapshot(_accountService.GetSnapshot(providerKey));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _accountService.AccountChanged -= AccountService_AccountChanged;
        lock (_refreshGate)
        {
            _refreshRevision++;
            _refreshCancellation?.Cancel();
        }
    }

    private void AccountService_AccountChanged(object? sender, OnlineAccountSnapshot snapshot)
    {
        if (_synchronizationContext is null
            || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            ApplySnapshot(snapshot);
            return;
        }

        _synchronizationContext.Post(static state =>
        {
            var update = (SnapshotUpdate)state!;
            update.Owner.ApplySnapshot(update.Snapshot);
        }, new SnapshotUpdate(this, snapshot));
    }

    private void ApplySnapshot(OnlineAccountSnapshot snapshot)
    {
        if (string.Equals(snapshot.ProviderKey, "netease", StringComparison.OrdinalIgnoreCase))
        {
            Netease = OnlineAccountCardViewModel.FromSnapshot("NetEase Cloud Music", snapshot);
        }
        else if (string.Equals(snapshot.ProviderKey, "qq", StringComparison.OrdinalIgnoreCase))
        {
            Qq = OnlineAccountCardViewModel.FromSnapshot("QQ Music", snapshot);
        }
    }

    private async Task RestoreProviderSessionAsync(string providerKey, CancellationToken cancellationToken)
    {
        try
        {
            await _accountService.GetSessionAsync(providerKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Provider initialization is best effort. Never surface credential-bearing exceptions.
        }
    }

    private sealed record SnapshotUpdate(
        OnlineAccountSettingsViewModel Owner,
        OnlineAccountSnapshot Snapshot);
}

public sealed record OnlineAccountCardViewModel(
    string ProviderKey,
    string ProviderName,
    OnlineProviderAuthState State,
    string DisplayName,
    string? AvatarUrl,
    string StatusText,
    bool IsAuthenticated,
    bool CanSignOut)
{
    internal static OnlineAccountCardViewModel FromSnapshot(
        string providerName,
        OnlineAccountSnapshot snapshot)
    {
        var status = !string.IsNullOrWhiteSpace(snapshot.StatusMessage)
            ? snapshot.StatusMessage
            : snapshot.State switch
            {
                OnlineProviderAuthState.WaitingForScan => "Waiting for scan",
                OnlineProviderAuthState.Scanned => "Scanned · confirm on your phone",
                OnlineProviderAuthState.Authenticated => "Connected",
                OnlineProviderAuthState.Expired => "Session expired",
                OnlineProviderAuthState.Failed => "Connection failed",
                _ => "Not connected",
            };
        return new OnlineAccountCardViewModel(
            snapshot.ProviderKey,
            providerName,
            snapshot.State,
            string.IsNullOrWhiteSpace(snapshot.DisplayName) ? providerName : snapshot.DisplayName,
            NormalizePublicAvatarUrl(snapshot.AvatarUrl),
            status,
            snapshot.State == OnlineProviderAuthState.Authenticated,
            CanSignOut: true);
    }

    private static string? NormalizePublicAvatarUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        return new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri.AbsoluteUri.TrimEnd('/');
    }
}
