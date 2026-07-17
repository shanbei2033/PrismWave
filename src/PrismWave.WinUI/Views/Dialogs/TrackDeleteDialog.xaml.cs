using Microsoft.UI.Xaml.Controls;

namespace PrismWave_WinUI.Views.Dialogs;

public sealed partial class TrackDeleteDialog : ContentDialog
{
    public TrackDeleteDialog()
    {
        InitializeComponent();
    }

    public bool DeleteSourceFile => DeleteSourceCheckBox.IsChecked == true;
}
