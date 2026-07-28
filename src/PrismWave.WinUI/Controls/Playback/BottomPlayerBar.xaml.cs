using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave_WinUI.Controls.Playback;

public sealed partial class BottomPlayerBar : UserControl
{
    private PlaybackViewModel? _subscribedViewModel;
    private bool _isSeeking;

    public BottomPlayerBar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += BottomPlayerBar_Unloaded;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _subscribedViewModel = null;
        }

        if (args.NewValue is PlaybackViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void BottomPlayerBar_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _subscribedViewModel = null;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack))
        {
            DispatcherQueue.TryEnqueue(() => SeekSlider.Value = 0);
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.PositionSeconds)
                 && _subscribedViewModel is not null
                 && !_isSeeking)
        {
            // WinUI 3 Slider OneWay binding stops updating the thumb after user interaction.
            // Force-update the value on every PositionSeconds change when not seeking.
            var position = _subscribedViewModel.PositionSeconds;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isSeeking)
                {
                    SeekSlider.Value = position;
                }
            });
        }
    }

    public static readonly DependencyProperty QueueCommandProperty = DependencyProperty.Register(
        nameof(QueueCommand), typeof(ICommand), typeof(BottomPlayerBar), new PropertyMetadata(null));

    public static readonly DependencyProperty FullPlayCommandProperty = DependencyProperty.Register(
        nameof(FullPlayCommand), typeof(ICommand), typeof(BottomPlayerBar), new PropertyMetadata(null));

    public ICommand? QueueCommand
    {
        get => (ICommand?)GetValue(QueueCommandProperty);
        set => SetValue(QueueCommandProperty, value);
    }

    public ICommand? FullPlayCommand
    {
        get => (ICommand?)GetValue(FullPlayCommandProperty);
        set => SetValue(FullPlayCommandProperty, value);
    }

    private void BottomPlayerBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var state = e.NewSize.Width >= 1120
            ? "Wide"
            : e.NewSize.Width >= 760
                ? "Medium"
                : "Compact";
        VisualStateManager.GoToState(this, state, useTransitions: false);
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (DataContext is PlaybackViewModel viewModel)
        {
            viewModel.SetVolume(e.NewValue);
        }
    }

    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = true;
    }

    private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = false;
        CommitSeek(sender);
    }

    private void SeekSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        _isSeeking = false;
        CommitSeek(sender);
    }

    private void CommitSeek(object sender)
    {
        if (sender is Slider slider && DataContext is PlaybackViewModel viewModel)
        {
            viewModel.Seek(slider.Value);
        }
    }
}
