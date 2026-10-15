using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using MonitorControl.App.Localization;
using MonitorControl.App.Platform;
using MonitorControl.App.Services;
using MonitorControl.App.UI;
using MonitorControl.App.UI.Osd;
using MonitorControl.App.UI.Settings;
using MonitorControl.App.UI.Tray;
using MonitorControl.Core;
using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Input;
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
    private OsdService? _osd;
    private ShortcutStore? _shortcuts;
    private AppShell? _shell;
    private AudioService? _audio;
    private KeyboardService? _keyboard;
    private Prefs? _prefs;
    private SettingsWindow? _settingsWindow;
    private UpdateService? _updates;
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
        _prefs = prefs;
        Loc.SetLanguage(prefs.String(PrefKey.Language));
        Display.NameLocalizer = name => name switch
        {
            DisplayEnumerator.BuiltInDisplayName => Loc.T("Displays.Type.BuiltIn"),
            DisplayEnumerator.UnknownDisplayName => Loc.T("Common.Unknown"),
            _ => name,
        };
        ApplicationThemeManager.ApplySystemTheme();

        var ui = new WpfUiDispatcher(Dispatcher);
        var state = new AppState();
        _platform = new WindowsDisplayPlatform();
        _shades = new ShadeHost(_platform.BoundsOf);
        _osd = new OsdService(_platform.WorkAreaOf);
        _displayManager = new DisplayManager(prefs, state, _platform, _shades, _osd, ui, new Alerts());
        _controller = new AppController(prefs, state, _displayManager, ui, new Alerts(), BuildNumber);

        var actions = new FlyoutActions(OpenSettings, CheckForUpdates, Quit);
        var flyoutWindow = new FlyoutWindow();
        _trayIcon = new TrayIcon(flyoutWindow, actions);
        var flyout = new FlyoutBuilder(flyoutWindow, _displayManager, state, actions, _trayIcon.SetVisible);
        _shortcuts = new ShortcutStore(_settings);
        _shell = new AppShell(_trayIcon, flyout, _shortcuts);
        _controller.Shell = _shell;

        _messageWindow = new MessageWindow();
        _audio = new AudioService();
        var displayManager = _displayManager;
        _keyboard = new KeyboardService(prefs, ui, _messageWindow, _shortcuts, shell => (
            new MediaKeyHandler(displayManager, _audio, shell),
            new ShortcutManager(displayManager, ui, shell, KeyboardService.RepeatRate)));
        _keyboard.ConflictsDetected += (_, names) => _trayIcon.Notify(
            Loc.T("App.Name"),
            Loc.F("Notification.ShortcutConflicts", string.Join(", ", names.Select(n => ShortcutNames.Display(n)))));
        _shell.Keyboard = _keyboard;
        _shell.DisplaySettingsReload = () => _settingsWindow?.Displays.LoadDisplayList();
        _displayManager.DisplaySettingsChanged += (_, _) => _settingsWindow?.Displays.LoadDisplayList();
        flyoutWindow.Opening += (_, _) => _keyboard.ShortcutManager.Disengage();
        _audio.DefaultOutputChanged += (_, _) => _keyboard.Update();

        _systemEvents = new SystemEventsMonitor(ui, _messageWindow);
        _systemEvents.Sleeping += (_, _) => _controller.Sleep();
        _systemEvents.Waking += (_, _) => _controller.Wake();
        _systemEvents.DisplaysChanged += (_, _) => _controller.DisplayReconfigured();

        _controller.Start(safeMode);
        StartupRegistration.Refresh();
        _updates = new UpdateService(prefs, beforeRestart: ShutdownServices);
        _updates.Start();
        if (_controller.IsFirstLaunch)
        {
            new OnboardingWindow().Show();
        }
#if DEBUG
        DebugPreview(Environment.GetCommandLineArgs());
#endif
    }

#if DEBUG
    /// <summary>Development aid: <c>--ui-preview flyout</c> opens a window right after startup (screenshots, manual checks).</summary>
    private void DebugPreview(string[] args)
    {
        var index = Array.IndexOf(args, "--ui-preview");
        if (index < 0 || index + 1 >= args.Length)
        {
            return;
        }

        var target = args[index + 1];
        Dispatcher.BeginInvoke(() =>
        {
            if (target == "flyout")
            {
                _trayIcon?.OpenFlyout();
            }
            else if (target == "osd" && _displayManager is { Displays.Count: > 0 } manager)
            {
                _osd?.Show(manager.Displays[0].Id, Command.Brightness, 0.6f, roundChiclet: true);
            }
            else if (target.StartsWith("settings", StringComparison.Ordinal))
            {
                OpenSettings(target.Length > 8 ? target[8] - '0' : 0);
                _settingsWindow!.Topmost = true;
            }
            else if (target == "onboarding")
            {
                new OnboardingWindow { Topmost = true }.Show();
            }
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
#endif

    protected override void OnExit(ExitEventArgs e)
    {
        ShutdownServices();
        base.OnExit(e);
    }

    public void Quit() => Shutdown();

    /// <summary>Opens (or brings to front) the settings window.</summary>
    public void OpenSettings() => OpenSettings(page: -1);

    private void OpenSettings(int page)
    {
        if (_settingsWindow is null)
        {
            var context = new SettingsContext(_prefs!, _controller!, _displayManager!, _shortcuts!, _keyboard!, _audio!, CheckForUpdates, Quit);
            _settingsWindow = new SettingsWindow(context);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        if (page >= 0)
        {
            _settingsWindow.ShowPage(page);
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    private void CheckForUpdates() => _ = _updates?.CheckAsync(userInitiated: true);

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
            _updates?.Dispose();
            _systemEvents?.Dispose();
            _keyboard?.Dispose();
            _audio?.Dispose();
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
        DispatcherUnhandledException += (_, args) =>
        {
            // A UI glitch must not kill a background utility: log it and keep running.
            SerilogLog.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
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
}
