using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PrismWave_WinUI.Controls.Navigation;

public sealed partial class Sidebar : UserControl
{
    public Sidebar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyExpandedState();
            ApplySelection();
        };
    }

    public IReadOnlyList<SidebarItem> PrimaryItems { get; } =
    [
        new("Home", "首页", "\uE80F"),
        new("Search", "搜索", "\uE721"),
        new("Library", "库", "\uE8D6"),
        new("Albums", "专辑", "\uEB9F"),
        new("Artists", "艺术家", "\uE77B"),
        new("Favorites", "我最爱的", "\uEB51"),
        new("Hits", "HITS", "\uE95B")
    ];

    public IReadOnlyList<SidebarItem> FooterItems { get; } =
    [
        new("Settings", "设置", "\uE713")
    ];

    public static readonly DependencyProperty SelectedRouteProperty = DependencyProperty.Register(
        nameof(SelectedRoute),
        typeof(string),
        typeof(Sidebar),
        new PropertyMetadata("Home", (dependencyObject, _) => ((Sidebar)dependencyObject).ApplySelection()));

    public static readonly DependencyProperty NavigateCommandProperty = DependencyProperty.Register(
        nameof(NavigateCommand),
        typeof(ICommand),
        typeof(Sidebar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FolderCountProperty = DependencyProperty.Register(
        nameof(FolderCount),
        typeof(int),
        typeof(Sidebar),
        new PropertyMetadata(0));

    public static readonly DependencyProperty TrackCountProperty = DependencyProperty.Register(
        nameof(TrackCount),
        typeof(int),
        typeof(Sidebar),
        new PropertyMetadata(0));

    public static readonly DependencyProperty FavoriteCountProperty = DependencyProperty.Register(
        nameof(FavoriteCount),
        typeof(int),
        typeof(Sidebar),
        new PropertyMetadata(0));

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded),
        typeof(bool),
        typeof(Sidebar),
        new PropertyMetadata(true, (dependencyObject, _) => ((Sidebar)dependencyObject).ApplyExpandedState()));

    public string SelectedRoute
    {
        get => (string)GetValue(SelectedRouteProperty);
        set => SetValue(SelectedRouteProperty, value);
    }

    public ICommand? NavigateCommand
    {
        get => (ICommand?)GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    public int FolderCount
    {
        get => (int)GetValue(FolderCountProperty);
        set => SetValue(FolderCountProperty, value);
    }

    public int TrackCount
    {
        get => (int)GetValue(TrackCountProperty);
        set => SetValue(TrackCountProperty, value);
    }

    public int FavoriteCount
    {
        get => (int)GetValue(FavoriteCountProperty);
        set => SetValue(FavoriteCountProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    private void NavigationList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SidebarItem item && NavigateCommand?.CanExecute(item.Route) == true)
        {
            NavigateCommand.Execute(item.Route);
        }
    }

    private void FooterList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SidebarItem item && NavigateCommand?.CanExecute(item.Route) == true)
        {
            NavigateCommand.Execute(item.Route);
        }
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
    }

    private void ApplyExpandedState()
    {
        if (CollapseButton is null)
        {
            return;
        }

        Width = IsExpanded
            ? (double)Application.Current.Resources["PrismShellSidebarWidth"]
            : (double)Application.Current.Resources["PrismShellSidebarCollapsedWidth"];
        CollapseButton.HorizontalAlignment = IsExpanded
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Center;
        CollapseGlyph.Glyph = IsExpanded ? "\uE76B" : "\uE76C";
        ToolTipService.SetToolTip(CollapseButton, IsExpanded ? "折叠侧栏" : "展开侧栏");
    }

    private void ApplySelection()
    {
        if (NavigationList is null || FooterList is null)
        {
            return;
        }

        var route = SelectedRoute switch
        {
            "TopPlaylist" or "AlbumDetail" => "Home",
            _ => SelectedRoute
        };
        NavigationList.SelectedItem = PrimaryItems.FirstOrDefault(item => item.Route == route);
        FooterList.SelectedItem = FooterItems.FirstOrDefault(item => item.Route == route);
    }

    public sealed record SidebarItem(string Route, string Label, string Glyph);
}
