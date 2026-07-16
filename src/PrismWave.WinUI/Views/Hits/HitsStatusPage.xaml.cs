using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Hits;

namespace PrismWave_WinUI.Views.Hits;

public sealed partial class HitsStatusPage : Page
{
    private readonly DispatcherQueueTimer _timer;
    private HitsStatusViewModel ViewModel => App.Services.Hits;

    public HitsStatusPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => ViewModel.Tick();
        Loaded += HitsStatusPage_Loaded;
        Unloaded += HitsStatusPage_Unloaded;
    }

    private async void HitsStatusPage_Loaded(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        await ViewModel.InitializeAsync();
    }

    private void HitsStatusPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
    }

    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HitsScheduleItemModel track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }
}
