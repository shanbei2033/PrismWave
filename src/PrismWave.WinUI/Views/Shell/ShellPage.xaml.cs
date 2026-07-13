using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Views.Hits;
using PrismWave_WinUI.Views.Home;
using PrismWave_WinUI.Views.Library;
using PrismWave_WinUI.Views.Player;
using PrismWave_WinUI.Views.Search;
using PrismWave_WinUI.Views.Settings;

namespace PrismWave_WinUI.Views.Shell;

public sealed partial class ShellPage : Page
{
    private bool _isSynchronizingSelection = true;
    private bool _hasInitializedContent;
    private bool _isTransitionActive;
    private long _navigationTransitionRevision;
    private long _activeTransitionRevision;
    private Frame _currentContentFrame = null!;
    private Frame _incomingContentFrame = null!;

    public ShellPage()
    {
        StartupLog.Write("ShellPage constructor");
        InitializeComponent();
        _currentContentFrame = PrimaryContentFrame;
        _incomingContentFrame = SecondaryContentFrame;
        DataContext = App.Services.Shell;
        App.Services.Shell.NavigationRequested += (_, route) => Navigate(route);
        Loaded += ShellPage_Loaded;
        _isSynchronizingSelection = false;
        StartupLog.Write("ShellPage initialized");
    }

    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (AppNavigationView.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Content = "设置";
            AutomationProperties.SetName(settingsItem, "设置");
            ToolTipService.SetToolTip(settingsItem, "设置");
        }

        Navigate(App.Services.Shell.SelectedRoute);
    }

    private void Navigate(string route)
    {
        var target = route switch
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

        if (_currentContentFrame.CurrentSourcePageType == target)
        {
            SynchronizeNavigationSelection(route);
            return;
        }

        if (!_hasInitializedContent)
        {
            _hasInitializedContent = _currentContentFrame.Navigate(
                target,
                null,
                new SuppressNavigationTransitionInfo());
            if (!_hasInitializedContent)
            {
                StartupLog.Write($"navigation.cover.failed route={route} phase=initial");
            }

            SynchronizeNavigationSelection(route);
            return;
        }

        if (_isTransitionActive)
        {
            CompleteActiveTransition(superseded: true);
        }

        StartCoverNavigation(target, route);
        SynchronizeNavigationSelection(route);
    }

    private void StartCoverNavigation(Type target, string route)
    {
        StartupLog.Write($"navigation.cover.requested route={route}");
        ResetFrame(_incomingContentFrame);

        if (!_incomingContentFrame.Navigate(target, null, new SuppressNavigationTransitionInfo()))
        {
            StartupLog.Write($"navigation.cover.failed route={route} phase=navigate");
            return;
        }

        _incomingContentFrame.Visibility = Visibility.Visible;
        _incomingContentFrame.IsHitTestVisible = false;
        _isTransitionActive = true;
        _activeTransitionRevision = ++_navigationTransitionRevision;
        var transitionRevision = _activeTransitionRevision;
        StartupLog.Write($"navigation.cover.prepared route={route} revision={transitionRevision}");

        if (!DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => BeginCoverAnimation(transitionRevision)))
        {
            BeginCoverAnimation(transitionRevision);
        }
    }

    private void BeginCoverAnimation(long transitionRevision)
    {
        if (!_isTransitionActive || transitionRevision != _navigationTransitionRevision)
        {
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(PageTransitionHost).Compositor;
        var currentVisual = ElementCompositionPreview.GetElementVisual(_currentContentFrame);
        var incomingVisual = ElementCompositionPreview.GetElementVisual(_incomingContentFrame);
        currentVisual.StopAnimation("Offset.X");
        currentVisual.Offset = Vector3.Zero;
        incomingVisual.Offset = new Vector3((float)PageTransitionHost.ActualWidth, 0, 0);

        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1.0f));
        var animation = compositor.CreateScalarKeyFrameAnimation();
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        animation.InsertKeyFrame(1f, 0f, easing);
        animation.Duration = TimeSpan.FromMilliseconds(280);
        batch.Completed += (_, _) =>
        {
            if (!DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () =>
                    {
                        if (!_isTransitionActive || transitionRevision != _navigationTransitionRevision)
                        {
                            return;
                        }

                        CompleteActiveTransition();
                    }))
            {
                CompleteActiveTransition();
            }
        };

        incomingVisual.StartAnimation("Offset.X", animation);
        batch.End();
        StartupLog.Write($"navigation.cover.started revision={transitionRevision} durationMs=280");
    }

    private void CompleteActiveTransition(bool superseded = false)
    {
        var transitionRevision = _activeTransitionRevision;
        if (!_isTransitionActive || transitionRevision != _navigationTransitionRevision)
        {
            return;
        }

        var incomingVisual = ElementCompositionPreview.GetElementVisual(_incomingContentFrame);
        incomingVisual.StopAnimation("Offset.X");
        incomingVisual.Offset = Vector3.Zero;

        var previousContentFrame = _currentContentFrame;
        _currentContentFrame = _incomingContentFrame;
        _incomingContentFrame = previousContentFrame;
        _currentContentFrame.IsHitTestVisible = true;
        ResetFrame(_incomingContentFrame);
        _isTransitionActive = false;

        if (superseded)
        {
            StartupLog.Write($"navigation.cover.superseded revision={transitionRevision}");
            return;
        }

        if (_currentContentFrame.Content is Control page)
        {
            page.Focus(FocusState.Programmatic);
        }
        else
        {
            _currentContentFrame.Focus(FocusState.Programmatic);
        }

        StartupLog.Write($"navigation.cover.completed revision={transitionRevision}");
    }

    private static void ResetFrame(Frame frame)
    {
        var visual = ElementCompositionPreview.GetElementVisual(frame);
        visual.StopAnimation("Offset.X");
        visual.Offset = Vector3.Zero;
        frame.Content = null;
        frame.Visibility = Visibility.Collapsed;
        frame.IsHitTestVisible = false;
    }

    private void PageTransitionHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isTransitionActive)
        {
            CompleteActiveTransition(superseded: true);
        }

        PageTransitionClip.Rect = new Windows.Foundation.Rect(
            0,
            0,
            e.NewSize.Width,
            e.NewSize.Height);
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
}
