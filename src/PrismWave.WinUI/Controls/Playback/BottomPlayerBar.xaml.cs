using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave_WinUI.Controls.Playback;

public sealed partial class BottomPlayerBar : UserControl
{
    public BottomPlayerBar()
    {
        InitializeComponent();
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

    private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        CommitSeek(sender);
    }

    private void SeekSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
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
