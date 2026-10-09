namespace PrismWave_WinUI.Services.Implementations;

public enum OnlineProviderFailureKind
{
    NetworkOrProtocol,
    TrackUnavailable,
    Cancelled
}

public sealed class OnlineProviderHealthTracker
{
    private const int FailureThreshold = 3;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, ProviderHealth> _providers = new(StringComparer.OrdinalIgnoreCase);

    public OnlineProviderHealthTracker(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool CanRequest(string provider)
    {
        lock (_gate)
        {
            if (!_providers.TryGetValue(provider, out var health) || health.CooldownUntil is null)
            {
                return true;
            }

            if (health.CooldownUntil <= _timeProvider.GetUtcNow())
            {
                _providers[provider] = new ProviderHealth(0, null);
                return true;
            }

            return false;
        }
    }

    public void ReportSuccess(string provider)
    {
        lock (_gate)
        {
            _providers[provider] = new ProviderHealth(0, null);
        }
    }

    public void ReportFailure(string provider, OnlineProviderFailureKind kind)
    {
        if (kind != OnlineProviderFailureKind.NetworkOrProtocol)
        {
            return;
        }

        lock (_gate)
        {
            var current = _providers.GetValueOrDefault(provider) ?? new ProviderHealth(0, null);
            var failures = current.ConsecutiveFailures + 1;
            _providers[provider] = new ProviderHealth(
                failures,
                failures >= FailureThreshold ? _timeProvider.GetUtcNow() + Cooldown : null);
        }
    }

    private sealed record ProviderHealth(int ConsecutiveFailures, DateTimeOffset? CooldownUntil);
}
