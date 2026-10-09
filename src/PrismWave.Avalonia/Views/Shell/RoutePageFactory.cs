using Avalonia.Controls;
using PrismWave.Avalonia.Views.Placeholders;

namespace PrismWave.Avalonia.Views.Shell;

/// <summary>
/// Shell 路由 → 页面实例工厂。Phase 2-6 逐步替换占位页。
/// </summary>
public static class RoutePageFactory
{
    public static Control Create(string route)
    {
        return route switch
        {
            "Home" => new RoutePlaceholderPage("首页"),
            "Search" => new RoutePlaceholderPage("搜索"),
            "Library" => new RoutePlaceholderPage("库"),
            "Albums" => new RoutePlaceholderPage("专辑"),
            "Artists" => new RoutePlaceholderPage("艺术家"),
            "Favorites" => new RoutePlaceholderPage("我最爱的"),
            "Hits" => new RoutePlaceholderPage("HITS"),
            "Settings" => new RoutePlaceholderPage("设置"),
            _ => new RoutePlaceholderPage(route)
        };
    }
}
