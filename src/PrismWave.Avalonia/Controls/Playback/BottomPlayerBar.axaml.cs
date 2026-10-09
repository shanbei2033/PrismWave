using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave.Avalonia.Controls.Playback;

public partial class BottomPlayerBar : UserControl
{
    private PlaybackViewModel? _subscribedViewModel;
    private bool _suppressVolumeChanged;

    public BottomPlayerBar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetached;
    }

    public static readonly StyledProperty<ICommand?> FullPlayCommandProperty =
        StyledProperty<ICommand?>.Register<BottomPlayerBar, ICommand?>(nameof(FullPlayCommand));

    public static readonly StyledProperty<ICommand?> QueueCommandProperty =
        StyledProperty<ICommand?>.Register<BottomPlayerBar, ICommand?>(nameof(QueueCommand));

    public ICommand? FullPlayCommand
    {
        get => GetValue(FullPlayCommandProperty);
        set => SetValue(FullPlayCommandProperty, value);
    }

    public ICommand? QueueCommand
    {
        get => GetValue(QueueCommandProperty);
        set => SetValue(QueueCommandProperty, value);
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is PlaybackViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
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
            Dispatcher.UIThread.Post(() => SeekSlider.Value = 0);
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.PositionSeconds)
                 && _subscribedViewModel is not null
                 && _subscribedViewModel.PositionSeconds == 0)
        {
            Dispatcher.UIThread.Post(() => SeekSlider.Value = 0);
        }
    }

    private void BottomPlayerBar_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveState(e.NewSize.Width);
    }

    private void ApplyResponsiveState(double width)
    {
        if (width >= 1120)
        {
            ApplyWide();
        }
        else if (width >= 760)
        {
            ApplyMedium();
        }
        else
        {
            ApplyCompact();
        }
    }

    private void ApplyCompact()
    {
        PlayerLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        PlayerLayout.ColumnDefinitions[1].Width = GridLength.Auto;
        PlayerLayout.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        PlayerLayout.ColumnSpacing = 8;
        TrackLayout.ColumnDefinitions[0].Width = new GridLength(0);
        CoverButton.IsVisible = false;
        FavoriteButton.IsVisible = false;
        SubtitleText.IsVisible = false;
        ErrorText.IsVisible = false;
        ModeButton.IsVisible = false;
        QueueButton.IsVisible = false;
        VolumeSlider.IsVisible = false;
        VolumeRegion.Spacing = 0;
    }

    private void ApplyMedium()
    {
        PlayerLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        PlayerLayout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        PlayerLayout.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        PlayerLayout.ColumnSpacing = 14;
        TrackLayout.ColumnDefinitions[0].Width = new GridLength(58);
        CoverButton.IsVisible = true;
        FavoriteButton.IsVisible = false;
        SubtitleText.IsVisible = true;
        ErrorText.IsVisible = false;
        ModeButton.IsVisible = true;
        QueueButton.IsVisible = true;
        VolumeSlider.IsVisible = true;
        VolumeSlider.Width = 100;
        VolumeRegion.Spacing = 8;
    }

    private void ApplyWide()
    {
        PlayerLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        PlayerLayout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        PlayerLayout.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        PlayerLayout.ColumnSpacing = 18;
        TrackLayout.ColumnDefinitions[0].Width = new GridLength(58);
        CoverButton.IsVisible = true;
        FavoriteButton.IsVisible = true;
        SubtitleText.IsVisible = true;
        ErrorText.IsVisible = true;
        ModeButton.IsVisible = true;
        QueueButton.IsVisible = true;
        VolumeSlider.IsVisible = true;
        VolumeSlider.Width = 130;
        VolumeRegion.Spacing = 8;
    }

    private void VolumeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressVolumeChanged)
        {
            return;
        }

        if (DataContext is PlaybackViewModel viewModel)
        {
            viewModel.SetVolume(e.NewValue);
        }
    }

    private void SeekSlider_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CommitSeek();
    }

    private void SeekSlider_KeyUp(object? sender, KeyEventArgs e)
    {
        CommitSeek();
    }

    private void CommitSeek()
    {
        if (DataContext is PlaybackViewModel viewModel)
        {
            viewModel.Seek(SeekSlider.Value);
        }
    }
}
