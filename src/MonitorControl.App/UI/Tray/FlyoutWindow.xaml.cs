using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MonitorControl.App.Localization;
using MonitorControl.App.Platform;
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
        WindowPlacement.Place(this, workArea, (width, height) =>
        {
            if (workArea.Top > bounds.Top)
            {
                return (cursorX - (width / 2), workArea.Top); // Taskbar at the top.
            }

            if (workArea.Left > bounds.Left)
            {
                return (workArea.Left, cursorY - (height / 2)); // Taskbar on the left.
            }

            if (workArea.Right < bounds.Right)
            {
                return (workArea.Right - width, cursorY - (height / 2)); // Taskbar on the right.
            }

            // Taskbar at the bottom (or auto-hidden), like the Windows flyouts.
            return (cursorX - (width / 2), workArea.Bottom - height);
        });
        Opacity = 1;
        Activate();
    }
}
