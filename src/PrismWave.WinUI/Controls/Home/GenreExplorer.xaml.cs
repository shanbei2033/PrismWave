using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Controls.Home;

public sealed partial class GenreExplorer : UserControl
{
    private INotifyCollectionChanged? _channelCollection;
    private INotifyCollectionChanged? _genreCollection;

    public GenreExplorer()
    {
        InitializeComponent();
    }

    public event EventHandler<SectionOpenRequestedEventArgs>? OpenRequested;

    public static readonly DependencyProperty ChannelSectionsProperty = DependencyProperty.Register(
        nameof(ChannelSections),
        typeof(IEnumerable),
        typeof(GenreExplorer),
        new PropertyMetadata(null, OnChannelSectionsChanged));

    public static readonly DependencyProperty GenreSectionsProperty = DependencyProperty.Register(
        nameof(GenreSections),
        typeof(IEnumerable),
        typeof(GenreExplorer),
        new PropertyMetadata(null, OnGenreSectionsChanged));

    public IEnumerable? ChannelSections
    {
        get => (IEnumerable?)GetValue(ChannelSectionsProperty);
        set => SetValue(ChannelSectionsProperty, value);
    }

    public IEnumerable? GenreSections
    {
        get => (IEnumerable?)GetValue(GenreSectionsProperty);
        set => SetValue(GenreSectionsProperty, value);
    }

    public ObservableCollection<GenreExplorerEntry> ChannelItems { get; } = new();

    public ObservableCollection<GenreExplorerEntry> GenreItems { get; } = new();

    private static void OnChannelSectionsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var explorer = (GenreExplorer)dependencyObject;
        explorer.ObserveCollection(
            ref explorer._channelCollection,
            args.NewValue,
            explorer.ChannelSections_CollectionChanged);
        explorer.RefreshItems(explorer.ChannelSections, explorer.ChannelItems);
    }

    private static void OnGenreSectionsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var explorer = (GenreExplorer)dependencyObject;
        explorer.ObserveCollection(
            ref explorer._genreCollection,
            args.NewValue,
            explorer.GenreSections_CollectionChanged);
        explorer.RefreshItems(explorer.GenreSections, explorer.GenreItems);
    }

    private void ObserveCollection(
        ref INotifyCollectionChanged? observedCollection,
        object? source,
        NotifyCollectionChangedEventHandler handler)
    {
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged -= handler;
        }

        observedCollection = source as INotifyCollectionChanged;
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged += handler;
        }
    }

    private void ChannelSections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshItems(ChannelSections, ChannelItems);
    }

    private void GenreSections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshItems(GenreSections, GenreItems);
    }

    private void RefreshItems(
        IEnumerable? source,
        ObservableCollection<GenreExplorerEntry> target)
    {
        target.Clear();
        if (source is null)
        {
            return;
        }

        foreach (var section in source.OfType<HomeSectionModel>())
        {
            target.Add(new GenreExplorerEntry
            {
                Title = section.Title,
                Subtitle = string.IsNullOrWhiteSpace(section.Subtitle)
                    ? "打开此分类"
                    : section.Subtitle,
                IconGlyph = ResolveIconGlyph(section.Id),
                Section = section
            });
        }
    }

    private void ExplorerEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HomeSectionModel section })
        {
            OpenRequested?.Invoke(this, new SectionOpenRequestedEventArgs(section));
        }
    }

    private static string ResolveIconGlyph(string sectionId)
    {
        return sectionId.ToLowerInvariant() switch
        {
            "world-charts" => "\uE774",
            "audius-trending" => "\uE720",
            "style-electronic" => "\uE945",
            "style-hiphop" => "\uE7F6",
            "style-jazz" => "\uE189",
            "style-ambient" => "\uE8D6",
            "style-rock" => "\uE7C3",
            _ => "\uE8D6"
        };
    }
}

public sealed class GenreExplorerEntry
{
    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string IconGlyph { get; set; } = string.Empty;

    public HomeSectionModel? Section { get; set; }
}

public sealed class SectionOpenRequestedEventArgs(HomeSectionModel section) : EventArgs
{
    public HomeSectionModel Section { get; } = section;
}
