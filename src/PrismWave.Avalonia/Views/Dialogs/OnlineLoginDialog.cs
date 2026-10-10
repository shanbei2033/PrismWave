using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using PrismWave_WinUI.Infrastructure.Online;
using PrismWave_WinUI.Models;
using QRCoder;

namespace PrismWave.Avalonia.Views.Dialogs;

/// <summary>
/// 在线账号扫码登录对话框（对齐 WinUI OnlineLoginDialog，复用 Core 的 OnlineLoginDialogCoordinator）。
/// </summary>
public static class OnlineLoginDialog
{
    public static async Task ShowAsync(Control host, string providerKey)
    {
        if (host.GetVisualRoot() is not Window window)
        {
            return;
        }

        var progress = new ProgressRing { Width = 32, Height = 32, IsActive = true };
        var qrImage = new Image { Stretch = Stretch.Uniform };
        var qrHost = new Panel();
        qrHost.Children.Add(progress);
        qrHost.Children.Add(qrImage);

        var providerText = new TextBlock
        {
            Text = providerKey.Equals("netease", StringComparison.OrdinalIgnoreCase)
                ? "NetEase Cloud Music"
                : "QQ Music",
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var statusText = new TextBlock
        {
            Text = "Creating a secure sign-in code…",
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.75
        };

        var refreshButton = new Button
        {
            Content = "Refresh QR code",
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false
        };

        var whiteCard = new Border
        {
            Width = 260,
            Height = 260,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.White,
            Child = qrHost
        };

        var stack = new StackPanel { Spacing = 14, Width = 340 };
        stack.Children.Add(whiteCard);
        stack.Children.Add(new StackPanel
        {
            Spacing = 5,
            Children = { providerText, statusText }
        });
        stack.Children.Add(refreshButton);

        var coordinator = new OnlineLoginDialogCoordinator(App.Services.OnlineAccountService);
        var dialog = new ContentDialog
        {
            Title = "Scan to connect",
            Content = stack,
            CloseButtonText = "Cancel"
        };
        var cancelled = false;

        void CloseRequested() => dialog.Hide();

        coordinator.ChallengeChanged += challenge =>
        {
            var pngBytes = challenge.QrImageBytes.Length > 0
                ? challenge.QrImageBytes
                : CreateQrPng(challenge.QrPayload);
            Dispatcher.UIThread.Post(() =>
            {
                if (cancelled)
                {
                    return;
                }

                qrImage.Source = Bitmap.DecodeToWidth(new MemoryStream(pngBytes), 480);
                progress.IsActive = false;
                statusText.Text = "Use the provider app to scan this QR code.";
            });
        };
        coordinator.SnapshotChanged += snapshot =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (cancelled)
                {
                    return;
                }

                statusText.Text = snapshot.State switch
                {
                    OnlineProviderAuthState.WaitingForScan => "Waiting for scan…",
                    OnlineProviderAuthState.Scanned => "Scanned. Confirm sign-in on your phone.",
                    OnlineProviderAuthState.Authenticated => "Connected.",
                    OnlineProviderAuthState.Expired => "This QR code has expired.",
                    OnlineProviderAuthState.Failed => snapshot.StatusMessage ?? "Sign-in failed.",
                    _ => snapshot.StatusMessage ?? "Not connected.",
                };
                refreshButton.IsVisible = snapshot.State
                    is OnlineProviderAuthState.Expired or OnlineProviderAuthState.Failed;
                if (snapshot.State == OnlineProviderAuthState.Authenticated)
                {
                    CloseRequested();
                }
            });
        };

        async Task RunLoginAsync()
        {
            qrImage.Source = null;
            progress.IsActive = true;
            refreshButton.IsVisible = false;
            statusText.Text = "Creating a secure sign-in code…";
            try
            {
                await coordinator.RunAsync(providerKey);
            }
            catch (Exception)
            {
                progress.IsActive = false;
                statusText.Text = "Could not create the sign-in code.";
                refreshButton.IsVisible = true;
            }
        }

        refreshButton.Click += async (_, _) => await RunLoginAsync();
        dialog.Closed += (_, _) =>
        {
            cancelled = true;
            coordinator.Cancel();
            coordinator.Dispose();
        };

        var showTask = dialog.ShowAsync(window);
        _ = RunLoginAsync();
        await showTask;
    }

    private static byte[] CreateQrPng(string payload)
    {
        using var qrData = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrData);
        return qrCode.GetGraphic(10);
    }
}
