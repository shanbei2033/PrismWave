using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PrismWave_WinUI.ViewModels.Hits;

namespace PrismWave.Avalonia.Views.Hits;

/// <summary>
/// HITS 电台沉浸页（对齐 WinUI HitsStatusPage：背景封面 + 居中封面开关 + LIVE 状态 + 曲目信息）。
/// Esc 返回由 ShellPage 统一处理（沉浸覆盖层可见时）。
/// </summary>
public partial class HitsStatusPage : UserControl
{
    private readonly HitsStatusViewModel _viewModel;
    private DispatcherTimer? _tickTimer;

    public HitsStatusPage()
    {
        InitializeComponent();
        _viewModel = App.Services.Hits;
        DataContext = _viewModel;
        Loaded += HitsStatusPage_Loaded;
        Unloaded += HitsStatusPage_Unloaded;
    }

    private async void HitsStatusPage_Loaded(object? sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _tickTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => _viewModel.Tick());
        _tickTimer.Start();

        try
        {
            await _viewModel.InitializeAsync();
            if (_viewModel.IsAvailable && !_viewModel.IsSessionActive)
            {
                await _viewModel.PrepareHitsSessionCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PrismWave_WinUI.Infrastructure.StartupLog.Write("HITS page init failed", exception);
        }
    }

    private void HitsStatusPage_Unloaded(object? sender, RoutedEventArgs e)
    {
        _viewModel.EndHitsSessionCommand.Execute(null);
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _tickTimer?.Stop();
        _tickTimer = null;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 从简动效：曲目切换与暂停态直接刷新绑定，不做装饰性过渡。
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        App.Services.Shell.GoBackCommand.Execute(null);
    }
}
