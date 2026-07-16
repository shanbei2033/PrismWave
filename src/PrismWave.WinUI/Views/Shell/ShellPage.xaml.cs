using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Infrastructure.Animation;
using PrismWave_WinUI.Infrastructure.Navigation;
using PrismWave_WinUI.Views.Hits;
using PrismWave_WinUI.Views.Home;
using PrismWave_WinUI.Views.Library;
using PrismWave_WinUI.Views.Player;
using PrismWave_WinUI.Views.Search;
using PrismWave_WinUI.Views.Settings;

namespace PrismWave_WinUI.Views.Shell;

public sealed partial class ShellPage : Page
{
    private const double FullPlayTransitionDurationMilliseconds = 280;

    private readonly CoverNavigationCoordinator _navigationCoordinator = new();
    private readonly BackNavigationPageCache<Page> _backPageCache = new();
    private bool _isSynchronizingSelection = true;
    private bool _isShellLoaded;
    private Frame _currentContentFrame = null!;
    private Frame _incomingContentFrame = null!;
    private FrameworkElement? _incomingLoadedElement;
    private long _incomingLoadedRevision;
    private CompositionScopedBatch? _activeAnimationBatch;
    private CompositionScopedBatch? _fullPlayExitBatch;
    private long _activeAnimationRevision;
    private long _navigatingRevision;
    private Exception? _navigationFailedException;
    private bool _isFullPlayVisible;
    private bool _animationsEnabled = true;

    public ShellPage()
    {
        StartupLog.Write("ShellPage constructor");
        InitializeComponent();
        _currentContentFrame = PrimaryContentFrame;
        _incomingContentFrame = SecondaryContentFrame;
        DataContext = App.Services.Shell;
        Loaded += ShellPage_Loaded;
        Unloaded += ShellPage_Unloaded;
        _isSynchronizingSelection = false;
        StartupLog.Write("ShellPage initialized");
    }

    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isShellLoaded)
        {
            return;
        }

        _isShellLoaded = true;
        _animationsEnabled = ResolveAnimationsEnabled();
        App.Services.Shell.NavigationRequested += ShellViewModel_NavigationRequested;
        PrimaryContentFrame.NavigationFailed += ContentFrame_NavigationFailed;
        SecondaryContentFrame.NavigationFailed += ContentFrame_NavigationFailed;
        _navigationCoordinator.Load();

        if (AppNavigationView.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Content = "设置";
            AutomationProperties.SetName(settingsItem, "设置");
            ToolTipService.SetToolTip(settingsItem, "设置");
        }

        ProcessNavigationRequest(new ShellNavigationRequest(
            App.Services.Shell.SelectedRoute,
            ShellNavigationKind.Initial));
    }

    private void ShellPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_isShellLoaded)
        {
            return;
        }

        _isShellLoaded = false;
        App.Services.Shell.NavigationRequested -= ShellViewModel_NavigationRequested;
        PrimaryContentFrame.NavigationFailed -= ContentFrame_NavigationFailed;
        SecondaryContentFrame.NavigationFailed -= ContentFrame_NavigationFailed;
        _navigationCoordinator.Unload();
        _backPageCache.Clear();
        DetachIncomingLoadedHandler();
        DetachAnimationBatch();
        ResetFullPlayOverlay();
        ResetFrame(PrimaryContentFrame);
        ResetFrame(SecondaryContentFrame);
        _currentContentFrame = PrimaryContentFrame;
        _incomingContentFrame = SecondaryContentFrame;
        TransitionFocusTarget.IsTabStop = false;
        _navigatingRevision = 0;
        _navigationFailedException = null;
    }

    private void ShellViewModel_NavigationRequested(object? sender, ShellNavigationRequest request) =>
        ProcessNavigationRequest(request);

    private void ProcessNavigationRequest(ShellNavigationRequest request)
    {
        if (!_isShellLoaded)
        {
            return;
        }

        if (request.Route == "FullPlay")
        {
            ShowFullPlayOverlay();
            return;
        }

        if (_isFullPlayVisible && request.Kind == ShellNavigationKind.Back)
        {
            SynchronizeNavigationSelection(request.Route);
            HideFullPlayOverlay();
            return;
        }

        if (_isFullPlayVisible)
        {
            ResetFullPlayOverlay();
        }

        if (request.Kind == ShellNavigationKind.Primary)
        {
            _backPageCache.Clear();
        }

        SynchronizeNavigationSelection(request.Route);
        ExecuteIntent(_navigationCoordinator.RequestNavigation(request));
    }

    private void ShowFullPlayOverlay()
    {
        if (_isFullPlayVisible)
        {
            return;
        }

        DetachFullPlayExitBatch();
        _isFullPlayVisible = true;
        SetFullPlayImmersiveTitleBar(true);
        FullPlayFrame.Content = new FullPlayPage();
        FullPlayOverlay.Visibility = Visibility.Visible;
        FullPlayOverlay.IsHitTestVisible = true;
        FullPlayOverlay.Opacity = 1;
        AppNavigationView.IsHitTestVisible = false;
        ShellBottomPlayerBar.IsHitTestVisible = false;

        ElementCompositionPreview.SetIsTranslationEnabled(FullPlayOverlay, true);
        var visual = ElementCompositionPreview.GetElementVisual(FullPlayOverlay);
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation.Y");
        if (!_animationsEnabled)
        {
            visual.Opacity = 1;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            FullPlayFrame.Focus(FocusState.Programmatic);
            StartupLog.Write("navigation.fullplay.opened durationMs=0");
            return;
        }

        FullPlayOverlay.UpdateLayout();
        var startOffset = (float)Math.Max(1, FullPlayOverlay.ActualHeight);
        visual.Opacity = 1;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1.0f));
        var slideAnimation = compositor.CreateScalarKeyFrameAnimation();
        slideAnimation.InsertKeyFrame(0f, startOffset);
        slideAnimation.InsertKeyFrame(1f, 0f, easing);
        slideAnimation.Duration = TimeSpan.FromMilliseconds(
            FullPlayTransitionDurationMilliseconds);
        var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
        fadeAnimation.InsertKeyFrame(0f, 0.12f);
        fadeAnimation.InsertKeyFrame(1f, 1f, easing);
        fadeAnimation.Duration = slideAnimation.Duration;
        visual.StartAnimation("Translation.Y", slideAnimation);
        visual.StartAnimation("Opacity", fadeAnimation);
        FullPlayFrame.Focus(FocusState.Programmatic);
        StartupLog.Write(
            $"navigation.fullplay.opened durationMs={FullPlayTransitionDurationMilliseconds:0}");
    }

    private void HideFullPlayOverlay()
    {
        if (!_isFullPlayVisible)
        {
            return;
        }

        _isFullPlayVisible = false;
        FullPlayOverlay.IsHitTestVisible = false;
        AppNavigationView.IsHitTestVisible = true;
        ShellBottomPlayerBar.IsHitTestVisible = true;
        DetachFullPlayExitBatch();

        if (!_animationsEnabled)
        {
            ResetFullPlayOverlay();
            RestoreCurrentInputAndFocus();
            StartupLog.Write("navigation.fullplay.closed durationMs=0");
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(FullPlayOverlay);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation.Y");
        FullPlayOverlay.UpdateLayout();
        var endOffset = (float)Math.Max(1, FullPlayOverlay.ActualHeight);
        visual.Opacity = 0;
        visual.Properties.InsertVector3("Translation", new Vector3(0, endOffset, 0));
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.4f, 0.0f),
            new Vector2(1.0f, 1.0f));
        var slideAnimation = compositor.CreateScalarKeyFrameAnimation();
        slideAnimation.InsertKeyFrame(0f, 0f);
        slideAnimation.InsertKeyFrame(1f, endOffset, easing);
        slideAnimation.Duration = TimeSpan.FromMilliseconds(
            FullPlayTransitionDurationMilliseconds);
        var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
        fadeAnimation.InsertKeyFrame(0f, 1f);
        fadeAnimation.InsertKeyFrame(1f, 0.12f, easing);
        fadeAnimation.Duration = slideAnimation.Duration;
        _fullPlayExitBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _fullPlayExitBatch.Completed += FullPlayExitBatch_Completed;
        visual.StartAnimation("Translation.Y", slideAnimation);
        visual.StartAnimation("Opacity", fadeAnimation);
        _fullPlayExitBatch.End();
        StartupLog.Write(
            $"navigation.fullplay.closing durationMs={FullPlayTransitionDurationMilliseconds:0}");
    }

    private void FullPlayExitBatch_Completed(object sender, CompositionBatchCompletedEventArgs args)
    {
        DetachFullPlayExitBatch();
        if (!_isFullPlayVisible)
        {
            ResetFullPlayOverlay();
            RestoreCurrentInputAndFocus();
            StartupLog.Write("navigation.fullplay.closed");
        }
    }

    private void ResetFullPlayOverlay()
    {
        DetachFullPlayExitBatch();
        _isFullPlayVisible = false;
        SetFullPlayImmersiveTitleBar(false);
        var visual = ElementCompositionPreview.GetElementVisual(FullPlayOverlay);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation.Y");
        visual.Opacity = 1;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        FullPlayFrame.Content = null;
        FullPlayOverlay.Visibility = Visibility.Collapsed;
        FullPlayOverlay.IsHitTestVisible = false;
        FullPlayOverlay.Opacity = 0;
        AppNavigationView.IsHitTestVisible = true;
        ShellBottomPlayerBar.IsHitTestVisible = true;
    }

    private static void SetFullPlayImmersiveTitleBar(bool isImmersive)
    {
        if (App.Window is MainWindow mainWindow)
        {
            mainWindow.SetImmersiveTitleBar(isImmersive);
        }
    }

    private void DetachFullPlayExitBatch()
    {
        if (_fullPlayExitBatch is not null)
        {
            _fullPlayExitBatch.Completed -= FullPlayExitBatch_Completed;
        }

        _fullPlayExitBatch = null;
    }

    private void ExecuteIntent(CoverNavigationIntent intent)
    {
        if (!_isShellLoaded && intent.Kind != CoverNavigationIntentKind.Reset)
        {
            return;
        }

        switch (intent.Kind)
        {
            case CoverNavigationIntentKind.NavigateInitial:
                NavigateInitial(intent);
                break;
            case CoverNavigationIntentKind.PrepareIncoming:
                PrepareIncoming(intent);
                break;
            case CoverNavigationIntentKind.StartAnimation:
                BeginCoverAnimation(intent);
                break;
            case CoverNavigationIntentKind.CompleteTransition:
                CompleteTransitionVisual(intent);
                break;
            case CoverNavigationIntentKind.RestoreCurrent:
                RestoreCurrentInputAndFocus();
                break;
        }
    }

    private void NavigateInitial(CoverNavigationIntent intent)
    {
        if (!TryNavigateFrame(_currentContentFrame, ResolvePageType(intent.Route), intent.Revision, out var exception))
        {
            HandleNavigationFailure(intent, _currentContentFrame, exception);
            return;
        }

        _currentContentFrame.Visibility = Visibility.Visible;
        ExecuteIntent(_navigationCoordinator.NavigationSucceeded(intent.Revision));
    }

    private void PrepareIncoming(CoverNavigationIntent intent)
    {
        if (intent.NavigationKind is ShellNavigationKind.Initial or ShellNavigationKind.Primary)
        {
            _backPageCache.Clear();
        }

        StartupLog.Write($"navigation.cover.requested route={intent.Route} revision={intent.Revision}");
        DetachIncomingLoadedHandler();
        ResetFrame(_incomingContentFrame);
        PageTransitionHost.Children.Remove(_incomingContentFrame);
        PageTransitionHost.Children.Add(_incomingContentFrame);

        var restoredFromCache = false;
        if (intent.NavigationKind == ShellNavigationKind.Back &&
            !string.IsNullOrWhiteSpace(intent.Route) &&
            _backPageCache.TryPeek(intent.Route, out var cachedPage) &&
            cachedPage is not null)
        {
            try
            {
                _incomingContentFrame.Content = cachedPage;
                restoredFromCache = true;
                StartupLog.Write(
                    $"navigation.back.cache.restored route={intent.Route} revision={intent.Revision}");
            }
            catch (Exception cacheException)
            {
                StartupLog.Write(
                    $"navigation.back.cache.restore.failed route={intent.Route} revision={intent.Revision}",
                    cacheException);
                ResetFrame(_incomingContentFrame);
            }
        }

        if (!restoredFromCache &&
            !TryNavigateFrame(_incomingContentFrame, ResolvePageType(intent.Route), intent.Revision, out var exception))
        {
            HandleNavigationFailure(intent, _incomingContentFrame, exception);
            return;
        }

        if (_incomingContentFrame.Content is not FrameworkElement incomingContent)
        {
            HandleNavigationFailure(
                intent,
                _incomingContentFrame,
                new InvalidOperationException($"Route '{intent.Route}' did not create FrameworkElement content."));
            return;
        }

        var currentVisual = ElementCompositionPreview.GetElementVisual(_currentContentFrame);
        currentVisual.StopAnimation("Offset.X");
        currentVisual.Offset = Vector3.Zero;

        var incomingVisual = ElementCompositionPreview.GetElementVisual(_incomingContentFrame);
        incomingVisual.StopAnimation("Offset.X");
        incomingVisual.Offset = new Vector3((float)Math.Max(0, PageTransitionHost.ActualWidth), 0, 0);

        EnterTransitionInputLock();
        AttachIncomingLoadedHandler(incomingContent, intent.Revision);
        _incomingContentFrame.Visibility = Visibility.Visible;
        StartupLog.Write($"navigation.cover.prepared route={intent.Route} revision={intent.Revision}");

        if (_incomingLoadedElement is not null && incomingContent.IsLoaded)
        {
            DetachIncomingLoadedHandler();
            QueueIncomingReady(intent.Revision);
        }
    }

    private bool TryNavigateFrame(Frame frame, Type target, long revision, out Exception exception)
    {
        _navigatingRevision = revision;
        _navigationFailedException = null;
        Exception? thrownException = null;
        var navigated = false;
        try
        {
            navigated = frame.Navigate(target, null, new SuppressNavigationTransitionInfo());
        }
        catch (Exception caught)
        {
            thrownException = caught;
        }
        finally
        {
            _navigatingRevision = 0;
        }

        exception = thrownException ??
            _navigationFailedException ??
            new InvalidOperationException($"Frame.Navigate returned false for '{target.FullName}'.");
        return navigated && thrownException is null && _navigationFailedException is null;
    }

    private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs args)
    {
        if (!_isShellLoaded || _navigatingRevision == 0)
        {
            return;
        }

        _navigationFailedException = args.Exception;
        args.Handled = true;
    }

    private void HandleNavigationFailure(
        CoverNavigationIntent failedIntent,
        Frame failedFrame,
        Exception exception)
    {
        var rollback = _navigationCoordinator.NavigationFailed(failedIntent.Revision);
        if (rollback.Kind == CoverNavigationIntentKind.None)
        {
            return;
        }

        DetachIncomingLoadedHandler();
        DetachAnimationBatch();
        ResetFrame(failedFrame);
        if (!string.IsNullOrWhiteSpace(rollback.RollbackRoute))
        {
            App.Services.Shell.RollbackNavigation(
                rollback.RollbackRoute,
                new ShellNavigationRequest(
                    failedIntent.Route ?? string.Empty,
                    failedIntent.NavigationKind));
            SynchronizeNavigationSelection(rollback.RollbackRoute);
        }

        RestoreCurrentInputAndFocus();
        StartupLog.Write(
            $"navigation.cover.failed route={failedIntent.Route} revision={failedIntent.Revision}",
            exception);
    }

    private void AttachIncomingLoadedHandler(FrameworkElement element, long revision)
    {
        DetachIncomingLoadedHandler();
        _incomingLoadedElement = element;
        _incomingLoadedRevision = revision;
        element.Loaded += IncomingContent_Loaded;
    }

    private void IncomingContent_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isShellLoaded || !ReferenceEquals(sender, _incomingLoadedElement))
        {
            return;
        }

        var revision = _incomingLoadedRevision;
        DetachIncomingLoadedHandler();
        QueueIncomingReady(revision);
    }

    private void QueueIncomingReady(long revision)
    {
        void ReadyAfterLayout()
        {
            if (!_isShellLoaded || revision != _navigationCoordinator.Revision)
            {
                return;
            }

            if (_incomingContentFrame.Content is FrameworkElement content)
            {
                content.UpdateLayout();
            }

            ExecuteIntent(_navigationCoordinator.IncomingReady(
                revision,
                PageTransitionHost.ActualWidth));
        }

        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ReadyAfterLayout))
        {
            ReadyAfterLayout();
        }
    }

    private void BeginCoverAnimation(CoverNavigationIntent intent)
    {
        if (intent.HostWidth <= 0 || intent.Revision != _navigationCoordinator.Revision)
        {
            return;
        }

        DetachAnimationBatch();
        EnterTransitionInputLock();
        var compositor = ElementCompositionPreview.GetElementVisual(PageTransitionHost).Compositor;
        var incomingVisual = ElementCompositionPreview.GetElementVisual(_incomingContentFrame);
        incomingVisual.StopAnimation("Offset.X");
        incomingVisual.Offset = new Vector3((float)intent.HostWidth, 0, 0);

        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1.0f));
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, 0f, easing);
        animation.Duration = TimeSpan.FromMilliseconds(280);

        _activeAnimationRevision = intent.Revision;
        _activeAnimationBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _activeAnimationBatch.Completed += CoverAnimationBatch_Completed;
        incomingVisual.StartAnimation("Offset.X", animation);
        _activeAnimationBatch.End();
        StartupLog.Write($"navigation.cover.started route={intent.Route} revision={intent.Revision} durationMs=280");
    }

    private void CoverAnimationBatch_Completed(object sender, CompositionBatchCompletedEventArgs args)
    {
        var revision = _activeAnimationRevision;
        DetachAnimationBatch();

        void CompleteAfterAnimation()
        {
            if (!_isShellLoaded || revision != _navigationCoordinator.Revision)
            {
                return;
            }

            ExecuteIntent(_navigationCoordinator.AnimationCompleted(revision));
        }

        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CompleteAfterAnimation))
        {
            CompleteAfterAnimation();
        }
    }

    private void CompleteTransitionVisual(CoverNavigationIntent intent)
    {
        if (intent.Revision != _navigationCoordinator.Revision)
        {
            return;
        }

        DetachIncomingLoadedHandler();
        DetachAnimationBatch();
        var currentVisual = ElementCompositionPreview.GetElementVisual(_currentContentFrame);
        currentVisual.StopAnimation("Offset.X");
        currentVisual.Offset = Vector3.Zero;
        var incomingVisual = ElementCompositionPreview.GetElementVisual(_incomingContentFrame);
        incomingVisual.StopAnimation("Offset.X");
        incomingVisual.Offset = Vector3.Zero;

        var previousContentFrame = _currentContentFrame;
        var previousPage = previousContentFrame.Content as Page;
        var previousRoute = _navigationCoordinator.CurrentRoute;
        _currentContentFrame = _incomingContentFrame;
        _incomingContentFrame = previousContentFrame;
        _currentContentFrame.Visibility = Visibility.Visible;
        _currentContentFrame.IsHitTestVisible = false;

        if (intent.NavigationKind == ShellNavigationKind.Nested &&
            previousPage is not null &&
            !string.IsNullOrWhiteSpace(previousRoute))
        {
            _backPageCache.Push(previousRoute, previousPage);
            StartupLog.Write(
                $"navigation.back.cache.pushed route={previousRoute} depth={_backPageCache.Count}");
        }
        else if (intent.NavigationKind == ShellNavigationKind.Back &&
                 !string.IsNullOrWhiteSpace(intent.Route) &&
                 _backPageCache.TryPop(intent.Route, out _))
        {
            StartupLog.Write(
                $"navigation.back.cache.consumed route={intent.Route} depth={_backPageCache.Count}");
        }

        ResetFrame(_incomingContentFrame);
        QueueTransitionFinalization(intent);
    }

    private void QueueTransitionFinalization(CoverNavigationIntent completedIntent)
    {
        void FinalizeTransition()
        {
            if (!_isShellLoaded || completedIntent.Revision != _navigationCoordinator.Revision)
            {
                return;
            }

            var continuation = _navigationCoordinator.TransitionVisualCompleted(completedIntent.Revision);
            if (continuation.Kind == CoverNavigationIntentKind.None)
            {
                return;
            }

            if (completedIntent.CompletionReason == CoverTransitionCompletionReason.Superseded)
            {
                StartupLog.Write($"navigation.cover.superseded revision={completedIntent.Revision}");
            }
            else
            {
                StartupLog.Write(
                    $"navigation.cover.completed revision={completedIntent.Revision} reason={completedIntent.CompletionReason}");
            }

            ExecuteIntent(continuation);
        }

        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FinalizeTransition))
        {
            FinalizeTransition();
        }
    }

    private void EnterTransitionInputLock()
    {
        _currentContentFrame.IsHitTestVisible = false;
        _incomingContentFrame.IsHitTestVisible = false;
        TransitionFocusTarget.IsTabStop = true;
        if (!TransitionFocusTarget.Focus(FocusState.Programmatic))
        {
            AppNavigationView.Focus(FocusState.Programmatic);
        }
    }

    private void TransitionFocusTarget_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_navigationCoordinator.State is CoverNavigationState.Preparing or
            CoverNavigationState.Animating or
            CoverNavigationState.Completing)
        {
            args.Handled = true;
        }
    }

    private void RestoreCurrentInputAndFocus()
    {
        TransitionFocusTarget.IsTabStop = false;
        _incomingContentFrame.IsHitTestVisible = false;
        _currentContentFrame.IsHitTestVisible = _currentContentFrame.Content is not null;
        if (_currentContentFrame.Content is Control page)
        {
            page.Focus(FocusState.Programmatic);
        }
        else if (_currentContentFrame.Content is not null)
        {
            _currentContentFrame.Focus(FocusState.Programmatic);
        }
    }

    private void DetachIncomingLoadedHandler()
    {
        if (_incomingLoadedElement is not null)
        {
            _incomingLoadedElement.Loaded -= IncomingContent_Loaded;
        }

        _incomingLoadedElement = null;
        _incomingLoadedRevision = 0;
    }

    private void DetachAnimationBatch()
    {
        if (_activeAnimationBatch is not null)
        {
            _activeAnimationBatch.Completed -= CoverAnimationBatch_Completed;
        }

        _activeAnimationBatch = null;
        _activeAnimationRevision = 0;
    }

    private static void ResetFrame(Frame frame)
    {
        var visual = ElementCompositionPreview.GetElementVisual(frame);
        visual.StopAnimation("Offset.X");
        visual.Offset = Vector3.Zero;
        frame.BackStack.Clear();
        frame.ForwardStack.Clear();
        frame.Content = null;
        frame.Visibility = Visibility.Collapsed;
        frame.IsHitTestVisible = false;
    }

    private void PageTransitionHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PageTransitionClip.Rect = new Windows.Foundation.Rect(
            0,
            0,
            e.NewSize.Width,
            e.NewSize.Height);

        if (!_isShellLoaded)
        {
            return;
        }

        if (_navigationCoordinator.State == CoverNavigationState.Preparing)
        {
            var incomingVisual = ElementCompositionPreview.GetElementVisual(_incomingContentFrame);
            incomingVisual.StopAnimation("Offset.X");
            incomingVisual.Offset = new Vector3((float)Math.Max(0, e.NewSize.Width), 0, 0);
        }

        ExecuteIntent(_navigationCoordinator.HostWidthChanged(e.NewSize.Width));
    }

    private void AppNavigationView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSynchronizingSelection || args.IsSettingsSelected)
        {
            return;
        }

        if (args.SelectedItemContainer?.Tag is string route &&
            App.Services.Shell.NavigateCommand.CanExecute(route))
        {
            App.Services.Shell.NavigateCommand.Execute(route);
        }
    }

    private void AppNavigationView_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (!args.IsSettingsInvoked)
        {
            return;
        }

        const string route = "Settings";
        if (App.Services.Shell.NavigateCommand.CanExecute(route))
        {
            App.Services.Shell.NavigateCommand.Execute(route);
        }
    }

    private void SynchronizeNavigationSelection(string route)
    {
        var primaryRoute = route switch
        {
            "TopPlaylist" or "AlbumDetail" => "Home",
            _ => route
        };

        object? selectedItem = primaryRoute == "Settings"
            ? AppNavigationView.SettingsItem
            : AppNavigationView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, primaryRoute, StringComparison.Ordinal));

        if (ReferenceEquals(AppNavigationView.SelectedItem, selectedItem))
        {
            return;
        }

        _isSynchronizingSelection = true;
        AppNavigationView.SelectedItem = selectedItem;
        _isSynchronizingSelection = false;
    }

    private static Type ResolvePageType(string? route) => route switch
    {
        "Home" => typeof(HomePage),
        "TopPlaylist" => typeof(TopPlaylistPage),
        "AlbumDetail" => typeof(AlbumDetailPage),
        "Search" => typeof(SearchPage),
        "Albums" => typeof(AlbumsPage),
        "Artists" => typeof(ArtistsPage),
        "Favorites" => typeof(FavoritesPage),
        "FullPlay" => typeof(FullPlayPage),
        "Hits" => typeof(HitsStatusPage),
        "Settings" => typeof(SettingsPage),
        _ => typeof(LibraryPage)
    };

    private static bool ResolveAnimationsEnabled()
    {
        return MotionPolicy.ShouldAnimateInteraction();
    }
}
