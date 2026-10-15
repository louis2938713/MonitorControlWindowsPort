namespace MonitorControl.Core.Platform;

/// <summary>Rectangle in physical pixels (virtual screen coordinates).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Everything the app needs to know about a connected display, as reported by the platform.</summary>
/// <param name="Id">Stable identifier of the display on its connector (derived from the device path).</param>
/// <param name="Name">Localized friendly name.</param>
/// <param name="RawName">Name from the EDID (used for audio device name matching).</param>
/// <param name="MirrorOf">Id of the display this one mirrors (Windows "Duplicate"), 0 when not mirroring.</param>
public sealed record DisplayInfo(
    uint Id,
    string Name,
    string RawName,
    uint? VendorNumber,
    uint? ModelNumber,
    uint? SerialNumber,
    bool IsBuiltIn,
    bool IsVirtual,
    bool IsDummy,
    bool IsHdr,
    uint MirrorOf,
    string DevicePath,
    PixelRect Bounds,
    PixelRect WorkArea)
{
    public bool IsInMirrorSet { get; init; }
}
