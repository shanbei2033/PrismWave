using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave_WinUI.Controls.Playback;

public sealed partial class QueuePane : UserControl
{
    public QueuePane()
    {
        InitializeComponent();
    }

    private void Queue_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackModel track && DataContext is PlaybackViewModel viewModel)
        {
            viewModel.PlayQueueTrackCommand.Execute(track);
        }
    }

    private void Queue_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (DataContext is PlaybackViewModel viewModel)
        {
            viewModel.PersistQueueOrder();
        }
    }

    private void Remove_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrackModel track } && DataContext is PlaybackViewModel viewModel)
        {
            viewModel.RemoveFromQueueCommand.Execute(track);
        }
    }
}
