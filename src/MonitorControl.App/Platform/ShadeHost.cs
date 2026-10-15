using System.Runtime.InteropServices;
using MonitorControl.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MonitorControl.App.Platform;

/// <summary>
/// Black, click-through, always-on-top layered windows used to dim displays (the "shade" of the macOS app).
/// The window has a constant alpha (LWA_ALPHA), so the compositor does the work and alpha changes are cheap.
/// All calls are marshalled to the UI thread.
/// </summary>
internal sealed unsafe class ShadeHost : IShadeHost, IDisposable
{
    private const string ClassName = "MonitorControlShade";
    private static readonly HWND Topmost = new(-1);
    private static readonly WNDPROC WindowProc = (hwnd, msg, wParam, lParam) => PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    private readonly Func<uint, PixelRect?> _boundsOf;
    private readonly Dictionary<uint, (HWND Window, float Alpha)> _shades = [];
    private bool _classRegistered;

    public ShadeHost(Func<uint, PixelRect?> boundsOf) => _boundsOf = boundsOf;

    public bool Update(uint displayId) => OnUi(() =>
    {
        var shade = GetOrCreate(displayId);
        if (shade is null || _boundsOf(displayId) is not { } bounds)
        {
            return false;
        }

        PInvoke.SetWindowPos(shade.Value, Topmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        return true;
    });

    public float? GetAlpha(uint displayId) => OnUi<float?>(() =>
    {
        if (GetOrCreate(displayId) is null)
        {
            return null;
        }

        return _shades[displayId].Alpha;
    });

    public bool SetAlpha(uint displayId, float alpha) => OnUi(() =>
    {
        if (GetOrCreate(displayId) is not { } window)
        {
            return false;
        }

        var clamped = Math.Clamp(alpha, 0, 1);
        _shades[displayId] = (window, clamped);
        PInvoke.SetLayeredWindowAttributes(window, new COLORREF(0), (byte)MathF.Round(clamped * 255), LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        // Keep the shade above windows that became topmost after it.
        PInvoke.SetWindowPos(window, Topmost, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        return true;
    });

    public bool Destroy(uint displayId) => OnUi(() =>
    {
        if (!_shades.Remove(displayId, out var shade))
        {
            return false;
        }

        PInvoke.DestroyWindow(shade.Window);
        Core.Log.Info($"Destroyed shade for display {displayId}");
        return true;
    });

    public bool DestroyAll() => OnUi(() =>
    {
        var any = false;
        foreach (var id in _shades.Keys.ToList())
        {
            any |= Destroy(id);
        }

        return any;
    });

    public void Dispose() => DestroyAll();

    private static T OnUi<T>(Func<T> function)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess() ? function() : dispatcher.Invoke(function);
    }

    private HWND? GetOrCreate(uint displayId)
    {
        if (_shades.TryGetValue(displayId, out var existing))
        {
            return existing.Window;
        }

        if (_boundsOf(displayId) is not { } bounds)
        {
            return null;
        }

        var instance = PInvoke.GetModuleHandle((string?)null);
        if (!_classRegistered)
        {
            fixed (char* className = ClassName)
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = WindowProc,
                    hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                    hbrBackground = new HBRUSH(PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.BLACK_BRUSH).Value),
                    lpszClassName = className,
                };
                if (PInvoke.RegisterClassEx(windowClass) == 0)
                {
                    Core.Log.Error($"Unable to register the shade window class ({Marshal.GetLastPInvokeError()})");
                    return null;
                }
            }

            _classRegistered = true;
        }

        var window = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
            ClassName,
            $"MonitorControl Shade for Display {displayId}",
            WINDOW_STYLE.WS_POPUP,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            HWND.Null,
            null,
            instance,
            null);
        if (window.IsNull)
        {
            Core.Log.Error($"Unable to create shade window ({Marshal.GetLastPInvokeError()})");
            return null;
        }

        PInvoke.SetLayeredWindowAttributes(window, new COLORREF(0), 0, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        _shades[displayId] = (window, 0);
        Core.Log.Info($"Window shade created for display {displayId}");
        return window;
    }
}
