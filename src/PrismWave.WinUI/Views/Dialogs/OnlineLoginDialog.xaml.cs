using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PrismWave_WinUI.Infrastructure.Online;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using QRCoder;
using Windows.Storage.Streams;

namespace PrismWave_WinUI.Views.Dialogs;

public sealed partial class OnlineLoginDialog : ContentDialog, IDisposable
{
    private readonly string _providerKey;
    private readonly OnlineLoginDialogCoordinator _coordinator;
    private bool _disposed;

    public OnlineLoginDialog(IOnlineAccountService accountService, string providerKey)
    {
        InitializeComponent();
        _providerKey = providerKey;
        _coordinator = new OnlineLoginDialogCoordinator(accountService);
        _coordinator.ChallengeChanged += Coordinator_ChallengeChanged;
        _coordinator.SnapshotChanged += Coordinator_SnapshotChanged;
        ProviderText.Text = providerKey.Equals("netease", StringComparison.OrdinalIgnoreCase)
            ? "NetEase Cloud Music"
            : "QQ Music";
        Opened += OnlineLoginDialog_Opened;
        Closed += OnlineLoginDialog_Closed;
    }

    public void CancelLogin() => _coordinator.Cancel();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.ChallengeChanged -= Coordinator_ChallengeChanged;
        _coordinator.SnapshotChanged -= Coordinator_SnapshotChanged;
        _coordinator.Dispose();
    }

    private void OnlineLoginDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _ = RunLoginAsync();
    }

    private void OnlineLoginDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        CancelLogin();
    }

    private async void RefreshQr_Click(object sender, RoutedEventArgs e)
    {
        RefreshQrButton.Visibility = Visibility.Collapsed;
        QrImage.Source = null;
        QrProgress.IsActive = true;
        StatusText.Text = "Creating a new sign-in code…";
        await RunLoginAsync();
    }

    private async Task RunLoginAsync()
    {
        try
        {
            await _coordinator.RunAsync(_providerKey);
        }
        catch (Exception)
        {
            await EnqueueUiAsync(() =>
            {
                QrProgress.IsActive = false;
                StatusText.Text = "Could not create the sign-in code.";
                RefreshQrButton.Visibility = Visibility.Visible;
            });
        }
    }

    private void Coordinator_ChallengeChanged(OnlineLoginChallenge challenge)
    {
        var pngBytes = challenge.QrImageBytes.Length > 0
            ? challenge.QrImageBytes
            : CreateQrPng(challenge.QrPayload);
        _ = EnqueueUiAsync(async () =>
        {
            QrImage.Source = await CreateBitmapAsync(pngBytes);
            QrProgress.IsActive = false;
            StatusText.Text = "Use the provider app to scan this QR code.";
        });
    }

    private void Coordinator_SnapshotChanged(OnlineAccountSnapshot snapshot)
    {
        _ = EnqueueUiAsync(() =>
        {
            StatusText.Text = snapshot.State switch
            {
                OnlineProviderAuthState.WaitingForScan => "Waiting for scan…",
                OnlineProviderAuthState.Scanned => "Scanned. Confirm sign-in on your phone.",
                OnlineProviderAuthState.Authenticated => "Connected.",
                OnlineProviderAuthState.Expired => "This QR code has expired.",
                OnlineProviderAuthState.Failed => snapshot.StatusMessage ?? "Sign-in failed.",
                _ => snapshot.StatusMessage ?? "Not connected.",
            };
            RefreshQrButton.Visibility = snapshot.State is OnlineProviderAuthState.Expired or OnlineProviderAuthState.Failed
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (snapshot.State == OnlineProviderAuthState.Authenticated)
            {
                Hide();
            }
        });
    }

    private Task EnqueueUiAsync(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }))
        {
            completion.TrySetCanceled();
        }

        return completion.Task;
    }

    private Task EnqueueUiAsync(Func<Task> action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }))
        {
            completion.TrySetCanceled();
        }

        return completion.Task;
    }

    private static byte[] CreateQrPng(string payload)
    {
        using var qrData = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrData);
        return qrCode.GetGraphic(10);
    }

    private static async Task<BitmapImage> CreateBitmapAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
