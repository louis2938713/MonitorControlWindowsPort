using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using MonitorControl.App.Localization;
using MonitorControl.App.Platform;
using MonitorControl.App.Services;
using MonitorControl.App.UI.Tray;
using MonitorControl.Core;
using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;
using Wpf.Ui.Appearance;
using SerilogLog = Serilog.Log;

namespace MonitorControl.App;

/// <summary>Composition root: creates and wires the application services.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Services are disposed in OnExit, matching the WPF application lifetime.")]
public partial class App : Application
{
    private JsonSettingsStore? _settings;
    private WindowsDisplayPlatform? _platform;
    private ShadeHost? _shades;
    private DisplayManager? _displayManager;
    private AppController? _controller;
    private MessageWindow? _messageWindow;
    private SystemEventsMonitor? _systemEvents;
    private TrayIcon? _trayIcon;
    private bool _shutDown;

    /// <summary>Build number of this version (settings migration).</summary>
    public static int BuildNumber { get; } = int.TryParse(
        typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildNumber")?.Value,
        System.Globalization.NumberStyles.Integer,
        System.Globalization.CultureInfo.InvariantCulture,
        out var build) ? build : 1;

    public static string Version { get; } = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorControl", "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logging.Initialize();
        RegisterCrashHandlers();
        SerilogLog.Information("MonitorControl {Version} (build {Build}) starting", Version, BuildNumber);

        var safeMode = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        _settings = new JsonSettingsStore(SettingsPath);
        var prefs = new Prefs(_settings);
        Loc.SetLanguage(prefs.String(PrefKey.Language));
        ApplicationThemeManager.ApplySystemTheme();

        var ui = new WpfUiDispatcher(Dispatcher);
        var state = new AppState();
        _platform = new WindowsDisplayPlatform();
        _shades = new ShadeHost(_platform.BoundsOf);
        _displayManager = new DisplayManager(prefs, state, _platform, _shades, new NullOsd(), ui, new Alerts());
        _controller = new AppController(prefs, state, _displayManager, ui, new Alerts(), BuildNumber);

        _trayIcon = new TrayIcon(onQuit: Quit);
        _controller.Shell = new AppShell(_trayIcon);

        _messageWindow = new MessageWindow();
        _systemEvents = new SystemEventsMonitor(ui, _messageWindow);
        _systemEvents.Sleeping += (_, _) => _controller.Sleep();
        _systemEvents.Waking += (_, _) => _controller.Wake();
        _systemEvents.DisplaysChanged += (_, _) => _controller.DisplayReconfigured();

        _controller.Start(safeMode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShutdownServices();
        base.OnExit(e);
    }

    public void Quit() => Shutdown();

    private void ShutdownServices()
    {
        if (_shutDown)
        {
            return;
        }

        _shutDown = true;
        try
        {
            _controller?.Shutdown();
        }
        finally
        {
            _systemEvents?.Dispose();
            _messageWindow?.Dispose();
            _trayIcon?.Dispose();
            _displayManager?.Dispose();
            _shades?.Dispose();
            _platform?.Dispose();
            _settings?.Dispose();
            SerilogLog.CloseAndFlush();
        }
    }

    private void RegisterCrashHandlers()
    {
        Core.Log.Sink = (level, message, exception) =>
        {
            switch (level)
            {
                case Core.LogLevel.Debug: SerilogLog.Debug(exception, message); break;
                case Core.LogLevel.Info: SerilogLog.Information(exception, message); break;
                case Core.LogLevel.Warning: SerilogLog.Warning(exception, message); break;
                default: SerilogLog.Error(exception, message); break;
            }
        };
        DispatcherUnhandledException += (_, args) => OnFatalError(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => OnFatalError(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            SerilogLog.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private void OnFatalError(Exception? exception)
    {
        SerilogLog.Fatal(exception, "Unhandled exception");
        // Never leave displays dimmed behind us.
        try
        {
            _displayManager?.RestoreAllForExit();
            _settings?.Flush();
        }
#pragma warning disable CA1031 // Best effort while crashing
        catch (Exception ex)
#pragma warning restore CA1031
        {
            SerilogLog.Error(ex, "Unable to restore displays after crash");
        }

        SerilogLog.CloseAndFlush();
    }

    /// <summary>Placeholder until the OSD is implemented.</summary>
    private sealed class NullOsd : IOsd
    {
        public void Show(uint displayId, Command command, float value, float maxValue = 1, bool roundChiclet = false, bool locked = false)
        {
        }

        public void ShowVolumeDisabled(uint displayId)
        {
        }

        public void ShowMuteDisabled(uint displayId)
        {
        }

        public void PopEmpty(uint displayId, Command command)
        {
        }
    }
}
