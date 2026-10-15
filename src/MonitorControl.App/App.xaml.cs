using System.Windows;
using System.Windows.Threading;
using MonitorControl.App.Services;
using MonitorControl.App.UI.Tray;
using Serilog;

namespace MonitorControl.App;

/// <summary>Composition root: creates and wires the application services.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Services are disposed in OnExit, matching the WPF application lifetime.")]
public partial class App : Application
{
    private TrayIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logging.Initialize();
        RegisterCrashHandlers();
        Log.Information("MonitorControl starting");

        _trayIcon = new TrayIcon(onQuit: Quit);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Goodbye!");
        _trayIcon?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void Quit() => Shutdown();

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) => OnFatalError(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => OnFatalError(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private static void OnFatalError(Exception? exception)
    {
        Log.Fatal(exception, "Unhandled exception");
        Log.CloseAndFlush();
    }

    /// <summary>Runs an action on the UI thread.</summary>
    public static void RunOnUi(Action action)
    {
        var dispatcher = Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        }
    }
}
