using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    /// Gets the root frame for navigation. This is only available after the window is created.
    /// </summary>
    public static Frame? GetRootFrame() => Window is MainWindow mainWindow ? 
        (Frame)mainWindow.GetRootFrameElement() : null;

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
    /// Tracks whether the main window has been closed, so async launch
    /// continuations can bail out early instead of touching disposed UI.
    /// </summary>
    private static bool _isWindowClosed;

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
            
            // Create window first
            Window = new MainWindow(launchSize);
            Window.Closed += (_, _) => _isWindowClosed = true;
            Window.Activate();
            StartupLog.Write("Window activated");
            
            // Wait a brief moment for window initialization to complete
            await Task.Delay(100);
            if (_isWindowClosed) return;
            
            // Navigate to splash page immediately
            var rootFrame = App.GetRootFrame();
            if (rootFrame != null && rootFrame.Content == null)
            {
                rootFrame.Navigate(typeof(Views.Shell.SplashPage));
                StartupLog.Write("SplashPage navigation requested");
            }
            if (_isWindowClosed) return;
            
            // Start service initialization in parallel with splash animation
            var initTask = Services.LibraryService.InitializeAsync();
            
            // Play splash animation after a short delay for rendering
            if (rootFrame?.Content is Views.Shell.SplashPage splashPage)
            {
                await splashPage.PlayEntranceSequenceAsync();
                if (_isWindowClosed) return;
                
                // Logo flies out to the right and fades before revealing the home page
                await splashPage.PlayExitSequenceAsync();
            }
            if (_isWindowClosed) return;
            
            // Switch to main interface with a fade-in reveal after splash completes
            rootFrame?.Navigate(
                typeof(Views.Shell.ShellPage),
                null,
                new Microsoft.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());
            StartupLog.Write("Switched to ShellPage");
            
            // Ensure library initialization completes
            await initTask;
            StartupLog.Write("Splash animation completed, switching to main UI");
            
            // Auto-check for updates if the user has enabled it
            if (!_isWindowClosed && Services.SettingsService.Current.AutoCheckUpdate)
            {
                _ = Task.Run(async () =>
                {
                    var result = await Services.UpdateService.CheckForUpdatesAsync();
                    if (result.HasUpdate)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (!_isWindowClosed && Window is MainWindow mainWindow)
                            {
                                mainWindow.ShowUpdateNotification(result);
                            }
                        });
                    }
                });
            }
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
