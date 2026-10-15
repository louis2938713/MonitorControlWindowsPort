using System.Windows.Interop;

namespace MonitorControl.App.Platform;

/// <summary>Hidden message-only window receiving system notifications (power settings, raw input).</summary>
internal sealed class MessageWindow : IDisposable
{
    private const int HwndMessage = -3;
    private readonly HwndSource _source;

    public MessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("MonitorControl.MessageWindow")
        {
            ParentWindow = new IntPtr(HwndMessage),
            WindowStyle = 0,
        });
        _source.AddHook(Hook);
    }

    /// <summary>Raised for every message: (msg, wParam, lParam).</summary>
    public event Action<int, IntPtr, IntPtr>? MessageReceived;

    public IntPtr Handle => _source.Handle;

    public void Dispose() => _source.Dispose();

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        MessageReceived?.Invoke(msg, wParam, lParam);
        return IntPtr.Zero;
    }
}
