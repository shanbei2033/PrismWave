using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PrismWave.Avalonia.Infrastructure;
using PrismWave_WinUI.Infrastructure;

namespace PrismWave.Avalonia;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    /// <summary>启动参数（供测试用 --goto 路由等场景）。</summary>
    public static string[] LaunchArgs { get; private set; } = Array.Empty<string>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            LaunchArgs = desktop.Args ?? Array.Empty<string>();
            StartupLog.Write("PrismWave.Avalonia starting");
            Services = AppServices.Create(() => desktop.MainWindow);
            desktop.MainWindow = new MainWindow(desktop.Args ?? Array.Empty<string>());
            _ = InitializeLibraryAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>对齐 WinUI OnLaunched：启动时初始化本地曲库（扫描已配置文件夹）。</summary>
    private static async Task InitializeLibraryAsync()
    {
        try
        {
            await Services.LibraryService.InitializeAsync();
            StartupLog.Write("Local library initialized");
        }
        catch (Exception exception)
        {
            StartupLog.Write("Avalonia library init failed", exception);
        }
    }
}
