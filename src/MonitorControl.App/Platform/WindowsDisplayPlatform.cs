using MonitorControl.Core.Platform;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Graphics.Gdi;

namespace MonitorControl.App.Platform;

/// <summary>Windows implementation of the display services used by the core logic.</summary>
internal sealed unsafe class WindowsDisplayPlatform : IDisplayPlatform, IDisposable
{
    private static readonly TimeSpan NegativeProbeLifetime = TimeSpan.FromSeconds(60);
    private readonly object _lock = new();
    private readonly List<DdcChannel> _channels = [];
    private readonly Dictionary<string, (bool Capable, DateTime At)> _probeCache = new(StringComparer.OrdinalIgnoreCase);
    private volatile List<WinDisplay> _displays = [];
    private WmiBrightness? _wmi;

    public IReadOnlyList<WinDisplay> Current => _displays;

    public IReadOnlyList<DisplayInfo> EnumerateDisplays()
    {
        lock (_lock)
        {
            // Physical monitor handles are invalidated by display changes: reopen them on every configuration.
            foreach (var channel in _channels)
            {
                channel.Dispose();
            }

            _channels.Clear();
        }

        _displays = DisplayEnumerator.Enumerate();
        return _displays.Select(d => d.Info).ToList();
    }

    public PixelRect? BoundsOf(uint displayId) => Find(displayId)?.Info.Bounds;

    public DisplayInfo? InfoOf(uint displayId) => Find(displayId)?.Info;

    public IDdcChannel? OpenDdc(uint displayId, bool probe)
    {
        var display = Find(displayId);
        if (display is null || display.Info.IsVirtual || display.Info.IsBuiltIn)
        {
            return null;
        }

        var channel = DdcChannel.Open(display.Monitor, display.PhysicalIndex);
        if (channel is null)
        {
            return null;
        }

        if (probe && !IsDdcCapable(display, channel))
        {
            channel.Dispose();
            return null;
        }

        lock (_lock)
        {
            _channels.Add(channel);
        }

        return channel;
    }

    public IInternalBrightness? OpenInternalBrightness(uint displayId)
    {
        var display = Find(displayId);
        if (display is null || !display.Info.IsBuiltIn)
        {
            return null;
        }

        _wmi ??= new WmiBrightness();
        var onlyBuiltIn = _displays.Count(d => d.Info.IsBuiltIn) == 1;
        var instance = _wmi.FindInstance(display.Info.DevicePath, onlyBuiltIn);
        return instance is null ? null : _wmi.Control(instance);
    }

    public IGammaController? OpenGamma(uint displayId)
    {
        var display = Find(displayId);
        return display is null || display.Info.IsVirtual ? null : new GammaController(display.GdiDeviceName);
    }

    public uint? DisplayUnderCursor()
    {
        if (!PInvoke.GetCursorPos(out var point))
        {
            return null;
        }

        var candidates = _displays.Where(d => d.Info.Bounds.Contains(point.X, point.Y)).ToList();
        return (candidates.FirstOrDefault(d => d.Info.MirrorOf == 0) ?? candidates.FirstOrDefault())?.Info.Id;
    }

    public uint? DisplayWithFocus()
    {
        var window = PInvoke.GetForegroundWindow();
        if (window.IsNull)
        {
            return DisplayUnderCursor();
        }

        var monitor = PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return _displays.FirstOrDefault(d => d.Monitor == monitor && d.Info.MirrorOf == 0)?.Info.Id;
    }

    public bool ToggleMirror()
    {
        var displays = _displays;
        if (displays.Count < 2)
        {
            return false;
        }

        var mirrored = displays.Any(d => d.Info.IsInMirrorSet);
        var flags = SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY | (mirrored ? SET_DISPLAY_CONFIG_FLAGS.SDC_TOPOLOGY_EXTEND : SET_DISPLAY_CONFIG_FLAGS.SDC_TOPOLOGY_CLONE);
        var result = PInvoke.SetDisplayConfig(0, null, 0, null, flags);
        Core.Log.Info($"Display mirroring {(mirrored ? "disabled" : "enabled")} (result {result})");
        return result == 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var channel in _channels)
            {
                channel.Dispose();
            }

            _channels.Clear();
        }

        _wmi?.Dispose();
    }

    private WinDisplay? Find(uint displayId) => _displays.FirstOrDefault(d => d.Info.Id == displayId);

    private bool IsDdcCapable(WinDisplay display, DdcChannel channel)
    {
        var key = display.Info.DevicePath;
        lock (_lock)
        {
            if (_probeCache.TryGetValue(key, out var cached) && (cached.Capable || DateTime.UtcNow - cached.At < NegativeProbeLifetime))
            {
                return cached.Capable;
            }
        }

        var capable = channel.Probe();
        Core.Log.Info($"DDC probe for {display.Info.Name}: {(capable ? "capable" : "not capable")}");
        lock (_lock)
        {
            _probeCache[key] = (capable, DateTime.UtcNow);
        }

        return capable;
    }
}
