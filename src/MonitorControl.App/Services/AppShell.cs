using MonitorControl.App.UI.Tray;
using MonitorControl.Core;

namespace MonitorControl.App.Services;

/// <summary>Connects the core logic to the UI: tray icon and flyout, keyboard handling, settings window.</summary>
internal sealed class AppShell(TrayIcon trayIcon, FlyoutBuilder flyout) : IAppShell
{
    public void UpdateMenus() => flyout.UpdateMenus();

    public void UpdateKeyboard()
    {
    }

    public void ReloadDisplaySettings()
    {
    }

    public void ForceStatusItemVisible() => trayIcon.SetVisible(true);

    public void ApplyFirstLaunchDefaults()
    {
    }
}
