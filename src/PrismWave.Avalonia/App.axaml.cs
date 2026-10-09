using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PrismWave.Avalonia.Infrastructure;
using PrismWave_WinUI.Infrastructure;

namespace PrismWave.Avalonia;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartupLog.Write("PrismWave.Avalonia starting");
            Services = AppServices.Create(() => desktop.MainWindow);
            desktop.MainWindow = new MainWindow(desktop.Args ?? Array.Empty<string>());
        }

        base.OnFrameworkInitializationCompleted();
    }
}
