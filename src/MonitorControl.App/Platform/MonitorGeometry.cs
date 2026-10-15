using Microsoft.Win32;
using MonitorControl.Core.Platform;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace MonitorControl.App.Platform;

/// <summary>Cursor position and monitor rectangles in physical pixels.</summary>
internal static unsafe class MonitorGeometry
{
    public static (int X, int Y) CursorPosition() => PInvoke.GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    /// <summary>Bounds and work area of the monitor containing a point.</summary>
    public static (PixelRect Bounds, PixelRect WorkArea) MonitorAt(int x, int y)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        return PInvoke.GetMonitorInfo(monitor, &info)
            ? (DisplayEnumerator.ToRect(info.rcMonitor), DisplayEnumerator.ToRect(info.rcWork))
            : (new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040));
    }

    /// <summary>True when the taskbar uses the light theme (dark icons needed).</summary>
    public static bool TaskbarUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    /// <summary>True when apps use the light theme.</summary>
    public static bool AppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
