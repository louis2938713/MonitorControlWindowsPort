using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MonitorControl.App.Localization;
using MonitorControl.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MonitorControl.App.UI.Tray;

/// <summary>The window opened from the notification area icon (equivalent of the status item menu).</summary>
public partial class FlyoutWindow
{
    private DateTime _hiddenAt = DateTime.MinValue;

    public FlyoutWindow()
    {
        InitializeComponent();
        FlowDirection = Loc.FlowDirection;
        Deactivated += (_, _) => HideFlyout();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HideFlyout();
            }
        };
    }

    /// <summary>Raised right before the flyout opens (refresh the "relevant display" sliders, stop key repeats…).</summary>
    public event EventHandler? Opening;

    /// <summary>True right after the flyout was dismissed by clicking the tray icon (to toggle instead of reopening).</summary>
    public bool WasJustHidden => (DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 300;

    public void HideFlyout()
    {
        if (IsVisible)
        {
            _hiddenAt = DateTime.UtcNow;
            Hide();
        }
    }

    /// <summary>Opens the flyout on the display under the mouse, next to the taskbar.</summary>
    public void ShowFlyout(PixelRect bounds, PixelRect workArea, int cursorX, int cursorY)
    {
        Opening?.Invoke(this, EventArgs.Empty);
        FlowDirection = Loc.FlowDirection;
        Opacity = 0;
        Show();
        UpdateLayout();
        Position(bounds, workArea, cursorX, cursorY);
        Opacity = 1;
        Activate();
    }

    private unsafe void Position(PixelRect bounds, PixelRect workArea, int cursorX, int cursorY)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        int x;
        int y;
        if (workArea.Top > bounds.Top)
        {
            // Taskbar at the top.
            x = Math.Clamp(cursorX - (width / 2), workArea.Left, workArea.Right - width);
            y = workArea.Top;
        }
        else if (workArea.Left > bounds.Left)
        {
            x = workArea.Left;
            y = Math.Clamp(cursorY - (height / 2), workArea.Top, workArea.Bottom - height);
        }
        else if (workArea.Right < bounds.Right)
        {
            x = workArea.Right - width;
            y = Math.Clamp(cursorY - (height / 2), workArea.Top, workArea.Bottom - height);
        }
        else
        {
            // Taskbar at the bottom (or auto-hidden): bottom right, like the Windows flyouts.
            x = Math.Clamp(cursorX - (width / 2), workArea.Left, workArea.Right - width);
            y = workArea.Bottom - height;
        }

        PInvoke.SetWindowPos(new HWND(handle), HWND.Null, x, y, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }
}
