using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MonitorControl.Core.Platform;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace MonitorControl.App.Platform;

/// <summary>A display as seen by Windows: core info plus the handles needed to control it.</summary>
internal sealed record WinDisplay(DisplayInfo Info, HMONITOR Monitor, int PhysicalIndex, string GdiDeviceName);

/// <summary>Enumerates active displays using the DisplayConfig API, monitor handles and the EDID in the registry.</summary>
internal static unsafe class DisplayEnumerator
{
    /// <summary>Generic names used when the display reports none (translated for the UI only).</summary>
    public const string BuiltInDisplayName = "Built-in Display";

    public const string UnknownDisplayName = "Unknown";

    private const uint ErrorInsufficientBuffer = 122;

    public static List<WinDisplay> Enumerate()
    {
        var paths = QueryActivePaths();
        var monitors = EnumerateMonitors();
        var result = new List<WinDisplay>();
        var sourceGroups = new Dictionary<(uint, int, uint), List<uint>>();

        foreach (var path in paths)
        {
            var target = GetTargetName(path);
            var gdiName = GetSourceGdiName(path);
            if (target is null || gdiName is null || !monitors.TryGetValue(gdiName, out var monitor))
            {
                continue;
            }

            var devicePath = target.Value.monitorDevicePath.ToString();
            var edid = Edid.ReadFromRegistry(devicePath);
            var friendlyName = target.Value.monitorFriendlyDeviceName.ToString();
            var technology = target.Value.outputTechnology;
            var isBuiltIn = IsBuiltIn(technology);
            var name = !string.IsNullOrWhiteSpace(friendlyName) ? friendlyName
                : !string.IsNullOrWhiteSpace(edid?.Name) ? edid!.Name!
                : isBuiltIn ? BuiltInDisplayName : UnknownDisplayName;
            var rawName = !string.IsNullOrWhiteSpace(edid?.Name) ? edid!.Name! : name;
            var id = StableId(devicePath.Length > 0 ? devicePath : $"{gdiName}#{path.targetInfo.id}");
            var isVirtual = IsVirtual(technology);
            var vendor = edid?.VendorNumber ?? (target.Value.edidManufactureId != 0 ? target.Value.edidManufactureId : null);

            var sourceKey = (path.sourceInfo.adapterId.LowPart, path.sourceInfo.adapterId.HighPart, path.sourceInfo.id);
            if (!sourceGroups.TryGetValue(sourceKey, out var group))
            {
                group = [];
                sourceGroups[sourceKey] = group;
            }

            var physicalIndex = group.Count;
            group.Add(id);

            var info = new DisplayInfo(
                Id: id,
                Name: name,
                RawName: rawName,
                VendorNumber: vendor,
                ModelNumber: edid?.ModelNumber ?? (target.Value.edidProductCodeId != 0 ? target.Value.edidProductCodeId : null),
                SerialNumber: edid?.SerialNumber,
                IsBuiltIn: isBuiltIn,
                IsVirtual: isVirtual,
                IsDummy: rawName.Contains("dummy", StringComparison.OrdinalIgnoreCase) || (isVirtual && vendor == 0xF0F0),
                IsHdr: IsHdrEnabled(path),
                MirrorOf: 0,
                DevicePath: devicePath,
                Bounds: monitor.Bounds,
                WorkArea: monitor.WorkArea);
            result.Add(new WinDisplay(info, monitor.Handle, physicalIndex, gdiName));
        }

        // Windows "Duplicate": several targets share one source. The first one acts as the main display.
        for (var i = 0; i < result.Count; i++)
        {
            var display = result[i];
            var group = sourceGroups.Values.First(g => g.Contains(display.Info.Id));
            if (group.Count > 1)
            {
                var main = group[0];
                result[i] = display with
                {
                    Info = display.Info with { MirrorOf = main == display.Info.Id ? 0 : main, IsInMirrorSet = true },
                };
            }
        }

        return result;
    }

    /// <summary>FNV-1a hash of the device path: stable across reboots for a given monitor on a given connector.</summary>
    internal static uint StableId(string key)
    {
        var hash = 2166136261u;
        foreach (var c in key.ToUpperInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash == 0 ? 1 : hash;
    }

    private static bool IsBuiltIn(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY technology) => technology is
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL or
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED or
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED or
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS;

    private static bool IsVirtual(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY technology) => technology is
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST or
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED or
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL or
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER;

    private static DISPLAYCONFIG_PATH_INFO[] QueryActivePaths()
    {
        while (true)
        {
            if (PInvoke.GetDisplayConfigBufferSizes(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != WIN32_ERROR.ERROR_SUCCESS)
            {
                return [];
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            WIN32_ERROR status;
            fixed (DISPLAYCONFIG_PATH_INFO* pathPtr = paths)
            fixed (DISPLAYCONFIG_MODE_INFO* modePtr = modes)
            {
                status = PInvoke.QueryDisplayConfig(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, &pathCount, pathPtr, &modeCount, modePtr, null);
            }

            if ((uint)status == ErrorInsufficientBuffer)
            {
                continue;
            }

            return status == WIN32_ERROR.ERROR_SUCCESS ? paths[..(int)pathCount] : [];
        }
    }

    private static DISPLAYCONFIG_TARGET_DEVICE_NAME? GetTargetName(DISPLAYCONFIG_PATH_INFO path)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        request.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        request.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
        request.header.adapterId = path.targetInfo.adapterId;
        request.header.id = path.targetInfo.id;
        return PInvoke.DisplayConfigGetDeviceInfo(&request.header) == 0 ? request : null;
    }

    private static string? GetSourceGdiName(DISPLAYCONFIG_PATH_INFO path)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        request.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        request.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
        request.header.adapterId = path.sourceInfo.adapterId;
        request.header.id = path.sourceInfo.id;
        return PInvoke.DisplayConfigGetDeviceInfo(&request.header) == 0 ? request.viewGdiDeviceName.ToString() : null;
    }

    private static bool IsHdrEnabled(DISPLAYCONFIG_PATH_INFO path)
    {
        var request = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        request.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
        request.header.size = (uint)sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO);
        request.header.adapterId = path.targetInfo.adapterId;
        request.header.id = path.targetInfo.id;
        if (PInvoke.DisplayConfigGetDeviceInfo(&request.header) != 0)
        {
            return false;
        }

        // Bit 1: advancedColorEnabled.
        return (request.Anonymous.value & 0x2) != 0;
    }

    private readonly record struct MonitorEntry(HMONITOR Handle, PixelRect Bounds, PixelRect WorkArea);

    private static Dictionary<string, MonitorEntry> EnumerateMonitors()
    {
        var handles = new List<HMONITOR>();
        var callback = new MONITORENUMPROC((monitor, _, _, _) =>
        {
            handles.Add(monitor);
            return true;
        });
        PInvoke.EnumDisplayMonitors(HDC.Null, null, callback, default);
        GC.KeepAlive(callback);

        var result = new Dictionary<string, MonitorEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in handles)
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (PInvoke.GetMonitorInfo(handle, (MONITORINFO*)&info))
            {
                result[info.szDevice.ToString()] = new MonitorEntry(handle, ToRect(info.monitorInfo.rcMonitor), ToRect(info.monitorInfo.rcWork));
            }
        }

        return result;
    }

    internal static PixelRect ToRect(RECT rect) => new(rect.left, rect.top, rect.right, rect.bottom);
}

/// <summary>The few EDID fields MonitorControl uses.</summary>
internal sealed record Edid(uint VendorNumber, uint ModelNumber, uint? SerialNumber, string? Name)
{
    /// <summary>Reads the EDID stored by Windows under HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY (readable without elevation).</summary>
    public static Edid? ReadFromRegistry(string devicePath)
    {
        // \\?\DISPLAY#DEL4123#5&2a3b&0&UID4353#{e6f07b5f-...} → DISPLAY\DEL4123\5&2a3b&0&UID4353
        var parts = devicePath.Split('#');
        if (parts.Length < 3)
        {
            return null;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
            return key?.GetValue("EDID") is byte[] bytes ? Parse(bytes) : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public static Edid? Parse(ReadOnlySpan<byte> edid)
    {
        if (edid.Length < 128)
        {
            return null;
        }

        var vendor = (uint)((edid[8] << 8) | edid[9]);
        var model = (uint)(edid[10] | (edid[11] << 8));
        var serial = BitConverter.ToUInt32(edid.Slice(12, 4));
        string? name = null;
        for (var offset = 54; offset <= 108; offset += 18)
        {
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xFC)
            {
                name = System.Text.Encoding.ASCII.GetString(edid.Slice(offset + 5, 13)).Split('\n')[0].Trim();
            }
        }

        return new Edid(vendor, model, serial == 0 ? null : serial, name);
    }
}
