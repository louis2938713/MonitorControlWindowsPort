using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using MonitorControl.Core;

namespace MonitorControl.App.UI.Tray;

/// <summary>Notification area icon (equivalent of the macOS status item).</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIcon(Action onQuit)
    {
        var quitItem = new MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => onQuit();

        _icon = new TaskbarIcon
        {
            ToolTipText = AppInfo.Name,
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon.ico")),
            ContextMenu = new ContextMenu { Items = { quitItem } },
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Dispose() => _icon.Dispose();
}
