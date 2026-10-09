using Avalonia;
using Avalonia.Threading;
using LaciaIsland.Services;

namespace LaciaIsland;

internal static class Program
{
    private static Mutex? _single;
    /// <summary>Fallback: draw the island in a regular Avalonia window instead of the Win32 layered window.</summary>
    public static bool UseAvaloniaWindow { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Log.ToConsole = args.Contains("--console");
        UseAvaloniaWindow = args.Contains("--avalonia-window");
        _single = new Mutex(true, "LaciaIsland.SingleInstance.v1", out var created);
        if (!created && !args.Contains("--multi"))
        {
            Log.Info("already running, exiting");
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Error("unhandled", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("task", e.Exception);
            e.SetObserved();
        };

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Error("fatal", ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // The island draws itself into a layered window; Avalonia only renders the settings window.
            // Classic redirection-surface mode keeps that independent of the WinUI compositor clock.
            .With(UseAvaloniaWindow
                ? new Win32PlatformOptions()
                : new Win32PlatformOptions { CompositionMode = new[] { Win32CompositionMode.RedirectionSurface } })
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(_ =>
            {
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    Log.Error("ui", e.Exception);
                    e.Handled = true;
                };
            });
}
