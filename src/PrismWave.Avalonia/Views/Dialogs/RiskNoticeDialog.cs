using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;

namespace PrismWave.Avalonia.Views.Dialogs;

/// <summary>
/// 实验功能风险提示（对齐 WinUI RiskNoticeDialog 文案）。返回 true 表示用户同意开启。
/// </summary>
public static class RiskNoticeDialog
{
    public static async Task<bool> ShowAsync(Control host)
    {
        if (host.GetVisualRoot() is not Window window)
        {
            return false;
        }

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new InfoBar
        {
            IsOpen = true,
            Severity = InfoBarSeverity.Warning,
            Title = "Read this notice before enabling experimental features"
        });
        foreach (var line in new[]
        {
            "PrismWave is an independently developed local music player. BETA features may retrieve music metadata, lyrics, artwork, charts, search results or media addresses from third-party services.",
            "PrismWave is not affiliated with, authorized by, endorsed by or commercially associated with those platforms, rights holders or service providers, and does not claim rights to their content, APIs, audio, lyrics, artwork or trademarks.",
            "Some providers may use public pages, public or non-public endpoints, reverse-parsed responses and cached data. Availability can change because of platform policy, copyright, region, network, account, technical or legal restrictions.",
            "Enabling BETA features grants no right to download, record, save, redistribute or commercially use unauthorized content, or to bypass membership, regional, access-control, encryption or anti-scraping protections.",
            "PrismWave does not host or sell third-party music and cannot guarantee legality, accuracy, availability or continued access. You are responsible for complying with applicable laws and each provider's terms.",
        })
        {
            stack.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        }

        stack.Children.Add(new TextBlock
        {
            Text = "If you disagree with this notice or cannot confirm a lawful basis for your use, do not enable BETA features.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5
        });

        var dialog = new ContentDialog
        {
            Title = "BETA feature risk notice",
            Content = new ScrollViewer { MaxHeight = 420, Content = stack },
            PrimaryButtonText = "I understand and agree",
            CloseButtonText = "Disagree"
        };

        return await dialog.ShowAsync(window) == ContentDialogResult.Primary;
    }
}
