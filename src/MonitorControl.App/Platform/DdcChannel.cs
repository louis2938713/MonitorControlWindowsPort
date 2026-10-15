using MonitorControl.Core.Platform;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace MonitorControl.App.Platform;

/// <summary>
/// DDC/CI through the Windows Monitor Configuration API (dxva2). Windows handles the I²C framing,
/// checksums and the mandatory delays, which replaces both IntelDDC and Arm64DDC of the macOS app.
/// </summary>
internal sealed unsafe class DdcChannel : IDdcChannel, IDisposable
{
    private readonly HANDLE _handle;
    private bool _disposed;

    private DdcChannel(HANDLE handle) => _handle = handle;

    /// <summary>Gets the physical monitor at <paramref name="index"/> behind a monitor handle.</summary>
    public static DdcChannel? Open(HMONITOR monitor, int index)
    {
        if (!PInvoke.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
        {
            return null;
        }

        var physical = new PHYSICAL_MONITOR[count];
        if (!PInvoke.GetPhysicalMonitorsFromHMONITOR(monitor, physical))
        {
            return null;
        }

        DdcChannel? channel = null;
        for (var i = 0; i < physical.Length; i++)
        {
            if (i == Math.Min(index, physical.Length - 1))
            {
                channel = new DdcChannel(physical[i].hPhysicalMonitor);
            }
            else
            {
                PInvoke.DestroyPhysicalMonitor(physical[i].hPhysicalMonitor);
            }
        }

        return channel;
    }

    public bool Write(byte code, ushort value)
    {
        if (_disposed)
        {
            return false;
        }

        return PInvoke.SetVCPFeature(_handle, code, value) != 0;
    }

    public (ushort Current, ushort Max)? Read(byte code)
    {
        if (_disposed)
        {
            return null;
        }

        uint current = 0, max = 0;
        if (PInvoke.GetVCPFeatureAndVCPFeatureReply(_handle, code, null, &current, &max) == 0)
        {
            return null;
        }

        return ((ushort)Math.Min(current, ushort.MaxValue), (ushort)Math.Min(max, ushort.MaxValue));
    }

    /// <summary>True when the display answers DDC/CI: a brightness read, or at least a capabilities request.</summary>
    public bool Probe()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (Read(0x10) is not null)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        uint length = 0;
        return PInvoke.GetCapabilitiesStringLength(_handle, &length) != 0 && length > 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            PInvoke.DestroyPhysicalMonitor(_handle);
        }
    }
}
