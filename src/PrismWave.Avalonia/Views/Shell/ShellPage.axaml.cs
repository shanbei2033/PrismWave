using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using PrismWave.Avalonia.Views.Placeholders;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Infrastructure.Navigation;
using PrismWave_WinUI.ViewModels.Shell;

namespace PrismWave.Avalonia.Views.Shell;

public partial class ShellPage : UserControl
{
    private static readonly double MotionNormalMs =
        Application.Current!.Resources.TryGetValue("MotionNormal", out var motion)
            ? (double)motion!
            : 240d;

    private bool _isShellLoaded;
    private ShellViewModel Shell => App.Services.Shell;

    public ShellPage()
    {
        StartupLog.Write("[Avalonia] ShellPage constructor");
        InitializeComponent();
        DataContext = Shell;
        Loaded += ShellPage_Loaded;
        Unloaded += ShellPage_Unloaded;
    }

    private void ShellPage_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_isShellLoaded)
        {
            return;
        }

        _isShellLoaded = true;
        Shell.NavigationRequested += Shell_NavigationRequested;
        Shell.PropertyChanged += Shell_PropertyChanged;
        PlaybackQueuePane.CloseRequested += QueuePane_CloseRequested;

        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            topLevel.KeyDown += TopLevel_KeyDown;
        }

        if (Nav.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Tag = "Settings";
            settingsItem.Content = Shell.SettingsLabel;
        }

        SyncSelection(Shell.SelectedRoute);
        ShowRoute(Shell.SelectedRoute);
        StartupLog.Write($"[Avalonia] shell initial route: {Shell.SelectedRoute}");
    }

    private void ShellPage_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isShellLoaded)
        {
            return;
        }

        _isShellLoaded = false;
        Shell.NavigationRequested -= Shell_NavigationRequested;
        Shell.PropertyChanged -= Shell_PropertyChanged;
        PlaybackQueuePane.CloseRequested -= QueuePane_CloseRequested;

        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            topLevel.KeyDown -= TopLevel_KeyDown;
        }
    }

    private void TopLevel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Shell.IsQueuePaneOpen)
        {
            Shell.CloseQueuePaneCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void QueuePane_CloseRequested(object? sender, EventArgs e)
    {
        Shell.CloseQueuePaneCommand.Execute(null);
    }

    private void QueueBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Shell.CloseQueuePaneCommand.Execute(null);
    }

    private void Nav_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is NavigationViewItem item
            && item.Tag is string route
            && !string.Equals(route, Shell.SelectedRoute, StringComparison.Ordinal))
        {
            StartupLog.Write($"[Avalonia] navigate invoked: {route}");
            Shell.Navigate(route);
        }
    }

    private void Shell_NavigationRequested(object? sender, ShellNavigationRequest request)
    {
        SyncSelection(request.Route);
        ShowRoute(request.Route);
    }

    private void Shell_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.SelectedRoute))
        {
            SyncSelection(Shell.SelectedRoute);
        }
    }

    private void SyncSelection(string route)
    {
        foreach (var item in EnumerateSelectableItems())
        {
            if (item.Tag is string tag && string.Equals(tag, route, StringComparison.Ordinal))
            {
                Nav.SelectedItem = item;
                return;
            }
        }
    }

    private IEnumerable<NavigationViewItemBase> EnumerateSelectableItems()
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItemBase>())
        {
            yield return item;
        }

        if (Nav.SettingsItem is NavigationViewItemBase settings)
        {
            yield return settings;
        }
    }

    private void ShowRoute(string route)
    {
        var page = RoutePageFactory.Create(route);
        ContentHost.Content = page;
        PlayEnterTransition(page);
    }

    private static void PlayEnterTransition(Control content)
    {
        var easing = new SplineEasing(0.1, 0.9, 0.2, 1.0);
        var duration = TimeSpan.FromMilliseconds(MotionNormalMs);

        var translate = new TranslateTransform(72, 0)
        {
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = TranslateTransform.XProperty,
                    Duration = duration,
                    Easing = easing
                }
            }
        };
        content.RenderTransform = translate;
        content.Opacity = 0;
        content.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration,
                Easing = easing
            }
        };

        // 下一帧再触发目标值，确保 transition 生效
        Dispatcher.UIThread.Post(() =>
        {
            translate.X = 0;
            content.Opacity = 1;
        }, DispatcherPriority.Render);
    }
}
