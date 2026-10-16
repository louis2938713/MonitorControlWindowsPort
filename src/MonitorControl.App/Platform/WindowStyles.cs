using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MonitorControl.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MonitorControl.App.Platform;

/// <summary>Win32 window style helpers for WPF windows.</summary>
internal static class WindowStyles
{
    private static readonly HWND Topmost = new(-1);

    /// <summary>Non-activating, click-through, hidden from Alt+Tab (OSD).</summary>
    public static void MakeOverlay(IntPtr handle)
    {
        var hwnd = new HWND(handle);
        var style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        style |= (int)(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);
        _ = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style);
    }

    public static void BringToTop(IntPtr handle) =>
        PInvoke.SetWindowPos(new HWND(handle), Topmost, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
}

/// <summary>Places a WPF window in physical pixels on a given monitor, handling per-monitor DPI.</summary>
internal static class WindowPlacement
{
    /// <param name="position">Computes the top-left corner (pixels) from the window size (pixels).</param>
    public static void Place(Window window, PixelRect area, Func<int, int, (int X, int Y)> position)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var hwnd = new HWND(handle);
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        var center = new System.Drawing.Point(area.Left + (area.Width / 2), area.Top + (area.Height / 2));
        if (PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST) != PInvoke.MonitorFromPoint(center, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST))
        {
            // Move onto the target monitor first so WPF applies that monitor's DPI before measuring.
            PInvoke.SetWindowPos(hwnd, new HWND(-1), center.X, center.Y, 0, 0, flags);
        }

        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var (x, y) = position(width, height);
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));
        PInvoke.SetWindowPos(hwnd, new HWND(-1), x, y, 0, 0, flags);
    }
}
