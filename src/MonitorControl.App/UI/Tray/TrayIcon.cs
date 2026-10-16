using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using MonitorControl.App.Localization;
using MonitorControl.App.Platform;

namespace MonitorControl.App.UI.Tray;

/// <summary>
/// Notification area icon (equivalent of the macOS status item): left click toggles the slider flyout,
/// right click shows a small menu that is always available.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly FlyoutWindow _flyout;
    private readonly MenuItem _settingsItem = new();
    private readonly MenuItem _updateItem = new();
    private readonly MenuItem _quitItem = new();

    public TrayIcon(FlyoutWindow flyout, FlyoutActions actions)
    {
        _flyout = flyout;
        _settingsItem.Click += (_, _) => actions.OpenSettings();
        _updateItem.Click += (_, _) => actions.CheckForUpdates();
        _quitItem.Click += (_, _) => actions.Quit();

        _icon = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            Icon = LoadAppIcon(),
            ContextMenu = new ContextMenu { Items = { _settingsItem, _updateItem, new Separator(), _quitItem } },
        };
        _icon.TrayLeftMouseUp += (_, _) => ToggleFlyout();
        _icon.ForceCreate(enablesEfficiencyMode: false);
        Loc.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    public bool IsVisible => _icon.Visibility == Visibility.Visible;

    public void Refresh()
    {
        _icon.ToolTipText = Loc.T("App.Name");
        _settingsItem.Header = Loc.T("Menu.Settings");
        _updateItem.Header = Loc.T("Menu.CheckForUpdates");
        _quitItem.Header = Loc.T("Menu.Quit");
        if (_icon.ContextMenu is { } menu)
        {
            menu.FlowDirection = Loc.FlowDirection;
        }
    }

    public void SetVisible(bool visible) => _icon.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Windows notification from the app (shown even when the icon is hidden in the overflow area).</summary>
    public void Notify(string title, string message) => _icon.ShowNotification(title, message);

    /// <summary>Opens the flyout on the display under the mouse.</summary>
    public void OpenFlyout()
    {
        var (x, y) = MonitorGeometry.CursorPosition();
        var (bounds, workArea) = MonitorGeometry.MonitorAt(x, y);
        _flyout.ShowFlyout(bounds, workArea, x, y);
    }

    public void Dispose()
    {
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }

    /// <summary>The MonitorControl app icon, at the size Windows uses in the notification area.</summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico")).Stream;
        var size = (int)Math.Round(SystemParameters.SmallIconWidth * GetSystemScale());
        return new System.Drawing.Icon(stream, size, size);
    }

    private static double GetSystemScale()
    {
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters());
        return source.CompositionTarget?.TransformToDevice.M11 ?? 1;
    }

    private void ToggleFlyout()
    {
        if (_flyout.IsVisible || _flyout.WasJustHidden)
        {
            _flyout.HideFlyout();
            return;
        }

        OpenFlyout();
    }
}
