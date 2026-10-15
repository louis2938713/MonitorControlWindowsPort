using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using MonitorControl.App.Localization;

namespace MonitorControl.App.UI.Tray;

/// <summary>Notification area icon (equivalent of the macOS status item).</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _quitItem;

    public TrayIcon(Action onQuit)
    {
        _quitItem = new MenuItem();
        _quitItem.Click += (_, _) => onQuit();

        _icon = new TaskbarIcon
        {
            ToolTipText = Loc.T("App.Name"),
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon.ico")),
            ContextMenu = new ContextMenu { Items = { _quitItem } },
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
        Refresh();
    }

    public void Refresh() => _quitItem.Header = Loc.T("Menu.Quit");

    public void SetVisible(bool visible) => _icon.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void Dispose() => _icon.Dispose();
}
