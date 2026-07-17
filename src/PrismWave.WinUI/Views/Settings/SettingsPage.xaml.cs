using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.ViewModels.Settings;
using PrismWave_WinUI.Views.Dialogs;

namespace PrismWave_WinUI.Views.Settings;

public sealed partial class SettingsPage : Page
{
    private bool _updatingExperimentalToggle;
    private OnlineLoginDialog? _onlineLoginDialog;
    private CancellationTokenSource? _accountInitializationCancellation;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = App.Services.Settings;
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    private async void ExperimentalFeatures_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_updatingExperimentalToggle
            || sender is not ToggleSwitch toggle
            || DataContext is not SettingsViewModel viewModel
            || toggle.IsOn == viewModel.ExperimentalFeaturesEnabled)
        {
            return;
        }

        if (!toggle.IsOn)
        {
            await viewModel.SetExperimentalFeaturesEnabledAsync(false);
            return;
        }

        var dialog = new RiskNoticeDialog { XamlRoot = XamlRoot };
        var accepted = await dialog.ShowAsync() == ContentDialogResult.Primary;
        if (accepted)
        {
            await viewModel.SetExperimentalFeaturesEnabledAsync(true);
            return;
        }

        _updatingExperimentalToggle = true;
        toggle.IsOn = false;
        _updatingExperimentalToggle = false;
    }

    private async void ScanLogin_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_onlineLoginDialog is not null
            || sender is not Button { Tag: string providerKey }
            || DataContext is not SettingsViewModel { OnlineAccounts.IsLoginEnabled: true })
        {
            return;
        }

        var dialog = new OnlineLoginDialog(App.Services.OnlineAccountService, providerKey)
        {
            XamlRoot = XamlRoot,
        };
        _onlineLoginDialog = dialog;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            dialog.Dispose();
            _onlineLoginDialog = null;
        }
    }

    private async void SignOut_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string providerKey }
            && DataContext is SettingsViewModel viewModel)
        {
            await viewModel.OnlineAccounts.SignOutCommand.ExecuteAsync(providerKey);
        }
    }

    private void SettingsPage_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _accountInitializationCancellation?.Cancel();
        _accountInitializationCancellation?.Dispose();
        _accountInitializationCancellation = null;
        _onlineLoginDialog?.CancelLogin();
        _onlineLoginDialog?.Hide();
    }

    private async void SettingsPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _accountInitializationCancellation?.Cancel();
        _accountInitializationCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _accountInitializationCancellation = cancellation;
        try
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                await viewModel.OnlineAccounts.RefreshAccountsAsync(cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_accountInitializationCancellation, cancellation))
            {
                _accountInitializationCancellation = null;
                cancellation.Dispose();
            }
        }
    }
}
