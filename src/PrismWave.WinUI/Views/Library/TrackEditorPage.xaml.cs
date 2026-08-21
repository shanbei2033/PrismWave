using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using PrismWave_WinUI.ViewModels.Library;
using PrismWave_WinUI.Views.Dialogs;
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
        Loaded += TrackEditorPage_Loaded;
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.SaveCompleted -= ViewModel_SaveCompleted;
            DataContext = null;
        };
    }

    private TrackEditorViewModel ViewModel => App.Services.TrackEditor;

    private async void TrackEditorPage_Loaded(object sender, RoutedEventArgs e)
    {
        // 初次导航或从缓存恢复时主动渲染封面（PropertyChanged 可能已在构造前触发过）。
        await UpdateCoverPreviewAsync(ViewModel.CoverBytes);
        UpdateLockedBanner();
        UpdateStatusText();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (App.Services.Shell.GoBackCommand.CanExecute(null))
        {
            App.Services.Shell.GoBackCommand.Execute(null);
        }
    }

    private async void SearchCoverButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentTrack is null || !ViewModel.CanEditNow || ViewModel.IsBusy)
        {
            return;
        }

        var dialog = new CoverSearchDialog(App.Services.CoverService, ViewModel.CurrentTrack)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();

        // 弹窗关闭后，CoverService 已将封面下载到 cover_cache/images 并更新自定义封面。
        // 将下载的封面设为“待保存”，点保存时通过 TagLibSharp 嵌入音频文件 Tag.Pictures。
        if (ViewModel.CurrentTrack is not null)
        {
            var coverPath = App.Services.CoverService.ResolveCoverPath(ViewModel.CurrentTrack);
            if (!string.IsNullOrWhiteSpace(coverPath) && System.IO.File.Exists(coverPath))
            {
                await ViewModel.ApplyCoverSelectionAsync(coverPath);
            }
        }
    }

    private async void ResetCoverButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ResetCoverAsync();
    }

    private async void CoverPreview_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
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
            // MemoryStream.AsRandomAccessStream 比 InMemoryRandomAccessStream + DataWriter
            // 更可靠：DataWriter 释放时会关闭底层 stream 导致 BitmapImage 读不到数据。
            using var ms = new System.IO.MemoryStream(bytes);
            var image = new BitmapImage();
            await image.SetSourceAsync(ms.AsRandomAccessStream());
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
