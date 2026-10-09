using Avalonia.Threading;
using PrismWave_WinUI.ViewModels.Shell;

namespace PrismWave.Avalonia.Infrastructure;

/// <summary>
/// 开发/测试辅助：解析 --goto &lt;route&gt; 启动参数直达页面。
/// 详情路由会自动选中第一个专辑/艺术家；曲库尚未就绪时等待 LibraryChanged。
/// </summary>
public static class DevLaunch
{
    public static void Apply(ShellViewModel shell)
    {
        var route = FindRoute(App.LaunchArgs);
        if (route is null)
        {
            return;
        }

        var playRequested = App.LaunchArgs.Any(arg => string.Equals(arg, "--play", StringComparison.OrdinalIgnoreCase));

        switch (route)
        {
            case "LocalAlbumDetail":
                SelectThenNavigate(
                    shell,
                    route,
                    () => App.Services.Albums.SelectAlbumCommand.Execute(
                        App.Services.Albums.FilteredAlbums.FirstOrDefault()),
                    () => App.Services.LibraryService.Albums.Count > 0);
                return;
            case "ArtistDetail":
                SelectThenNavigate(
                    shell,
                    route,
                    () => App.Services.Artists.SelectArtistCommand.Execute(
                        App.Services.Artists.FilteredArtists.FirstOrDefault()),
                    () => App.Services.LibraryService.Artists.Count > 0);
                return;
            default:
                shell.Navigate(route);
                if (playRequested)
                {
                    ScheduleAutoPlay();
                }

                return;
        }
    }

    /// <summary>端到端验证 mpv 链路：曲库就绪后自动播放全部（第一首）。</summary>
    private static void ScheduleAutoPlay()
    {
        if (App.Services.Library.TrackCount > 0)
        {
            Dispatcher.UIThread.Post(() => App.Services.Library.PlayAllCommand.Execute(null));
            return;
        }

        void Handler(object? sender, EventArgs e)
        {
            if (App.Services.Library.TrackCount == 0)
            {
                return;
            }

            App.Services.LibraryService.LibraryChanged -= Handler;
            Dispatcher.UIThread.Post(() => App.Services.Library.PlayAllCommand.Execute(null));
        }

        App.Services.LibraryService.LibraryChanged += Handler;
    }

    private static string? FindRoute(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], "--goto", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void SelectThenNavigate(ShellViewModel shell, string route, Action select, Func<bool> ready)
    {
        if (ready())
        {
            select();
            shell.Navigate(route);
            return;
        }

        void Handler(object? sender, EventArgs e)
        {
            if (!ready())
            {
                return;
            }

            App.Services.LibraryService.LibraryChanged -= Handler;
            Dispatcher.UIThread.Post(() =>
            {
                select();
                shell.Navigate(route);
            });
        }

        App.Services.LibraryService.LibraryChanged += Handler;
    }
}
