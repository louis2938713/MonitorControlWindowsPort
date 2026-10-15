using MonitorControl.App.UI.Tray;
using MonitorControl.Core;
using MonitorControl.Core.Input;

namespace MonitorControl.App.Services;

/// <summary>Connects the core logic to the UI: tray icon and flyout, keyboard handling, settings window.</summary>
internal sealed class AppShell(TrayIcon trayIcon, FlyoutBuilder flyout, ShortcutStore shortcuts) : IAppShell
{
    /// <summary>Set once the keyboard service exists (it needs the display manager, created after the shell).</summary>
    public KeyboardService? Keyboard { get; set; }

    /// <summary>Called when the Displays settings page must reload (set by the settings window).</summary>
    public Action? DisplaySettingsReload { get; set; }

    public void UpdateMenus() => flyout.UpdateMenus();

    public void UpdateKeyboard() => Keyboard?.Update();

    public void ReloadDisplaySettings() => DisplaySettingsReload?.Invoke();

    public void ForceStatusItemVisible() => trayIcon.SetVisible(true);

    public void ApplyFirstLaunchDefaults() => shortcuts.ApplyDefaults();
}
