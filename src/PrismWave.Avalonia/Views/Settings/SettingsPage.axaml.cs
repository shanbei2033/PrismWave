using Avalonia.Controls;
using Avalonia.Interactivity;
using PrismWave.Avalonia.Views.Dialogs;

namespace PrismWave.Avalonia.Views.Settings;

/// <summary>
/// 设置页（对齐 WinUI SettingsPage 四个 Tab；实验功能开关需确认风险提示）。
/// </summary>
public partial class SettingsPage : UserControl
{
    private bool _updatingExperimentalToggle;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = App.Services.Settings;
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    private async void ExperimentalFeatures_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingExperimentalToggle
            || sender is not ToggleSwitch toggle
            || DataContext is not PrismWave_WinUI.ViewModels.Settings.SettingsViewModel viewModel
            || toggle.IsChecked == viewModel.ExperimentalFeaturesEnabled)
        {
            return;
        }

        if (toggle.IsChecked != true)
        {
            await viewModel.SetExperimentalFeaturesEnabledAsync(false);
            return;
        }

        if (await RiskNoticeDialog.ShowAsync(this))
        {
            await viewModel.SetExperimentalFeaturesEnabledAsync(true);
            return;
        }

        _updatingExperimentalToggle = true;
        toggle.IsChecked = false;
        _updatingExperimentalToggle = false;
    }

    private async void ScanLogin_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string providerKey })
        {
            return;
        }

        await OnlineLoginDialog.ShowAsync(this, providerKey);
    }

    private async void SettingsPage_Loaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            await App.Services.Settings.OnlineAccounts.RefreshAccountsAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SettingsPage_Unloaded(object? sender, RoutedEventArgs e)
    {
    }
}
