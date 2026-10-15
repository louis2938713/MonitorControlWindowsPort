using System.Runtime.InteropServices;
using Microsoft.Win32;
using MonitorControl.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MonitorControl.App.Platform;

/// <summary>
/// System sleep/wake, display power and display configuration changes
/// (the NSWorkspace and CGDisplayReconfiguration observers of the macOS app). Events are raised on the UI thread.
/// </summary>
internal sealed unsafe class SystemEventsMonitor : IDisposable
{
    private const int PbtPowerSettingChange = 0x8013;
    private readonly IUiDispatcher _ui;
    private readonly MessageWindow _window;
    private readonly HPOWERNOTIFY _displayStateNotification;

    public SystemEventsMonitor(IUiDispatcher ui, MessageWindow window)
    {
        _ui = ui;
        _window = window;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _window.MessageReceived += OnMessage;
        var guid = PInvoke.GUID_CONSOLE_DISPLAY_STATE;
        _displayStateNotification = PInvoke.RegisterPowerSettingNotification(new HANDLE(_window.Handle), &guid, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    public event EventHandler? Sleeping;

    public event EventHandler? Waking;

    public event EventHandler? DisplaysChanged;

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _window.MessageReceived -= OnMessage;
        if (!_displayStateNotification.IsNull)
        {
            PInvoke.UnregisterPowerSettingNotification(_displayStateNotification);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                _ui.Post(() => Sleeping?.Invoke(this, EventArgs.Empty));
                break;
            case PowerModes.Resume:
                _ui.Post(() => Waking?.Invoke(this, EventArgs.Empty));
                break;
            case PowerModes.StatusChange:
            default:
                break;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _ui.Post(() => DisplaysChanged?.Invoke(this, EventArgs.Empty));

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != (int)PInvoke.WM_POWERBROADCAST || wParam != PbtPowerSettingChange || lParam == IntPtr.Zero)
        {
            return;
        }

        var setting = (POWERBROADCAST_SETTING*)lParam;
        if (setting->PowerSetting != PInvoke.GUID_CONSOLE_DISPLAY_STATE || setting->DataLength < sizeof(uint))
        {
            return;
        }

        // 0 = off, 1 = on, 2 = dimmed.
        var state = Marshal.ReadInt32((IntPtr)(&setting->Data));
        if (state == 0)
        {
            _ui.Post(() => Sleeping?.Invoke(this, EventArgs.Empty));
        }
        else if (state == 1)
        {
            _ui.Post(() => Waking?.Invoke(this, EventArgs.Empty));
        }
    }
}
