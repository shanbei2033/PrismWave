using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PrismWave_WinUI.ViewModels.Library;
using Windows.Storage.Streams;

namespace PrismWave_WinUI.Views.Library;

public sealed partial class TrackEditorPage : Page
{
    public TrackEditorPage()
    {
        InitializeComponent();
        DataContext = App.Services.TrackEditor;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.SaveCompleted += ViewModel_SaveCompleted;
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.SaveCompleted -= ViewModel_SaveCompleted;
            DataContext = null;
        };
    }

    private TrackEditorViewModel ViewModel => App.Services.TrackEditor;

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (App.Services.Shell.GoBackCommand.CanExecute(null))
        {
            App.Services.Shell.GoBackCommand.Execute(null);
        }
    }

    private async void ChangeCoverButton_Click(object sender, RoutedEventArgs e)
    {
        var handle = App.WindowHandle;
        if (handle == 0 || !ViewModel.CanEditNow || ViewModel.IsBusy)
        {
            return;
        }

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await ViewModel.ApplyCoverSelectionAsync(file.Path);
    }

    private void ViewModel_SaveCompleted(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (App.Services.Shell.GoBackCommand.CanExecute(null))
            {
                App.Services.Shell.GoBackCommand.Execute(null);
            }
        });
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TrackEditorViewModel.CoverBytes):
                await UpdateCoverPreviewAsync(ViewModel.CoverBytes);
                break;
            case nameof(TrackEditorViewModel.LockedReason):
                UpdateLockedBanner();
                break;
            case nameof(TrackEditorViewModel.StatusMessage):
                UpdateStatusText();
                break;
        }
    }

    private async Task UpdateCoverPreviewAsync(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            CoverPreview.Source = null;
            return;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            CoverPreview.Source = image;
        }
        catch
        {
            CoverPreview.Source = null;
        }
    }

    private void UpdateLockedBanner()
    {
        var locked = !string.IsNullOrEmpty(ViewModel.LockedReason);
        LockedBanner.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStatusText()
    {
        var hasStatus = !string.IsNullOrEmpty(ViewModel.StatusMessage);
        StatusText.Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Foreground = ViewModel.StatusIsError
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGreen);
    }
}
