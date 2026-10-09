using Avalonia.Controls;

namespace PrismWave.Avalonia.Views.Placeholders;

/// <summary>
/// Phase 1 占位页：按 Phase 2-6 计划逐个替换为实装页面。
/// </summary>
public partial class RoutePlaceholderPage : UserControl
{
    public RoutePlaceholderPage(string routeTitle)
    {
        InitializeComponent();
        TitleText.Text = routeTitle;
    }
}
