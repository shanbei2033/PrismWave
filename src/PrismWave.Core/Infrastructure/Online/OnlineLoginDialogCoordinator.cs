using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Infrastructure.Online;

public sealed class OnlineLoginDialogCoordinator : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IOnlineAccountService _accountService;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private CancellationTokenSource? _runCancellation;
    private bool _disposed;

    public OnlineLoginDialogCoordinator(
        IOnlineAccountService accountService,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _accountService = accountService;
        _delay = delay ?? Task.Delay;
    }

    public event Action<OnlineLoginChallenge>? ChallengeChanged;

    public event Action<OnlineAccountSnapshot>? SnapshotChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _runCancellation is not null;
            }
        }
    }

    public string? ActiveProviderKey { get; private set; }

    public async Task<bool> RunAsync(
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource runCancellation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runCancellation is not null)
            {
                return false;
            }

            runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runCancellation = runCancellation;
            ActiveProviderKey = providerKey;
        }

        try
        {
            var challenge = await _accountService
                .CreateChallengeAsync(providerKey, runCancellation.Token)
                .ConfigureAwait(false);
            ChallengeChanged?.Invoke(challenge);

            while (!runCancellation.IsCancellationRequested)
            {
                await _delay(PollInterval, runCancellation.Token).ConfigureAwait(false);
                var snapshot = await _accountService
                    .PollAsync(providerKey, runCancellation.Token)
                    .ConfigureAwait(false);
                SnapshotChanged?.Invoke(snapshot);

                if (IsTerminal(snapshot.State))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_runCancellation, runCancellation))
                {
                    _runCancellation = null;
                    ActiveProviderKey = null;
                }
            }

            runCancellation.Dispose();
        }

        return true;
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _runCancellation?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runCancellation?.Cancel();
        }
    }

    private static bool IsTerminal(OnlineProviderAuthState state) => state is
        OnlineProviderAuthState.Authenticated
        or OnlineProviderAuthState.Expired
        or OnlineProviderAuthState.Failed
        or OnlineProviderAuthState.Disconnected;
}
