using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace PrismWave_WinUI.Views.Shell;

public sealed partial class SplashPage : Page
{
    private Storyboard _entranceStoryboard = null!;
    private Storyboard _exitStoryboard = null!;
    private bool _hasStarted;
    private bool _hasExited;

    public SplashPage()
    {
        this.InitializeComponent();
        CreateEntranceAnimations();
        CreateExitAnimations();
        this.Loaded += SplashPage_Loaded;
    }

    private void SplashPage_Loaded(object sender, RoutedEventArgs e)
    {
        StartEntranceAnimation();
    }

    private void CreateEntranceAnimations()
    {
        _entranceStoryboard = new Storyboard();

        // Animation 1: "Prism" slides in from left with spring effect (0.4s)
        var prismSlideX = new DoubleAnimation
        {
            From = -180,
            To = 0,
            Duration = new Duration(TimeSpan.FromSeconds(0.4)),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(prismSlideX, PrismContainer);
        Storyboard.SetTargetProperty(prismSlideX, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
        _entranceStoryboard.Children.Add(prismSlideX);

        var prismFadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromSeconds(0.4)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(prismFadeIn, PrismContainer);
        Storyboard.SetTargetProperty(prismFadeIn, "Opacity");
        _entranceStoryboard.Children.Add(prismFadeIn);

        // Animation 2: "Wave" slides down from top with delay (starts at 0.4s)
        var waveSlideY = new DoubleAnimation
        {
            From = -80,
            To = 0,
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
            BeginTime = TimeSpan.FromSeconds(0.4),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(waveSlideY, WaveContainer);
        Storyboard.SetTargetProperty(waveSlideY, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");
        _entranceStoryboard.Children.Add(waveSlideY);

        var waveFadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
            BeginTime = TimeSpan.FromSeconds(0.4),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(waveFadeIn, WaveContainer);
        Storyboard.SetTargetProperty(waveFadeIn, "Opacity");
        _entranceStoryboard.Children.Add(waveFadeIn);
    }

    private void StartEntranceAnimation()
    {
        if (_hasStarted)
        {
            return;
        }

        _hasStarted = true;
        _entranceStoryboard.Begin();
    }

    private void CreateExitAnimations()
    {
        _exitStoryboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromSeconds(0.45));
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        // "Prism" flies out to the right while fading
        var prismExitX = new DoubleAnimation
        {
            From = 0,
            To = 320,
            Duration = duration,
            EasingFunction = easeIn,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(prismExitX, PrismContainer);
        Storyboard.SetTargetProperty(prismExitX, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
        _exitStoryboard.Children.Add(prismExitX);

        var prismExitFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = duration,
            EasingFunction = easeIn
        };
        Storyboard.SetTarget(prismExitFade, PrismContainer);
        Storyboard.SetTargetProperty(prismExitFade, "Opacity");
        _exitStoryboard.Children.Add(prismExitFade);

        // "Wave" flies out to the right while fading (slight stagger for a trailing feel)
        var waveExitX = new DoubleAnimation
        {
            From = 0,
            To = 320,
            Duration = duration,
            BeginTime = TimeSpan.FromSeconds(0.06),
            EasingFunction = easeIn,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(waveExitX, WaveContainer);
        Storyboard.SetTargetProperty(waveExitX, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
        _exitStoryboard.Children.Add(waveExitX);

        var waveExitFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = duration,
            BeginTime = TimeSpan.FromSeconds(0.06),
            EasingFunction = easeIn
        };
        Storyboard.SetTarget(waveExitFade, WaveContainer);
        Storyboard.SetTargetProperty(waveExitFade, "Opacity");
        _exitStoryboard.Children.Add(waveExitFade);
    }

    /// <summary>
    /// Waits for the entrance animation and the brand hold period to finish.
    /// Entrance takes ~0.9s, followed by a ~0.6s hold (total ~1.5s).
    /// </summary>
    public async Task PlayEntranceSequenceAsync()
    {
        try
        {
            StartEntranceAnimation();
            await Task.Delay(TimeSpan.FromSeconds(1.2));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Splash animation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Plays the exit animation: the logo flies to the right and fades out (~0.5s).
    /// </summary>
    public async Task PlayExitSequenceAsync()
    {
        if (_hasExited)
        {
            return;
        }

        _hasExited = true;
        try
        {
            _exitStoryboard.Begin();
            await Task.Delay(TimeSpan.FromMilliseconds(550));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Splash exit animation error: {ex.Message}");
        }
    }
}
