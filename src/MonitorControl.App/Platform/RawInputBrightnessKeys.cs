using System.Runtime.InteropServices;
using MonitorControl.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;

namespace MonitorControl.App.Platform;

/// <summary>
/// Brightness keys of external keyboards (HID consumer usages 0x6F "Brightness Increment" and 0x70 "Brightness
/// Decrement"). They are not regular keys on Windows, so they are read through Raw Input. They cannot be swallowed.
/// </summary>
internal sealed unsafe class RawInputBrightnessKeys : IDisposable
{
    private const ushort ConsumerPage = 0x0C;
    private const ushort ConsumerControl = 0x01;
    private const ushort BrightnessIncrement = 0x6F;
    private const ushort BrightnessDecrement = 0x70;
    private const uint RimTypeHid = 2;
    private readonly MessageWindow _window;
    private readonly Action<MediaKey> _onPressed;
    private ushort _pressed;

    public RawInputBrightnessKeys(MessageWindow window, Action<MediaKey> onPressed)
    {
        _window = window;
        _onPressed = onPressed;
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = ConsumerPage,
            usUsage = ConsumerControl,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
            hwndTarget = new HWND(window.Handle),
        };
        if (!PInvoke.RegisterRawInputDevices([device], (uint)sizeof(RAWINPUTDEVICE)))
        {
            Core.Log.Warning($"Raw input registration failed ({Marshal.GetLastPInvokeError()})");
        }

        _window.MessageReceived += OnMessage;
    }

    public void Dispose() => _window.MessageReceived -= OnMessage;

    private void OnMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message != (int)PInvoke.WM_INPUT)
        {
            return;
        }

        uint size = 0;
        var headerSize = (uint)sizeof(RAWINPUTHEADER);
        var handle = new HRAWINPUT(lParam);
        if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, null, &size, headerSize) != 0 || size == 0 || size > 4096)
        {
            return;
        }

        var buffer = new byte[size];
        fixed (byte* data = buffer)
        {
            if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, data, &size, headerSize) != size)
            {
                return;
            }
        }

        var span = buffer.AsSpan();
        if (BitConverter.ToUInt32(span) != RimTypeHid || span.Length < headerSize + 8)
        {
            return;
        }

        var reportSize = (int)BitConverter.ToUInt32(span[(int)headerSize..]);
        var reportCount = (int)BitConverter.ToUInt32(span[((int)headerSize + 4)..]);
        var reports = span[((int)headerSize + 8)..];
        for (var i = 0; i < reportCount && (i + 1) * reportSize <= reports.Length; i++)
        {
            Process(reports.Slice(i * reportSize, reportSize));
        }
    }

    private void Process(ReadOnlySpan<byte> report)
    {
        // Byte 0 is the report id; usages follow as 16-bit little endian values.
        ushort usage = 0;
        for (var i = 1; i + 1 < report.Length; i += 2)
        {
            var value = (ushort)(report[i] | (report[i + 1] << 8));
            if (value is BrightnessIncrement or BrightnessDecrement)
            {
                usage = value;
                break;
            }
        }

        if (usage != 0 && usage != _pressed)
        {
            _onPressed(usage == BrightnessIncrement ? MediaKey.BrightnessUp : MediaKey.BrightnessDown);
        }

        _pressed = usage;
    }
}
