using Avalonia.Controls;
using Avalonia.Interactivity;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.ViewModels.Player;

namespace PrismWave.Avalonia.Controls.Playback;

public partial class QueuePane : UserControl
{
    public event EventHandler? CloseRequested;

    public QueuePane()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: PlaybackQueueItemViewModel item } &&
            DataContext is PlaybackViewModel viewModel)
        {
            viewModel.RemoveFromQueueCommand.Execute(item.Track);
        }
    }

    private void QueueList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
        {
            return;
        }

        if (e.AddedItems[0] is PlaybackQueueItemViewModel item
            && DataContext is PlaybackViewModel viewModel)
        {
            viewModel.PlayQueueTrackCommand.Execute(item.Track);
        }

        QueueList.SelectedIndex = -1;
    }
}
