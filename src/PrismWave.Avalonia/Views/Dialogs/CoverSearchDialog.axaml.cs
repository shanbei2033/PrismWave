using Avalonia.Controls;
using Avalonia.Input;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave.Avalonia.Views.Dialogs;

/// <summary>
/// 封面搜索替换对话框内容（对齐 WinUI 版 CoverSearchDialog）：
/// 打开后自动按当前曲目/艺人搜索在线封面，点击结果应用并经 CoverApplied 通知宿主关闭。
/// 作为 ContentDialog 的 Content 使用，由 FullPlayPage 负责弹出与防重入。
/// </summary>
public sealed partial class CoverSearchDialog : UserControl
{
    private bool _autoSearchStarted;

    public CoverSearchDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => StartAutoSearch();
    }

    private void StartAutoSearch()
    {
        if (_autoSearchStarted || DataContext is not CoverSearchViewModel viewModel)
        {
            return;
        }

        _autoSearchStarted = true;
        viewModel.SearchCommand.Execute(null);
    }

    private void QueryBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is CoverSearchViewModel viewModel)
        {
            viewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
