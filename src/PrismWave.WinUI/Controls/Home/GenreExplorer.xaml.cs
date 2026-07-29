using System.Collections;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Controls.Home;

public sealed partial class GenreExplorer : UserControl
{
    private readonly WeakCollectionChangedListener<GenreExplorer> _channelListener;
    private readonly WeakCollectionChangedListener<GenreExplorer> _genreListener;

    public GenreExplorer()
    {
        InitializeComponent();
        _channelListener = new WeakCollectionChangedListener<GenreExplorer>(
            this,
            static (self, _, _) => self.RefreshItems(self.ChannelSections, self.ChannelItems));
        _genreListener = new WeakCollectionChangedListener<GenreExplorer>(
            this,
            static (self, _, _) => self.RefreshItems(self.GenreSections, self.GenreItems));
        Unloaded += (_, _) =>
        {
            _channelListener.Unsubscribe();
            _genreListener.Unsubscribe();
        };
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
        explorer._channelListener.Subscribe(args.NewValue);
        explorer.RefreshItems(explorer.ChannelSections, explorer.ChannelItems);
    }

    private static void OnGenreSectionsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var explorer = (GenreExplorer)dependencyObject;
        explorer._genreListener.Subscribe(args.NewValue);
        explorer.RefreshItems(explorer.GenreSections, explorer.GenreItems);
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
