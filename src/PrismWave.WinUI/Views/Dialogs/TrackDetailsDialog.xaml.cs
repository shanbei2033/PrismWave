using Microsoft.UI.Xaml.Controls;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Views.Dialogs;

public sealed partial class TrackDetailsDialog : ContentDialog
{
    public TrackDetailsDialog(TrackModel track)
    {
        InitializeComponent();
        Track = track;
        DataContext = track;
    }

    public TrackModel Track { get; }
}
