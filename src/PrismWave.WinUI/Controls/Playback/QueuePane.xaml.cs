using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave_WinUI.Controls.Playback;

public sealed partial class QueuePane : UserControl
{
    public QueuePane()
    {
        InitializeComponent();
    }

    public event EventHandler? CloseRequested;

    public void FocusCloseButton() => QueueCloseButton.Focus(FocusState.Programmatic);

    private void Queue_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaybackQueueItemViewModel item &&
            DataContext is PlaybackViewModel viewModel)
        {
            viewModel.PlayQueueTrackCommand.Execute(item.Track);
        }
    }

    private void Queue_DragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (DataContext is PlaybackViewModel viewModel)
        {
            StartupLog.Write($"queue.reorder.drag-started: count={args.Items.Count}");
            viewModel.BeginQueueReorder();
        }
    }

    private void Queue_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (DataContext is PlaybackViewModel viewModel)
        {
            StartupLog.Write($"queue.reorder.drag-completed: result={args.DropResult}");
            viewModel.CompleteQueueReorder();
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlaybackQueueItemViewModel item } &&
            DataContext is PlaybackViewModel viewModel)
        {
            viewModel.RemoveFromQueueCommand.Execute(item.Track);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
