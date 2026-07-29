using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PrismWave_WinUI.Infrastructure.Animation;
using PrismWave_WinUI.Infrastructure.Lyrics;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Player;
using PrismWave_WinUI.Views.Dialogs;

namespace PrismWave_WinUI.Views.Player;

public sealed partial class FullPlayPage : Page
{
    private const double LyricsToolsAnimationDurationMilliseconds = 160;
    private const double LyricsToolsStaggerMilliseconds = 25;

    private static readonly Regex PartialLyricsOffsetPattern = new(
        @"^[+-]?\d+(?:\.\d?)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private DispatcherQueueTimer? _lyricsReturnTimer;
    private DispatcherQueueTimer? _backdropCleanupTimer;
    private bool _areLyricsToolsExpanded;
    private bool _isCoverSearchDialogOpen;
    private bool _animationsEnabled = true;
    private Storyboard? _lyricsToolsStoryboard;
    private int _lyricsDocumentRevision;
    private bool _lyricsRefreshScheduled;
    private int _backdropRevision;
    private LoadedImageSurface? _pendingBackdropSurface;
    private CompositionEffectFactory? _backdropEffectFactory;
    private ContainerVisual? _backdropContainer;
    private SpriteVisual? _currentBackdropVisual;
    private SpriteVisual? _previousBackdropVisual;
    private LoadedImageSurface? _currentBackdropSurface;
    private LoadedImageSurface? _previousBackdropSurface;
    private PlaybackViewModel ViewModel => App.Services.Playback;

    public FullPlayPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += FullPlayPage_Loaded;
        Unloaded += FullPlayPage_Unloaded;
    }

    private void FullPlayPage_Loaded(object sender, RoutedEventArgs e)
    {
        _animationsEnabled = ResolveAnimationsEnabled();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Lyrics.CollectionChanged += Lyrics_CollectionChanged;

        _lyricsReturnTimer = DispatcherQueue.CreateTimer();
        _lyricsReturnTimer.Interval = TimeSpan.FromSeconds(4);
        _lyricsReturnTimer.IsRepeating = false;
        _lyricsReturnTimer.Tick += LyricsReturnTimer_Tick;
        RefreshLyricsStage(LyricsPositionUpdateKind.TrackChanged);
        EnsureBackdropResources();
        LoadBackdrop(ViewModel.CurrentCoverPath);
    }

    private void FullPlayPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _lyricsToolsStoryboard?.Stop();
        _lyricsToolsStoryboard = null;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Lyrics.CollectionChanged -= Lyrics_CollectionChanged;

        if (_lyricsReturnTimer is not null)
        {
            _lyricsReturnTimer.Stop();
            _lyricsReturnTimer.Tick -= LyricsReturnTimer_Tick;
            _lyricsReturnTimer = null;
        }

        ReleaseBackdropResources();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.CurrentCoverPath))
        {
            DispatcherQueue.TryEnqueue(() => LoadBackdrop(ViewModel.CurrentCoverPath));
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                FullPlaySeekSlider.Value = 0;
                RefreshLyricsStage(LyricsPositionUpdateKind.TrackChanged);
            });
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.IsPlaying))
        {
            LyricsStage.UpdatePlaybackSample(
                EffectiveLyricsPosition,
                ViewModel.IsPlaying,
                LyricsPositionUpdateKind.PauseResume);
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.PositionSeconds))
        {
            if (ViewModel.PositionSeconds == 0)
            {
                DispatcherQueue.TryEnqueue(() => FullPlaySeekSlider.Value = 0);
            }
            LyricsStage.UpdatePlaybackSample(
                EffectiveLyricsPosition,
                ViewModel.IsPlaying,
                LyricsPositionUpdateKind.Sample);
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.LyricsOffsetSeconds))
        {
            LyricsStage.UpdatePlaybackSample(
                EffectiveLyricsPosition,
                ViewModel.IsPlaying,
                LyricsPositionUpdateKind.OffsetChanged);
        }
    }

    private double EffectiveLyricsPosition => Math.Max(
        0,
        ViewModel.PositionSeconds - ViewModel.LyricsOffsetSeconds);

    private void Lyrics_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_lyricsRefreshScheduled)
        {
            return;
        }

        _lyricsRefreshScheduled = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _lyricsRefreshScheduled = false;
                if (IsLoaded)
                {
                    RefreshLyricsStage(LyricsPositionUpdateKind.TrackChanged);
                }
            }))
        {
            _lyricsRefreshScheduled = false;
        }
    }

    private void RefreshLyricsStage(LyricsPositionUpdateKind updateKind)
    {
        LyricsStage.SetLyrics(
            ViewModel.Lyrics.Select(line => line.Line).ToArray(),
            ++_lyricsDocumentRevision);
        LyricsStage.UpdatePlaybackSample(EffectiveLyricsPosition, ViewModel.IsPlaying, updateKind);
    }

    private void LyricsStage_LyricInvoked(
        object sender,
        PrismWave_WinUI.Controls.Lyrics.LyricsLineInvokedEventArgs e)
    {
        _lyricsReturnTimer?.Stop();
        ViewModel.SeekToLyric(e.LineIndex);
        LyricsStage.UpdatePlaybackSample(
            EffectiveLyricsPosition,
            ViewModel.IsPlaying,
            LyricsPositionUpdateKind.Seek);
    }

    private void LyricsStage_ManualBrowseChanged(
        object sender,
        PrismWave_WinUI.Controls.Lyrics.LyricsManualBrowseChangedEventArgs e)
    {
        if (!e.IsManualBrowsing || _lyricsReturnTimer is null)
        {
            return;
        }

        _lyricsReturnTimer.Stop();
        _lyricsReturnTimer.Start();
    }

    private void LyricsReturnTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        LyricsStage.EndManualBrowse();
    }

    private void EnsureBackdropResources()
    {
        if (_backdropContainer is not null)
        {
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(FullPlayBackdropHost).Compositor;
        var blur = new GaussianBlurEffect
        {
            Name = "FullPlayBlur",
            BlurAmount = 30f,
            BorderMode = EffectBorderMode.Hard,
            Optimization = EffectOptimization.Balanced,
            Source = new CompositionEffectSourceParameter("BackdropSource")
        };
        _backdropEffectFactory = compositor.CreateEffectFactory(blur);
        _backdropContainer = compositor.CreateContainerVisual();
        _backdropContainer.RelativeSizeAdjustment = Vector2.One;
        _backdropContainer.Clip = compositor.CreateInsetClip();
        ElementCompositionPreview.SetElementChildVisual(FullPlayBackdropHost, _backdropContainer);

        _backdropCleanupTimer = DispatcherQueue.CreateTimer();
        _backdropCleanupTimer.Interval = TimeSpan.FromMilliseconds(220);
        _backdropCleanupTimer.IsRepeating = false;
        _backdropCleanupTimer.Tick += BackdropCleanupTimer_Tick;
    }

    private void LoadBackdrop(string? source)
    {
        var revision = ++_backdropRevision;
        
        // Dispose any pending surface from previous call
        _pendingBackdropSurface?.Dispose();
        _pendingBackdropSurface = null;
        
        if (CreateSourceUri(source) is not { } uri)
        {
            return;
        }

        var surface = LoadedImageSurface.StartLoadFromUri(
            uri,
            new Windows.Foundation.Size(960, 640));
        _pendingBackdropSurface = surface;
        surface.LoadCompleted += (_, args) =>
        {
            _pendingBackdropSurface = null;
            if (args.Status != LoadedImageSourceLoadStatus.Success)
            {
                surface.Dispose();
                return;
            }

            if (!DispatcherQueue.TryEnqueue(() => ApplyBackdropSurface(surface, revision)))
            {
                surface.Dispose();
            }
        };
    }

    private void ApplyBackdropSurface(LoadedImageSurface surface, int revision)
    {
        if (revision != _backdropRevision || !IsLoaded ||
            _backdropContainer is null || _backdropEffectFactory is null)
        {
            surface.Dispose();
            return;
        }

        CompleteBackdropFade();
        var compositor = _backdropContainer.Compositor;
        var surfaceBrush = compositor.CreateSurfaceBrush(surface);
        surfaceBrush.Stretch = CompositionStretch.UniformToFill;
        var blurBrush = _backdropEffectFactory.CreateBrush();
        blurBrush.SetSourceParameter("BackdropSource", surfaceBrush);
        var nextVisual = compositor.CreateSpriteVisual();
        nextVisual.RelativeSizeAdjustment = Vector2.One;
        nextVisual.Brush = blurBrush;

        _previousBackdropVisual = _currentBackdropVisual;
        _previousBackdropSurface = _currentBackdropSurface;
        _currentBackdropVisual = nextVisual;
        _currentBackdropSurface = surface;
        _backdropContainer.Children.InsertAtTop(nextVisual);

        if (_previousBackdropVisual is null)
        {
            nextVisual.Opacity = 1;
            return;
        }

        if (!_animationsEnabled)
        {
            nextVisual.Opacity = 1;
            CompleteBackdropFade();
            return;
        }

        nextVisual.Opacity = 0;
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1.0f));
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f, easing);
        fade.Duration = TimeSpan.FromMilliseconds(200);
        nextVisual.StartAnimation("Opacity", fade);
        _backdropCleanupTimer?.Start();
    }

    private void BackdropCleanupTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        CompleteBackdropFade();
    }

    private void CompleteBackdropFade()
    {
        _backdropCleanupTimer?.Stop();
        if (_previousBackdropVisual is not null && _backdropContainer is not null)
        {
            _backdropContainer.Children.Remove(_previousBackdropVisual);
        }

        _previousBackdropVisual = null;
        _previousBackdropSurface?.Dispose();
        _previousBackdropSurface = null;
        if (_currentBackdropVisual is not null)
        {
            _currentBackdropVisual.StopAnimation("Opacity");
            _currentBackdropVisual.Opacity = 1;
        }
    }

    private void ReleaseBackdropResources()
    {
        _backdropRevision++;
        if (_backdropCleanupTimer is not null)
        {
            _backdropCleanupTimer.Stop();
            _backdropCleanupTimer.Tick -= BackdropCleanupTimer_Tick;
            _backdropCleanupTimer = null;
        }

        ElementCompositionPreview.SetElementChildVisual(FullPlayBackdropHost, null);
        _previousBackdropSurface?.Dispose();
        _currentBackdropSurface?.Dispose();
        _previousBackdropSurface = null;
        _currentBackdropSurface = null;
        _previousBackdropVisual = null;
        _currentBackdropVisual = null;
        _backdropContainer = null;
        _backdropEffectFactory = null;
    }

    private static Uri? CreateSourceUri(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                return uri;
            }

            return File.Exists(source) ? new Uri(Path.GetFullPath(source)) : null;
        }
        catch
        {
            return null;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        App.Services.Shell.GoBackCommand.Execute(null);
    }

    private void BackKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (App.Services.Shell.GoBackCommand.CanExecute(null))
        {
            App.Services.Shell.GoBackCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void FullPlayVolumeSlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ViewModel.SetVolume(e.NewValue);
    }

    private void FullPlaySeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        CommitSeek(sender);
    }

    private void FullPlaySeekSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        CommitSeek(sender);
    }

    private void CommitSeek(object sender)
    {
        if (sender is Slider slider)
        {
            ViewModel.Seek(slider.Value);
            LyricsStage.UpdatePlaybackSample(
                Math.Max(0, slider.Value - ViewModel.LyricsOffsetSeconds),
                ViewModel.IsPlaying,
                LyricsPositionUpdateKind.Seek);
        }
    }

    private void QueueList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel.PlayQueueTrackCommand.CanExecute(e.ClickedItem))
        {
            ViewModel.PlayQueueTrackCommand.Execute(e.ClickedItem);
        }
    }

    private void LyricsToolsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var expanding = !_areLyricsToolsExpanded;
        _areLyricsToolsExpanded = expanding;
        _lyricsToolsStoryboard?.Stop();
        _lyricsToolsStoryboard = null;
        LyricsToolActions.IsHitTestVisible = expanding;
        LyricsToolsToggleIcon.Glyph = expanding ? "\uE711" : "\uE713";

        if (!_animationsEnabled)
        {
            foreach (var button in GetLyricsToolButtons(expanding))
            {
                SetLyricsToolButtonState(button, expanding);
            }

            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(
            LyricsToolsAnimationDurationMilliseconds));
        var easing = new CubicEase
        {
            EasingMode = expanding ? EasingMode.EaseOut : EasingMode.EaseIn
        };
        var storyboard = new Storyboard();
        var orderedButtons = GetLyricsToolButtons(expanding);
        for (var index = 0; index < orderedButtons.Length; index++)
        {
            var button = orderedButtons[index];
            var opacityAnimation = new DoubleAnimation
            {
                To = expanding ? 1 : 0,
                Duration = duration,
                BeginTime = TimeSpan.FromMilliseconds(index * LyricsToolsStaggerMilliseconds),
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            var translationAnimation = new DoubleAnimation
            {
                To = expanding ? 0 : 18,
                Duration = duration,
                BeginTime = TimeSpan.FromMilliseconds(index * LyricsToolsStaggerMilliseconds),
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(opacityAnimation, button);
            Storyboard.SetTargetProperty(opacityAnimation, "Opacity");
            Storyboard.SetTarget(translationAnimation, button);
            Storyboard.SetTargetProperty(
                translationAnimation,
                "(UIElement.RenderTransform).(TranslateTransform.Y)");
            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(translationAnimation);
        }

        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_lyricsToolsStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            foreach (var button in orderedButtons)
            {
                SetLyricsToolButtonState(button, expanding);
            }

            _lyricsToolsStoryboard = null;
        };
        _lyricsToolsStoryboard = storyboard;
        storyboard.Begin();
    }

    private Button[] GetLyricsToolButtons(bool expanding)
    {
        return expanding
            ? new[] { LyricsOffsetButton, LyricsSearchButton, LyricsSourceButton }
            : new[] { LyricsSourceButton, LyricsSearchButton, LyricsOffsetButton };
    }

    private static void SetLyricsToolButtonState(Button button, bool expanded)
    {
        button.Opacity = expanded ? 1 : 0;
        if (button.RenderTransform is TranslateTransform transform)
        {
            transform.Y = expanded ? 0 : 18;
        }
    }

    private async void SearchLyricsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentTrack is null)
        {
            return;
        }

        var dialog = new LyricsSearchDialog(ViewModel)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void LyricsOffsetFlyout_Opened(object sender, object e)
    {
        LyricsOffsetValidationText.Visibility = Visibility.Collapsed;
        LyricsOffsetInput.Text = ViewModel.LyricsOffsetSeconds.ToString(
            "+0.0;-0.0;0.0",
            CultureInfo.InvariantCulture);
        LyricsOffsetInput.Focus(FocusState.Programmatic);
        LyricsOffsetInput.SelectAll();
    }

    private void LyricsOffsetInput_BeforeTextChanging(
        TextBox sender,
        TextBoxBeforeTextChangingEventArgs args)
    {
        var text = args.NewText;
        args.Cancel = text.Length > 0
            && text is not "+" and not "-"
            && !PartialLyricsOffsetPattern.IsMatch(text);
    }

    private async void LyricsOffsetInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await ApplyLyricsOffsetInputAsync();
    }

    private async void ApplyLyricsOffsetButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyLyricsOffsetInputAsync();
    }

    private async Task ApplyLyricsOffsetInputAsync()
    {
        if (await ViewModel.ApplyLyricsOffsetAsync(LyricsOffsetInput.Text))
        {
            LyricsOffsetValidationText.Visibility = Visibility.Collapsed;
            LyricsOffsetFlyout.Hide();
            return;
        }

        LyricsOffsetValidationText.Visibility = Visibility.Visible;
        LyricsOffsetInput.Focus(FocusState.Programmatic);
        LyricsOffsetInput.SelectAll();
    }

    private async void SearchCoverButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowCoverSearchDialogAsync();
    }

    private async void FullPlayCover_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        await ShowCoverSearchDialogAsync();
    }

    private async Task ShowCoverSearchDialogAsync()
    {
        if (_isCoverSearchDialogOpen || ViewModel.CurrentTrack is null)
        {
            return;
        }

        _isCoverSearchDialogOpen = true;
        try
        {
            var dialog = new CoverSearchDialog(App.Services.CoverService, ViewModel.CurrentTrack)
            {
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _isCoverSearchDialogOpen = false;
        }
    }

    private static bool ResolveAnimationsEnabled()
    {
        return MotionPolicy.ShouldAnimateInteraction();
    }

}
