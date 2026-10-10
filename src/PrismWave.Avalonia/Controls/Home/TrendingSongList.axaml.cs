using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PrismWave_WinUI.Models;

namespace PrismWave.Avalonia.Controls.Home;

/// <summary>
/// 首页全球热门榜单（对齐 WinUI TrendingSongList）。
/// 宽度 ≥900 双列，否则上下两列。
/// </summary>
public sealed partial class TrendingSongList : UserControl
{
    private INotifyCollectionChanged? _observableItems;

    private double _lastResponsiveWidth = -1;

    public TrendingSongList()
    {
        InitializeComponent();
        LayoutUpdated += OnSelfLayoutUpdated;
    }

    private void OnSelfLayoutUpdated(object? sender, EventArgs e)
    {
        var width = Bounds.Width;
        if (double.IsNaN(width) || width <= 0 || Math.Abs(width - _lastResponsiveWidth) < 0.5)
        {
            return;
        }

        _lastResponsiveWidth = width;
        var wide = width >= 900;
        ListGrid.ColumnDefinitions[1].Width =
            new GridLength(wide ? 1 : 0, wide ? GridUnitType.Star : GridUnitType.Pixel);
        ListGrid.ColumnSpacing = wide ? 24 : 0;
        ListGrid.RowSpacing = wide ? 0 : 2;
        Grid.SetRow(RightList, wide ? 0 : 1);
        Grid.SetColumn(RightList, wide ? 1 : 0);
    }

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = StyledProperty<IEnumerable?>.Register<
        TrendingSongList, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<ICommand?> PlayCommandProperty = StyledProperty<ICommand?>.Register<
        TrendingSongList, ICommand?>(nameof(PlayCommand));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public ObservableCollection<RankedTrackItem> LeftTracks { get; } = new();

    public ObservableCollection<RankedTrackItem> RightTracks { get; } = new();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            AttachItems(change.OldValue as IEnumerable, change.NewValue as IEnumerable);
        }
    }

    private void AttachItems(IEnumerable? oldItems, IEnumerable? newItems)
    {
        if (_observableItems is not null)
        {
            _observableItems.CollectionChanged -= Items_CollectionChanged;
        }

        _observableItems = newItems as INotifyCollectionChanged;
        if (_observableItems is not null)
        {
            _observableItems.CollectionChanged += Items_CollectionChanged;
        }

        RefreshRankedTracks();
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshRankedTracks();
    }

    private void RefreshRankedTracks()
    {
        var ranked = ItemsSource?
            .OfType<HomeTrackModel>()
            .Take(10)
            .Select((track, index) => new RankedTrackItem(index + 1, track))
            .ToArray() ?? [];

        Replace(LeftTracks, ranked.Take(5));
        Replace(RightTracks, ranked.Skip(5).Take(5));
    }

    private static void Replace(
        ObservableCollection<RankedTrackItem> target,
        IEnumerable<RankedTrackItem> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void TrackList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        var item = listBox.SelectedItem as RankedTrackItem;
        listBox.SelectedIndex = -1;
        if (item?.Track is { } track && PlayCommand?.CanExecute(track) != false)
        {
            PlayCommand?.Execute(track);
        }
    }

    private void MoreButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            (button.ContextFlyout as FluentAvalonia.UI.Controls.FAMenuFlyout)?.ShowAt(button);
        }
    }

    private void PlayMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as FluentAvalonia.UI.Controls.MenuFlyoutItem)?.Tag is RankedTrackItem { Track: { } track } &&
            PlayCommand?.CanExecute(track) != false)
        {
            PlayCommand?.Execute(track);
        }
    }

    private void AddToLibraryMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as FluentAvalonia.UI.Controls.MenuFlyoutItem)?.Tag is RankedTrackItem { Track: { } track })
        {
            _ = App.Services.LibraryService.AddOnlineTrackAsync(ToTrackModel(track));
        }
    }

    private void FavoriteMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as FluentAvalonia.UI.Controls.MenuFlyoutItem)?.Tag is RankedTrackItem { Track: { } track })
        {
            _ = App.Services.LibraryService.ToggleFavoriteAsync(ToTrackModel(track));
        }
    }

    private static TrackModel ToTrackModel(HomeTrackModel track)
    {
        var id = $"{track.Provider}:{track.ProviderTrackId ?? track.Title}";
        var path = $"online://{track.Provider}/{Uri.EscapeDataString(track.ProviderTrackId ?? track.Title)}";
        return new TrackModel(
            id,
            path,
            track.Title,
            track.Artist,
            track.Album,
            track.Duration,
            track.CoverUrl,
            IsRemote: true,
            Provider: track.Provider,
            PlaybackUrl: track.AudioUrl,
            OnlineProviderTrackId: track.ProviderTrackId);
    }

    public sealed class RankedTrackItem
    {
        public RankedTrackItem()
        {
        }

        public RankedTrackItem(int rank, HomeTrackModel track)
        {
            Rank = rank;
            Track = track;
        }

        public int Rank { get; set; }
        public HomeTrackModel? Track { get; set; }
        public string RankLabel => Rank.ToString("00", CultureInfo.InvariantCulture);
        public string Title => Track?.Title ?? string.Empty;
        public string Artist => Track?.Artist ?? string.Empty;
        public string Duration => Track?.Duration ?? "--:--";
        public string? CoverUrl => Track?.CoverUrl;
    }
}
