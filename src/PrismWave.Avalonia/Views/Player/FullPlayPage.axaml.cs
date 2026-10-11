using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PrismWave.Avalonia.Controls.Lyrics;
using PrismWave_WinUI.Infrastructure.Lyrics;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave.Avalonia.Views.Player;

public sealed partial class FullPlayPage : UserControl
{
    private const double LyricsReturnSeconds = 4;

    /// <summary>进入转场（MotionNormal=240ms）的收尾余量：歌词布局延迟到转场后，避免转场首帧被文本 shaping 阻塞。</summary>
    private const int TransitionSettleMilliseconds = 280;

    private static readonly Regex PartialLyricsOffsetPattern = new(
        @"^[+-]?\d+(?:\.\d?)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ObservableCollection<LyricsSearchResultModel> _lyricsSearchResults = new();
    private DispatcherTimer? _lyricsReturnTimer;
    private int _deferredLyricsLoadRevision;
    private bool _lyricsRefreshScheduled;
    private bool _areLyricsToolsExpanded;
    private Flyout? LyricsSearchFlyout => LyricsSearchToolButton.Flyout as Flyout;
    private Flyout? LyricsOffsetFlyout => LyricsOffsetToolButton.Flyout as Flyout;
    private PlaybackViewModel ViewModel => App.Services.Playback;

    public FullPlayPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += FullPlayPage_Loaded;
        Unloaded += FullPlayPage_Unloaded;
    }

    private void FullPlayPage_Loaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Lyrics.CollectionChanged += Lyrics_CollectionChanged;

        _lyricsReturnTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(LyricsReturnSeconds)
        };
        _lyricsReturnTimer.Tick += (_, _) =>
        {
            _lyricsReturnTimer.Stop();
            LyricsStageHost.EndManualBrowse();
        };

        // 歌词全量文本布局成本高（每行 2 个 FormattedText），延迟到进入转场结束后执行。
        var deferredRevision = ++_deferredLyricsLoadRevision;
        var settleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TransitionSettleMilliseconds)
        };
        settleTimer.Tick += (_, _) =>
        {
            settleTimer.Stop();
            if (deferredRevision == _deferredLyricsLoadRevision && IsLoaded)
            {
                RefreshLyricsStage(LyricsPositionUpdateKind.TrackChanged);
            }
        };
        settleTimer.Start();
    }

    private void FullPlayPage_Unloaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Lyrics.CollectionChanged -= Lyrics_CollectionChanged;
        if (_lyricsReturnTimer is not null)
        {
            _lyricsReturnTimer.Stop();
            _lyricsReturnTimer = null;
        }
    }

    private double EffectiveLyricsPosition => Math.Max(
        0,
        ViewModel.PositionSeconds - ViewModel.LyricsOffsetSeconds);

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack))
        {
            Dispatcher.UIThread.Post(() =>
            {
                SeekSlider.Value = 0;
                RefreshLyricsStage(LyricsPositionUpdateKind.TrackChanged);
            });
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.IsPlaying))
        {
            LyricsStageHost.UpdatePlaybackSample(
                EffectiveLyricsPosition,
                ViewModel.IsPlaying,
                LyricsPositionUpdateKind.PauseResume);
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.PositionSeconds))
        {
            LyricsStageHost.UpdatePlaybackSample(
                EffectiveLyricsPosition,
                ViewModel.IsPlaying,
                LyricsPositionUpdateKind.Sample);
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.LyricsOffsetSeconds))
        {
            LyricsStageHost.UpdatePlaybackSample(
                EffectiveLyricsPosition,
                ViewModel.IsPlaying,
                LyricsPositionUpdateKind.OffsetChanged);
        }
    }

    private void Lyrics_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_lyricsRefreshScheduled)
        {
            return;
        }

        _lyricsRefreshScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _lyricsRefreshScheduled = false;
            if (IsLoaded)
            {
                RefreshLyricsStage(LyricsPositionUpdateKind.TrackChanged);
            }
        });
    }

    private void RefreshLyricsStage(LyricsPositionUpdateKind updateKind)
    {
        LyricsStageHost.SetLyrics(
            ViewModel.Lyrics.Select(line => line.Line).ToArray(),
            ViewModel.Lyrics.Count + ViewModel.LyricsOffsetSeconds.GetHashCode());
        LyricsStageHost.UpdatePlaybackSample(EffectiveLyricsPosition, ViewModel.IsPlaying, updateKind);
    }

    private void LyricsStage_LyricInvoked(object? sender, LyricsLineInvokedEventArgs e)
    {
        _lyricsReturnTimer?.Stop();
        ViewModel.SeekToLyric(e.LineIndex);
        LyricsStageHost.UpdatePlaybackSample(
            EffectiveLyricsPosition,
            ViewModel.IsPlaying,
            LyricsPositionUpdateKind.Seek);
    }

    private void LyricsStage_ManualBrowseChanged(object? sender, LyricsManualBrowseChangedEventArgs e)
    {
        if (!e.IsManualBrowsing || _lyricsReturnTimer is null)
        {
            return;
        }

        _lyricsReturnTimer.Stop();
        _lyricsReturnTimer.Start();
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        App.Services.Shell.GoBackCommand.Execute(null);
    }

    private void VolumeSlider_ValueChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            ViewModel.SetVolume(slider.Value);
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
        ViewModel.Seek(SeekSlider.Value);
        LyricsStageHost.UpdatePlaybackSample(
            Math.Max(0, SeekSlider.Value - ViewModel.LyricsOffsetSeconds),
            ViewModel.IsPlaying,
            LyricsPositionUpdateKind.Seek);
    }

    private void QueueList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list
            || list.SelectedItem is not PlaybackQueueItemViewModel item)
        {
            return;
        }

        list.SelectedIndex = -1;
        if (ViewModel.PlayQueueTrackCommand.CanExecute(item.Track))
        {
            ViewModel.PlayQueueTrackCommand.Execute(item.Track);
        }
    }

    private void LyricsToolsToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        var expanding = !_areLyricsToolsExpanded;
        _areLyricsToolsExpanded = expanding;
        LyricsToolActions.IsHitTestVisible = expanding;
        LyricsToolActions.Opacity = expanding ? 1 : 0;
        LyricsToolsToggleIcon.Text = expanding ? "\uE711" : "\uE713";
    }

    private async void SearchLyricsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentTrack is null)
        {
            return;
        }

        if (!_areLyricsToolsExpanded)
        {
            LyricsToolsToggleButton_Click(this, e);
        }

        LyricsSearchFlyout.ShowAt(LyricsSearchToolButton);
        await RunLyricsSearchAsync();
    }

    private async void LyricsSearchRunButton_Click(object? sender, RoutedEventArgs e)
    {
        await RunLyricsSearchAsync();
    }

    private async void LyricsSearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunLyricsSearchAsync();
        }
    }

    private async Task RunLyricsSearchAsync()
    {
        var query = string.IsNullOrWhiteSpace(LyricsSearchInput.Text)
            ? ViewModel.CurrentTitle
            : LyricsSearchInput.Text.Trim();
        LyricsSearchStatusText.IsVisible = true;
        LyricsSearchStatusText.Text = "正在搜索…";
        try
        {
            var results = await ViewModel.SearchOnlineLyricsAsync(query);
            _lyricsSearchResults.Clear();
            foreach (var result in results)
            {
                _lyricsSearchResults.Add(result);
            }

            LyricsSearchResults.ItemsSource = _lyricsSearchResults;
            LyricsSearchStatusText.IsVisible = _lyricsSearchResults.Count == 0;
            LyricsSearchStatusText.Text = _lyricsSearchResults.Count == 0 ? "未找到匹配的歌词" : string.Empty;
        }
        catch (Exception error)
        {
            LyricsSearchStatusText.IsVisible = true;
            LyricsSearchStatusText.Text = $"搜索失败：{error.Message}";
        }
    }

    private async void LyricsSearchResults_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list
            || list.SelectedItem is not LyricsSearchResultModel result)
        {
            return;
        }

        list.SelectedIndex = -1;
        LyricsSearchStatusText.IsVisible = true;
        LyricsSearchStatusText.Text = $"正在应用 {result.Provider} 歌词…";
        var applied = await ViewModel.ApplyLyricsSearchResultAsync(result);
        if (applied)
        {
            LyricsSearchFlyout.Hide();
            return;
        }

        LyricsSearchStatusText.Text = "应用失败，请重试";
    }

    private void LyricsOffsetFlyout_Opened(object? sender, EventArgs e)
    {
        LyricsOffsetValidationText.IsVisible = false;
        LyricsOffsetInput.Text = ViewModel.LyricsOffsetSeconds.ToString(
            "+0.0;-0.0;0.0",
            CultureInfo.InvariantCulture);
        LyricsOffsetInput.Focus();
        LyricsOffsetInput.SelectAll();
    }

    private async void LyricsOffsetInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ApplyLyricsOffsetInputAsync();
    }

    private async void ApplyLyricsOffsetButton_Click(object? sender, RoutedEventArgs e)
    {
        await ApplyLyricsOffsetInputAsync();
    }

    private async Task ApplyLyricsOffsetInputAsync()
    {
        if (await ViewModel.ApplyLyricsOffsetAsync(LyricsOffsetInput.Text))
        {
            LyricsOffsetValidationText.IsVisible = false;
            LyricsOffsetFlyout.Hide();
            return;
        }

        LyricsOffsetValidationText.IsVisible = true;
        LyricsOffsetInput.Focus();
        LyricsOffsetInput.SelectAll();
    }
}
