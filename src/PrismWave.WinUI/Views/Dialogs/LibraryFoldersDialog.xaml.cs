using Microsoft.UI.Xaml.Controls;

namespace PrismWave_WinUI.Views.Dialogs;

public sealed partial class LibraryFoldersDialog : ContentDialog
{
    public LibraryFoldersDialog()
    {
        InitializeComponent();
        DataContext = App.Services.LibraryFolders;
    }
}
