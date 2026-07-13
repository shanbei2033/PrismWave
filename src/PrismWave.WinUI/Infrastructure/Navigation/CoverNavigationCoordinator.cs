namespace PrismWave_WinUI.Infrastructure.Navigation;

public enum CoverNavigationState
{
    Unloaded,
    Idle,
    Initializing,
    Preparing,
    Animating,
    Completing
}

public enum CoverNavigationIntentKind
{
    None,
    NavigateInitial,
    PrepareIncoming,
    StartAnimation,
    CompleteTransition,
    RestoreCurrent,
    Reset
}

public enum CoverTransitionCompletionReason
{
    None,
    Finished,
    Superseded,
    Resized
}

public readonly record struct CoverNavigationIntent(
    CoverNavigationIntentKind Kind,
    long Revision = 0,
    string? Route = null,
    string? RollbackRoute = null,
    double HostWidth = 0,
    CoverTransitionCompletionReason CompletionReason = CoverTransitionCompletionReason.None);

public sealed class CoverNavigationCoordinator
{
    private string? _latestRoute;
    private bool _incomingReady;

    public bool IsLoaded { get; private set; }
    public long Revision { get; private set; }
    public string? CurrentRoute { get; private set; }
    public string? ActiveRoute { get; private set; }
    public CoverNavigationState State { get; private set; } = CoverNavigationState.Unloaded;

    public void Load()
    {
        if (IsLoaded)
        {
            return;
        }

        IsLoaded = true;
        State = CoverNavigationState.Idle;
    }

    public CoverNavigationIntent Unload()
    {
        Revision++;
        IsLoaded = false;
        CurrentRoute = null;
        ActiveRoute = null;
        _latestRoute = null;
        _incomingReady = false;
        State = CoverNavigationState.Unloaded;
        return new CoverNavigationIntent(CoverNavigationIntentKind.Reset, Revision);
    }

    public CoverNavigationIntent RequestNavigation(string route)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(route))
        {
            return default;
        }

        switch (State)
        {
            case CoverNavigationState.Idle:
                if (string.Equals(route, CurrentRoute, StringComparison.Ordinal))
                {
                    return default;
                }

                return BeginNavigation(route, CurrentRoute is null);

            case CoverNavigationState.Initializing:
                return string.Equals(route, ActiveRoute, StringComparison.Ordinal)
                    ? default
                    : BeginNavigation(route, initial: true);

            case CoverNavigationState.Preparing:
                return string.Equals(route, ActiveRoute, StringComparison.Ordinal)
                    ? default
                    : BeginNavigation(route, initial: false);

            case CoverNavigationState.Animating:
                if (string.Equals(route, ActiveRoute, StringComparison.Ordinal))
                {
                    _latestRoute = null;
                    return default;
                }

                _latestRoute = route;
                State = CoverNavigationState.Completing;
                return CompleteTransition(CoverTransitionCompletionReason.Superseded);

            case CoverNavigationState.Completing:
                _latestRoute = string.Equals(route, ActiveRoute, StringComparison.Ordinal)
                    ? null
                    : route;
                return default;

            default:
                return default;
        }
    }

    public CoverNavigationIntent NavigationSucceeded(long revision)
    {
        if (!IsCurrent(revision, CoverNavigationState.Initializing))
        {
            return default;
        }

        CurrentRoute = ActiveRoute;
        ActiveRoute = null;
        State = CoverNavigationState.Idle;
        return new CoverNavigationIntent(
            CoverNavigationIntentKind.RestoreCurrent,
            revision,
            CurrentRoute);
    }

    public CoverNavigationIntent IncomingReady(long revision, double hostWidth)
    {
        if (!IsCurrent(revision, CoverNavigationState.Preparing))
        {
            return default;
        }

        _incomingReady = true;
        return hostWidth > 0
            ? StartAnimation(hostWidth)
            : default;
    }

    public CoverNavigationIntent HostWidthChanged(double hostWidth)
    {
        if (!IsLoaded)
        {
            return default;
        }

        if (State == CoverNavigationState.Animating)
        {
            State = CoverNavigationState.Completing;
            return CompleteTransition(CoverTransitionCompletionReason.Resized);
        }

        if (State == CoverNavigationState.Preparing && _incomingReady && hostWidth > 0)
        {
            return StartAnimation(hostWidth);
        }

        return default;
    }

    public CoverNavigationIntent AnimationCompleted(long revision)
    {
        if (!IsCurrent(revision, CoverNavigationState.Animating))
        {
            return default;
        }

        State = CoverNavigationState.Completing;
        return CompleteTransition(CoverTransitionCompletionReason.Finished);
    }

    public CoverNavigationIntent TransitionVisualCompleted(long revision)
    {
        if (!IsCurrent(revision, CoverNavigationState.Completing))
        {
            return default;
        }

        CurrentRoute = ActiveRoute;
        ActiveRoute = null;
        _incomingReady = false;

        var latestRoute = _latestRoute;
        _latestRoute = null;
        if (!string.IsNullOrWhiteSpace(latestRoute) &&
            !string.Equals(latestRoute, CurrentRoute, StringComparison.Ordinal))
        {
            return BeginNavigation(latestRoute, initial: false);
        }

        State = CoverNavigationState.Idle;
        return new CoverNavigationIntent(
            CoverNavigationIntentKind.RestoreCurrent,
            revision,
            CurrentRoute);
    }

    public CoverNavigationIntent NavigationFailed(long revision)
    {
        if (!IsLoaded || revision != Revision ||
            State is not (CoverNavigationState.Initializing or CoverNavigationState.Preparing))
        {
            return default;
        }

        var rollbackRoute = CurrentRoute;
        Revision++;
        ActiveRoute = null;
        _latestRoute = null;
        _incomingReady = false;
        State = CoverNavigationState.Idle;
        return new CoverNavigationIntent(
            CoverNavigationIntentKind.RestoreCurrent,
            revision,
            rollbackRoute,
            rollbackRoute);
    }

    private CoverNavigationIntent BeginNavigation(string route, bool initial)
    {
        Revision++;
        ActiveRoute = route;
        _latestRoute = null;
        _incomingReady = false;
        State = initial
            ? CoverNavigationState.Initializing
            : CoverNavigationState.Preparing;
        return new CoverNavigationIntent(
            initial
                ? CoverNavigationIntentKind.NavigateInitial
                : CoverNavigationIntentKind.PrepareIncoming,
            Revision,
            route);
    }

    private CoverNavigationIntent StartAnimation(double hostWidth)
    {
        State = CoverNavigationState.Animating;
        return new CoverNavigationIntent(
            CoverNavigationIntentKind.StartAnimation,
            Revision,
            ActiveRoute,
            HostWidth: hostWidth);
    }

    private CoverNavigationIntent CompleteTransition(CoverTransitionCompletionReason reason) =>
        new(
            CoverNavigationIntentKind.CompleteTransition,
            Revision,
            ActiveRoute,
            CompletionReason: reason);

    private bool IsCurrent(long revision, CoverNavigationState state) =>
        IsLoaded && revision == Revision && State == state;
}
