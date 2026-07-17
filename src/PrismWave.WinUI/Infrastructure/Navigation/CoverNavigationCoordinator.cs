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
    CoverTransitionCompletionReason CompletionReason = CoverTransitionCompletionReason.None,
    ShellNavigationKind NavigationKind = ShellNavigationKind.Initial);

public sealed class CoverNavigationCoordinator
{
    private ShellNavigationRequest? _latestRequest;
    private bool _incomingReady;

    public bool IsLoaded { get; private set; }
    public long Revision { get; private set; }
    public string? CurrentRoute { get; private set; }
    public ShellNavigationRequest? ActiveRequest { get; private set; }
    public string? ActiveRoute => ActiveRequest?.Route;
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
        ActiveRequest = null;
        _latestRequest = null;
        _incomingReady = false;
        State = CoverNavigationState.Unloaded;
        return new CoverNavigationIntent(CoverNavigationIntentKind.Reset, Revision);
    }

    public CoverNavigationIntent RequestNavigation(ShellNavigationRequest request)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(request.Route))
        {
            return default;
        }

        switch (State)
        {
            case CoverNavigationState.Idle:
                if (string.Equals(request.Route, CurrentRoute, StringComparison.Ordinal))
                {
                    return default;
                }

                return BeginNavigation(request, CurrentRoute is null);

            case CoverNavigationState.Initializing:
                return string.Equals(request.Route, ActiveRoute, StringComparison.Ordinal)
                    ? default
                    : BeginNavigation(request, initial: true);

            case CoverNavigationState.Preparing:
                return string.Equals(request.Route, ActiveRoute, StringComparison.Ordinal)
                    ? default
                    : BeginNavigation(request, initial: false);

            case CoverNavigationState.Animating:
                if (string.Equals(request.Route, ActiveRoute, StringComparison.Ordinal))
                {
                    _latestRequest = null;
                    return default;
                }

                _latestRequest = request;
                State = CoverNavigationState.Completing;
                return CompleteTransition(CoverTransitionCompletionReason.Superseded);

            case CoverNavigationState.Completing:
                _latestRequest = string.Equals(request.Route, ActiveRoute, StringComparison.Ordinal)
                    ? null
                    : request;
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

        var completedRequest = ActiveRequest;
        CurrentRoute = ActiveRoute;
        ActiveRequest = null;
        State = CoverNavigationState.Idle;
        return new CoverNavigationIntent(
            CoverNavigationIntentKind.RestoreCurrent,
            revision,
            CurrentRoute,
            NavigationKind: completedRequest?.Kind ?? ShellNavigationKind.Initial);
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

        var completedRequest = ActiveRequest;
        CurrentRoute = ActiveRoute;
        ActiveRequest = null;
        _incomingReady = false;

        var latestRequest = _latestRequest;
        _latestRequest = null;
        if (latestRequest is { } request &&
            !string.IsNullOrWhiteSpace(request.Route) &&
            !string.Equals(request.Route, CurrentRoute, StringComparison.Ordinal))
        {
            return BeginNavigation(request, initial: false);
        }

        State = CoverNavigationState.Idle;
        return new CoverNavigationIntent(
            CoverNavigationIntentKind.RestoreCurrent,
            revision,
            CurrentRoute,
            NavigationKind: completedRequest?.Kind ?? ShellNavigationKind.Initial);
    }

    public CoverNavigationIntent NavigationFailed(long revision)
    {
        if (!IsLoaded || revision != Revision ||
            State is not (CoverNavigationState.Initializing or CoverNavigationState.Preparing))
        {
            return default;
        }

        var failedRequest = ActiveRequest;
        var rollbackRoute = CurrentRoute;
        Revision++;
        ActiveRequest = null;
        _latestRequest = null;
        _incomingReady = false;
        State = CoverNavigationState.Idle;
        return new CoverNavigationIntent(
            CoverNavigationIntentKind.RestoreCurrent,
            revision,
            rollbackRoute,
            rollbackRoute,
            NavigationKind: failedRequest?.Kind ?? ShellNavigationKind.Initial);
    }

    private CoverNavigationIntent BeginNavigation(ShellNavigationRequest request, bool initial)
    {
        Revision++;
        ActiveRequest = request;
        _latestRequest = null;
        _incomingReady = false;
        State = initial
            ? CoverNavigationState.Initializing
            : CoverNavigationState.Preparing;
        return new CoverNavigationIntent(
            initial
                ? CoverNavigationIntentKind.NavigateInitial
                : CoverNavigationIntentKind.PrepareIncoming,
            Revision,
            request.Route,
            NavigationKind: request.Kind);
    }

    private CoverNavigationIntent StartAnimation(double hostWidth)
    {
        State = CoverNavigationState.Animating;
        return new CoverNavigationIntent(
            CoverNavigationIntentKind.StartAnimation,
            Revision,
            ActiveRoute,
            HostWidth: hostWidth,
            NavigationKind: ActiveRequest?.Kind ?? ShellNavigationKind.Initial);
    }

    private CoverNavigationIntent CompleteTransition(CoverTransitionCompletionReason reason) =>
        new(
            CoverNavigationIntentKind.CompleteTransition,
            Revision,
            ActiveRoute,
            CompletionReason: reason,
            NavigationKind: ActiveRequest?.Kind ?? ShellNavigationKind.Initial);

    private bool IsCurrent(long revision, CoverNavigationState state) =>
        IsLoaded && revision == Revision && State == state;
}
