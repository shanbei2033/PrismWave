using PrismWave_WinUI.Infrastructure.Navigation;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class CoverNavigationCoordinatorTests
{
    [Fact]
    public void InitialNavigation_CommitsWithoutAnimation()
    {
        var coordinator = CreateLoadedCoordinator();

        var request = coordinator.RequestNavigation("Home");

        Assert.Equal(CoverNavigationIntentKind.NavigateInitial, request.Kind);
        Assert.Equal("Home", request.Route);

        var completed = coordinator.NavigationSucceeded(request.Revision);

        Assert.Equal(CoverNavigationIntentKind.RestoreCurrent, completed.Kind);
        Assert.Equal("Home", coordinator.CurrentRoute);
        Assert.Equal(CoverNavigationState.Idle, coordinator.State);
    }

    [Fact]
    public void SameTarget_WhenIdle_DoesNothing()
    {
        var coordinator = CreateCoordinatorWithCurrentRoute("Home");
        var revision = coordinator.Revision;

        var intent = coordinator.RequestNavigation("Home");

        Assert.Equal(CoverNavigationIntentKind.None, intent.Kind);
        Assert.Equal(revision, coordinator.Revision);
        Assert.Equal("Home", coordinator.CurrentRoute);
    }

    [Fact]
    public void SameTarget_WhenActive_DoesNotRestartPreparation()
    {
        var coordinator = CreateCoordinatorWithCurrentRoute("Home");
        var preparation = coordinator.RequestNavigation("Search");

        var repeated = coordinator.RequestNavigation("Search");

        Assert.Equal(CoverNavigationIntentKind.None, repeated.Kind);
        Assert.Equal(preparation.Revision, coordinator.Revision);
        Assert.Equal("Search", coordinator.ActiveRoute);
        Assert.Equal(CoverNavigationState.Preparing, coordinator.State);
    }

    [Fact]
    public void SupersedingAnimation_KeepsOnlyLatestTarget()
    {
        var coordinator = CreateCoordinatorWithCurrentRoute("Home");
        var search = coordinator.RequestNavigation("Search");
        Assert.Equal(
            CoverNavigationIntentKind.StartAnimation,
            coordinator.IncomingReady(search.Revision, 1200).Kind);

        var completion = coordinator.RequestNavigation("Albums");
        var coalesced = coordinator.RequestNavigation("Artists");

        Assert.Equal(CoverNavigationIntentKind.CompleteTransition, completion.Kind);
        Assert.Equal(CoverTransitionCompletionReason.Superseded, completion.CompletionReason);
        Assert.Equal(CoverNavigationIntentKind.None, coalesced.Kind);

        var latest = coordinator.TransitionVisualCompleted(search.Revision);

        Assert.Equal("Search", coordinator.CurrentRoute);
        Assert.Equal(CoverNavigationIntentKind.PrepareIncoming, latest.Kind);
        Assert.Equal("Artists", latest.Route);
        Assert.Equal("Artists", coordinator.ActiveRoute);
    }

    [Fact]
    public void StaleRevisionCallbacks_DoNotChangeCurrentPreparation()
    {
        var coordinator = CreateCoordinatorWithCurrentRoute("Home");
        var stale = coordinator.RequestNavigation("Search");
        var current = coordinator.RequestNavigation("Albums");

        var staleReady = coordinator.IncomingReady(stale.Revision, 1200);
        var staleFailure = coordinator.NavigationFailed(stale.Revision);

        Assert.Equal(CoverNavigationIntentKind.PrepareIncoming, current.Kind);
        Assert.Equal(CoverNavigationIntentKind.None, staleReady.Kind);
        Assert.Equal(CoverNavigationIntentKind.None, staleFailure.Kind);
        Assert.Equal("Home", coordinator.CurrentRoute);
        Assert.Equal("Albums", coordinator.ActiveRoute);
        Assert.Equal(current.Revision, coordinator.Revision);
    }

    [Fact]
    public void ZeroWidth_KeepsReadyIncomingPagePending()
    {
        var coordinator = CreateCoordinatorWithCurrentRoute("Home");
        var preparation = coordinator.RequestNavigation("Search");

        var pending = coordinator.IncomingReady(preparation.Revision, 0);

        Assert.Equal(CoverNavigationIntentKind.None, pending.Kind);
        Assert.Equal(CoverNavigationState.Preparing, coordinator.State);

        var start = coordinator.HostWidthChanged(1440);

        Assert.Equal(CoverNavigationIntentKind.StartAnimation, start.Kind);
        Assert.Equal(1440, start.HostWidth);
        Assert.Equal(preparation.Revision, start.Revision);
    }

    [Fact]
    public void Unload_InvalidatesCallbacksAndResetsNavigation()
    {
        var coordinator = CreateCoordinatorWithCurrentRoute("Home");
        var preparation = coordinator.RequestNavigation("Search");

        var reset = coordinator.Unload();

        Assert.Equal(CoverNavigationIntentKind.Reset, reset.Kind);
        Assert.False(coordinator.IsLoaded);
        Assert.Null(coordinator.CurrentRoute);
        Assert.Null(coordinator.ActiveRoute);
        Assert.Equal(CoverNavigationState.Unloaded, coordinator.State);
        Assert.Equal(CoverNavigationIntentKind.None, coordinator.IncomingReady(preparation.Revision, 1200).Kind);
        Assert.Equal(CoverNavigationIntentKind.None, coordinator.RequestNavigation("Library").Kind);

        coordinator.Load();
        Assert.Equal(CoverNavigationIntentKind.NavigateInitial, coordinator.RequestNavigation("Library").Kind);
    }

    [Fact]
    public void ResizeDuringAnimation_RequestsImmediateCompletion()
    {
        var coordinator = CreateCoordinatorWithCurrentRoute("Home");
        var preparation = coordinator.RequestNavigation("Search");
        coordinator.IncomingReady(preparation.Revision, 1280);

        var completion = coordinator.HostWidthChanged(1600);

        Assert.Equal(CoverNavigationIntentKind.CompleteTransition, completion.Kind);
        Assert.Equal(CoverTransitionCompletionReason.Resized, completion.CompletionReason);
        Assert.Equal(preparation.Revision, completion.Revision);
    }

    [Fact]
    public void FailedNavigation_RollsBackToCurrentRoute()
    {
        var coordinator = CreateCoordinatorWithCurrentRoute("Home");
        var preparation = coordinator.RequestNavigation("Search");

        var rollback = coordinator.NavigationFailed(preparation.Revision);

        Assert.Equal(CoverNavigationIntentKind.RestoreCurrent, rollback.Kind);
        Assert.Equal("Home", rollback.RollbackRoute);
        Assert.Equal("Home", coordinator.CurrentRoute);
        Assert.Null(coordinator.ActiveRoute);
        Assert.Equal(CoverNavigationState.Idle, coordinator.State);
    }

    private static CoverNavigationCoordinator CreateLoadedCoordinator()
    {
        var coordinator = new CoverNavigationCoordinator();
        coordinator.Load();
        return coordinator;
    }

    private static CoverNavigationCoordinator CreateCoordinatorWithCurrentRoute(string route)
    {
        var coordinator = CreateLoadedCoordinator();
        var initial = coordinator.RequestNavigation(route);
        coordinator.NavigationSucceeded(initial.Revision);
        return coordinator;
    }
}
