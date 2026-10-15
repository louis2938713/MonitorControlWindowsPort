using System.Collections.Concurrent;
using System.Management;
using MonitorControl.Core.Platform;
using CoreLog = MonitorControl.Core.Log;

namespace MonitorControl.App.Platform;

/// <summary>
/// Built-in panel brightness through WMI (root\WMI WmiMonitorBrightness*). Reads are served from a cache kept
/// up to date by brightness events; writes go through a background thread with coalescing because WMI calls are slow.
/// </summary>
internal sealed class WmiBrightness : IDisposable
{
    private readonly ConcurrentDictionary<string, int> _current = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, float> _lastSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ManagementEventWatcher? _watcher;
    private readonly Thread _writer;

    public WmiBrightness()
    {
        Refresh();
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                if (e.NewEvent["InstanceName"] is string instance && e.NewEvent["Brightness"] is byte brightness)
                {
                    _current[instance] = brightness;
                }
            };
            _watcher.Start();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            CoreLog.Info($"WMI brightness events unavailable: {ex.Message}");
            _watcher = null;
        }

        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "WMI brightness" };
        _writer.Start();
    }

    public IReadOnlyCollection<string> Instances => [.. _current.Keys];

    /// <summary>Finds the WMI instance of a display from its device path.</summary>
    public string? FindInstance(string devicePath, bool isOnlyBuiltIn)
    {
        // \\?\DISPLAY#BOE0867#4&1234&0&UID8388688#{guid} ↔ DISPLAY\BOE0867\4&1234&0&UID8388688_0
        var parts = devicePath.Split('#');
        if (parts.Length >= 3)
        {
            var prefix = $@"DISPLAY\{parts[1]}\{parts[2]}";
            var match = _current.Keys.FirstOrDefault(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return isOnlyBuiltIn && _current.Count == 1 ? _current.Keys.First() : null;
    }

    public IInternalBrightness Control(string instance) => new Instance(this, instance);

    public void Dispose()
    {
        _stop.Cancel();
        _signal.Release();
        _writer.Join(TimeSpan.FromSeconds(2));
        if (_watcher is not null)
        {
            try
            {
                _watcher.Stop();
            }
            catch (ManagementException)
            {
                // Already stopped.
            }

            _watcher.Dispose();
        }

        _signal.Dispose();
        _stop.Dispose();
    }

    private void Refresh()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, CurrentBrightness, Active FROM WmiMonitorBrightness");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                using (item)
                {
                    if (item["Active"] is true && item["InstanceName"] is string instance && item["CurrentBrightness"] is byte brightness)
                    {
                        _current[instance] = brightness;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            CoreLog.Info($"WMI brightness unavailable: {ex.Message}");
        }
    }

    private void WriteLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            _signal.Wait();
            foreach (var instance in _pending.Keys)
            {
                if (_pending.TryRemove(instance, out var value))
                {
                    WriteNow(instance, value);
                }
            }
        }
    }

    private static void WriteNow(string instance, int value)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                using (item)
                {
                    if (string.Equals(item["InstanceName"] as string, instance, StringComparison.OrdinalIgnoreCase))
                    {
                        item.InvokeMethod("WmiSetBrightness", [1u, (byte)value]);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            CoreLog.Warning("WMI brightness write failed", ex);
        }
    }

    private sealed class Instance(WmiBrightness owner, string instance) : IInternalBrightness
    {
        public float? Get()
        {
            if (!owner._current.TryGetValue(instance, out var percent))
            {
                return null;
            }

            // Hand back the exact value we set when the panel reports it, so syncing does not see rounding as a change.
            if (owner._lastSet.TryGetValue(instance, out var last) && (int)MathF.Round(last * 100) == percent)
            {
                return last;
            }

            return percent / 100f;
        }

        public void Set(float value)
        {
            var clamped = Math.Clamp(value, 0, 1);
            var percent = (int)MathF.Round(clamped * 100);
            owner._lastSet[instance] = clamped;
            owner._current[instance] = percent;
            owner._pending[instance] = percent;
            owner._signal.Release();
        }
    }
}
