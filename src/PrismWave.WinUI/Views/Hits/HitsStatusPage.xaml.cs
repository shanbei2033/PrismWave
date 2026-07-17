using System.ComponentModel;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PrismWave_WinUI.Infrastructure.Animation;
using PrismWave_WinUI.ViewModels.Hits;

namespace PrismWave_WinUI.Views.Hits;

public sealed partial class HitsStatusPage : Page
{
    private readonly DispatcherQueueTimer _scheduleTimer;
    private DispatcherQueueTimer? _backdropCleanupTimer;
    private CancellationTokenSource? _loadCancellation;
    private CompositionEffectFactory? _backdropEffectFactory;
    private ContainerVisual? _backdropContainer;
    private SpriteVisual? _currentBackdropVisual;
    private SpriteVisual? _previousBackdropVisual;
    private LoadedImageSurface? _currentBackdropSurface;
    private LoadedImageSurface? _previousBackdropSurface;
    private int _backdropRevision;
    private bool _animationsEnabled = true;
    private string? _displayedTrackId;
    private string _displayedTitle = string.Empty;
    private string _displayedArtist = string.Empty;
    private string _displayedAlbum = string.Empty;

    private HitsStatusViewModel ViewModel => App.Services.Hits;

    public HitsStatusPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        _scheduleTimer = DispatcherQueue.CreateTimer();
        _scheduleTimer.Interval = TimeSpan.FromSeconds(1);
        _scheduleTimer.Tick += ScheduleTimer_Tick;
        Loaded += HitsStatusPage_Loaded;
        Unloaded += HitsStatusPage_Unloaded;
    }

    private async void HitsStatusPage_Loaded(object sender, RoutedEventArgs e)
    {
        _animationsEnabled = MotionPolicy.ShouldAnimateInteraction() && !App.Services.SettingsService.Current.LowEffects;
        if (App.Window is MainWindow mainWindow)
        {
            mainWindow.SetImmersiveTitleBar(true, HitsDragRegion);
        }

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _scheduleTimer.Start();
        EnsureBackdropResources();
        CaptureDisplayedTrack();
        LoadBackdrop(ViewModel.CurrentCoverPath);
        AnimateCoverState(immediate: true);

        _loadCancellation?.Cancel();
        _loadCancellation = new CancellationTokenSource();
        try
        {
            await ViewModel.InitializeAsync(_loadCancellation.Token);
            if (IsLoaded && ViewModel.IsAvailable && !ViewModel.IsSessionActive)
            {
                await ViewModel.PrepareHitsSessionCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HitsStatusPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _scheduleTimer.Stop();
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ReleaseBackdropResources();
    }

    private void ScheduleTimer_Tick(DispatcherQueueTimer sender, object args) => ViewModel.Tick();

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HitsStatusViewModel.CurrentTrack))
        {
            var nextId = ViewModel.CurrentTrack?.StationTrackId;
            if (!string.Equals(nextId, _displayedTrackId, StringComparison.Ordinal))
            {
                AnimateTrackChange();
                LoadBackdrop(ViewModel.CurrentCoverPath);
                CaptureDisplayedTrack();
            }
        }
        else if (e.PropertyName == nameof(HitsStatusViewModel.IsPaused))
        {
            AnimateCoverState(immediate: false);
        }
    }

    private void CaptureDisplayedTrack()
    {
        _displayedTrackId = ViewModel.CurrentTrack?.StationTrackId;
        _displayedTitle = ViewModel.DisplayTitle;
        _displayedArtist = ViewModel.DisplayArtist;
        _displayedAlbum = ViewModel.DisplayAlbum;
    }

    private void AnimateTrackChange()
    {
        PreviousTrackTitle.Text = _displayedTitle;
        PreviousTrackArtist.Text = _displayedArtist;
        PreviousTrackAlbum.Text = _displayedAlbum;
        var previous = ElementCompositionPreview.GetElementVisual(PreviousTrackPanel);
        var current = ElementCompositionPreview.GetElementVisual(CurrentTrackPanel);
        ElementCompositionPreview.SetIsTranslationEnabled(PreviousTrackPanel, true);
        ElementCompositionPreview.SetIsTranslationEnabled(CurrentTrackPanel, true);
        previous.StopAnimation("Opacity");
        previous.StopAnimation("Translation.Y");
        current.StopAnimation("Opacity");
        current.StopAnimation("Translation.Y");

        if (!_animationsEnabled || string.IsNullOrEmpty(_displayedTrackId))
        {
            previous.Opacity = 0;
            current.Opacity = 1;
            previous.Properties.InsertVector3("Translation", Vector3.Zero);
            current.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }

        var compositor = current.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.2f, 0.7f),
            new Vector2(0.2f, 1f));
        previous.Opacity = 1;
        current.Opacity = 0;
        previous.Properties.InsertVector3("Translation", Vector3.Zero);
        current.Properties.InsertVector3("Translation", new Vector3(0, 12, 0));

        var previousOpacity = compositor.CreateScalarKeyFrameAnimation();
        previousOpacity.InsertKeyFrame(1, 0, easing);
        previousOpacity.Duration = TimeSpan.FromMilliseconds(250);
        var previousTranslation = compositor.CreateScalarKeyFrameAnimation();
        previousTranslation.InsertKeyFrame(1, -10, easing);
        previousTranslation.Duration = previousOpacity.Duration;
        var currentOpacity = compositor.CreateScalarKeyFrameAnimation();
        currentOpacity.InsertKeyFrame(1, 1, easing);
        currentOpacity.Duration = previousOpacity.Duration;
        var currentTranslation = compositor.CreateScalarKeyFrameAnimation();
        currentTranslation.InsertKeyFrame(1, 0, easing);
        currentTranslation.Duration = previousOpacity.Duration;
        previous.StartAnimation("Opacity", previousOpacity);
        previous.StartAnimation("Translation.Y", previousTranslation);
        current.StartAnimation("Opacity", currentOpacity);
        current.StartAnimation("Translation.Y", currentTranslation);
    }

    private void AnimateCoverState(bool immediate)
    {
        var cover = ElementCompositionPreview.GetElementVisual(CoverVisualHost);
        cover.CenterPoint = new Vector3(260, 260, 0);
        var target = ViewModel.IsPaused ? 0.93f : 1f;
        cover.StopAnimation("Scale");
        if (immediate || !_animationsEnabled)
        {
            cover.Scale = new Vector3(target, target, 1);
        }
        else
        {
            var easing = cover.Compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.2f, 0.7f),
                new Vector2(0.2f, 1f));
            var scale = cover.Compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, new Vector3(target, target, 1), easing);
            scale.Duration = TimeSpan.FromMilliseconds(270);
            cover.StartAnimation("Scale", scale);
        }

    }

    private void EnsureBackdropResources()
    {
        if (_backdropContainer is not null)
        {
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(HitsBackdropHost).Compositor;
        var blur = new GaussianBlurEffect
        {
            Name = "HitsBlur",
            BlurAmount = App.Services.SettingsService.Current.LowEffects ? 12f : 30f,
            BorderMode = EffectBorderMode.Hard,
            Optimization = EffectOptimization.Balanced,
            Source = new CompositionEffectSourceParameter("BackdropSource")
        };
        _backdropEffectFactory = compositor.CreateEffectFactory(blur);
        _backdropContainer = compositor.CreateContainerVisual();
        _backdropContainer.RelativeSizeAdjustment = Vector2.One;
        _backdropContainer.Clip = compositor.CreateInsetClip();
        ElementCompositionPreview.SetElementChildVisual(HitsBackdropHost, _backdropContainer);

        _backdropCleanupTimer = DispatcherQueue.CreateTimer();
        _backdropCleanupTimer.Interval = TimeSpan.FromMilliseconds(300);
        _backdropCleanupTimer.IsRepeating = false;
        _backdropCleanupTimer.Tick += BackdropCleanupTimer_Tick;
    }

    private void LoadBackdrop(string? source)
    {
        var revision = ++_backdropRevision;
        if (CreateSourceUri(source) is not { } uri)
        {
            return;
        }

        var surface = LoadedImageSurface.StartLoadFromUri(uri, new Windows.Foundation.Size(960, 640));
        surface.LoadCompleted += (_, args) =>
        {
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
        if (revision != _backdropRevision || !IsLoaded || _backdropContainer is null || _backdropEffectFactory is null)
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
        var next = compositor.CreateSpriteVisual();
        next.RelativeSizeAdjustment = Vector2.One;
        next.Brush = blurBrush;
        _previousBackdropVisual = _currentBackdropVisual;
        _previousBackdropSurface = _currentBackdropSurface;
        _currentBackdropVisual = next;
        _currentBackdropSurface = surface;
        _backdropContainer.Children.InsertAtTop(next);

        if (_previousBackdropVisual is null || !_animationsEnabled)
        {
            next.Opacity = 1;
            CompleteBackdropFade();
            return;
        }

        next.Opacity = 0;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(280);
        next.StartAnimation("Opacity", fade);
        _backdropCleanupTimer?.Start();
    }

    private void BackdropCleanupTimer_Tick(DispatcherQueueTimer sender, object args) => CompleteBackdropFade();

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

        ElementCompositionPreview.SetElementChildVisual(HitsBackdropHost, null);
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

    private void BackButton_Click(object sender, RoutedEventArgs e) => App.Services.Shell.GoBackCommand.Execute(null);

    private void BackKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (App.Services.Shell.GoBackCommand.CanExecute(null))
        {
            App.Services.Shell.GoBackCommand.Execute(null);
            args.Handled = true;
        }
    }
}
