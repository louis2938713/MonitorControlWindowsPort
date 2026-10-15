using MonitorControl.Core.Platform;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace MonitorControl.App.Platform;

/// <summary>Gamma ramp of one display through GDI (Get/SetDeviceGammaRamp).</summary>
internal sealed unsafe class GammaController(string gdiDeviceName) : IGammaController
{
    private const int Entries = GammaTable.Size;

    public GammaTable? Read()
    {
        var ramp = new ushort[Entries * 3];
        var dc = PInvoke.CreateDCW(gdiDeviceName, null, null, null);
        if (dc.IsNull)
        {
            return null;
        }

        bool ok;
        try
        {
            fixed (ushort* p = ramp)
            {
                ok = PInvoke.GetDeviceGammaRamp(dc, p);
            }
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }

        if (!ok)
        {
            return null;
        }

        var table = new GammaTable();
        for (var i = 0; i < Entries; i++)
        {
            table.Red[i] = ramp[i] / 65535f;
            table.Green[i] = ramp[Entries + i] / 65535f;
            table.Blue[i] = ramp[(2 * Entries) + i] / 65535f;
        }

        return table;
    }

    public bool Write(GammaTable table)
    {
        var ramp = new ushort[Entries * 3];
        for (var i = 0; i < Entries; i++)
        {
            ramp[i] = ToWord(table.Red[i]);
            ramp[Entries + i] = ToWord(table.Green[i]);
            ramp[(2 * Entries) + i] = ToWord(table.Blue[i]);
        }

        var dc = PInvoke.CreateDCW(gdiDeviceName, null, null, null);
        if (dc.IsNull)
        {
            return false;
        }

        try
        {
            fixed (ushort* p = ramp)
            {
                return PInvoke.SetDeviceGammaRamp(dc, p);
            }
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }
    }

    private static ushort ToWord(float value) => (ushort)Math.Clamp(MathF.Round(value * 65535f), 0, 65535);
}
