using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Controls.Home;

public sealed partial class TrendingSongList : UserControl
{
    private readonly WeakCollectionChangedListener<TrendingSongList> _itemsListener;
    private RankedTrackItem? _moreItem;

    public TrendingSongList()
    {
        InitializeComponent();
        _itemsListener = new WeakCollectionChangedListener<TrendingSongList>(
            this,
            static (self, _, _) => self.RefreshRankedTracks());
        Unloaded += (_, _) => _itemsListener.Unsubscribe();
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(TrendingSongList),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty PlayCommandProperty = DependencyProperty.Register(
        nameof(PlayCommand),
        typeof(ICommand),
        typeof(TrendingSongList),
        new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => (ICommand?)GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public ObservableCollection<RankedTrackItem> LeftTracks { get; } = new();
    public ObservableCollection<RankedTrackItem> RightTracks { get; } = new();

    private static void OnItemsSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var songList = (TrendingSongList)dependencyObject;
        songList._itemsListener.Subscribe(args.NewValue);
        songList.RefreshRankedTracks();
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

    private void TrendingSongList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var state = e.NewSize.Width >= 900 ? "Wide" : "Compact";
        VisualStateManager.GoToState(this, state, useTransitions: false);
    }

    private void TrackList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RankedTrackItem { Track: { } track } &&
            PlayCommand?.CanExecute(track) != false)
        {
            PlayCommand?.Execute(track);
        }
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        _moreItem = (sender as FrameworkElement)?.DataContext as RankedTrackItem;
    }

    private void PlayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_moreItem?.Track is { } track && PlayCommand?.CanExecute(track) != false)
        {
            PlayCommand?.Execute(track);
        }
    }

    private void AddToLibraryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_moreItem?.Track is { } track)
        {
            _ = App.Services.LibraryService.AddOnlineTrackAsync(ToTrackModel(track));
        }
    }

    private void FavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_moreItem?.Track is { } track)
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
