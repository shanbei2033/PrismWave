using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using PrismWave_WinUI.ViewModels.Library;

namespace PrismWave.Avalonia.Views.Library.Dialogs;

/// <summary>
/// 音乐文件夹管理对话框（对齐 WinUI LibraryFoldersDialog 的核心功能）。
/// </summary>
public static class LibraryFoldersDialog
{
    public static async Task ShowAsync(Control host)
    {
        if (host.GetVisualRoot() is not Window window)
        {
            return;
        }

        var folders = App.Services.LibraryFolders;
        var list = new ListBox { MinHeight = 200, MaxHeight = 420 };
        RebuildRows(list, folders);

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock
        {
            Text = folders.StatusText,
            FontSize = 12,
            Opacity = 0.75,
            Foreground = Brushes.White
        });
        if (!string.IsNullOrEmpty(folders.Error))
        {
            stack.Children.Add(new TextBlock
            {
                Text = folders.Error,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x7A))
            });
        }
        stack.Children.Add(list);

        var addButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    IconText("\uE710"),
                    new TextBlock { Text = "Add folder", VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        addButton.Click += async (_, _) => await folders.AddFolderCommand.ExecuteAsync(null);
        stack.Children.Add(addButton);

        var dialog = new ContentDialog
        {
            Title = "Music Folders",
            Content = stack,
            CloseButtonText = "Close"
        };

        // 移除按钮内已带重建逻辑（见 RebuildRows）
        await dialog.ShowAsync(window);
    }

    private static void RebuildRows(ListBox list, LibraryFolderManagerViewModel folders)
    {
        list.Items.Clear();
        foreach (var entry in folders.FolderEntries)
        {
            var row = new Grid { ColumnSpacing = 10, Margin = new Thickness(4) };
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = entry.Path,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brushes.White
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{entry.TrackCount} tracks · {entry.StatusText}",
                FontSize = 12,
                Opacity = 0.7,
                Foreground = Brushes.White
            });
            Grid.SetColumn(text, 0);
            row.Children.Add(text);

            var removeButton = new Button
            {
                Content = IconText("\uE74D"),
                Padding = new Thickness(8, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            removeButton.Click += async (_, _) =>
            {
                await folders.RemoveFolderCommand.ExecuteAsync(entry.Path);
                RebuildRows(list, folders);
            };
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);

            list.Items.Add(row);
        }
    }

    private static TextBlock IconText(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
        FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center
    };
}
