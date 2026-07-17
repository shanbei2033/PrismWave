using Microsoft.UI.Xaml;
using PrismWave_WinUI.Infrastructure;

namespace PrismWave_WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private static readonly Lazy<AppServices> ServiceContainer = new(() =>
    {
        StartupLog.Write("Creating AppServices");
        return AppServices.Create();
    });

    public static AppServices Services => ServiceContainer.Value;

    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        StartupLog.Write("App constructor");
        UnhandledException += App_UnhandledException;
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            StartupLog.Write("OnLaunched");
            StartupLog.Write($"Launch arguments: {args.Arguments}");
            var launchSize = WindowLaunchSize.ResolveLaunch(args.Arguments);
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Window = new MainWindow(launchSize);
            Window.Activate();
            StartupLog.Write("Window activated");
            await Services.LibraryService.InitializeAsync();
            StartupLog.Write("Local library initialized");
        }
        catch (Exception exception)
        {
            StartupLog.Write("OnLaunched failed", exception);
            throw;
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupLog.Write("Unhandled XAML exception", e.Exception);
    }
}
