using MonitorControl.Core.Enums;

namespace MonitorControl.Core.Platform;

/// <summary>DDC/CI channel to one physical display. Calls are blocking and only made from the DDC queue.</summary>
public interface IDdcChannel
{
    bool Write(byte code, ushort value);

    /// <summary>Single read attempt. Returns null on failure.</summary>
    (ushort Current, ushort Max)? Read(byte code);
}

/// <summary>Native brightness control of a built-in panel (equivalent of Apple DisplayServices).</summary>
public interface IInternalBrightness
{
    /// <summary>Current brightness (0..1) or null when unavailable.</summary>
    float? Get();

    void Set(float value);
}

/// <summary>256-entry gamma table with 0..1 values.</summary>
public sealed class GammaTable
{
    public const int Size = 256;

    public GammaTable()
    {
    }

    public GammaTable(float[] red, float[] green, float[] blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    public float[] Red { get; } = new float[Size];

    public float[] Green { get; } = new float[Size];

    public float[] Blue { get; } = new float[Size];

    public float Peak => Math.Max(Red.Max(), Math.Max(Green.Max(), Blue.Max()));

    public static GammaTable Identity()
    {
        var table = new GammaTable();
        for (var i = 0; i < Size; i++)
        {
            table.Red[i] = table.Green[i] = table.Blue[i] = i / (float)(Size - 1);
        }

        return table;
    }

    public GammaTable Scaled(float factor)
    {
        var table = new GammaTable();
        for (var i = 0; i < Size; i++)
        {
            table.Red[i] = Red[i] * factor;
            table.Green[i] = Green[i] * factor;
            table.Blue[i] = Blue[i] * factor;
        }

        return table;
    }
}

/// <summary>Gamma ramp access for one display.</summary>
public interface IGammaController
{
    GammaTable? Read();

    /// <summary>Applies a ramp. Returns false if Windows refused it (out of the allowed range, HDR…).</summary>
    bool Write(GammaTable table);
}

/// <summary>Black click-through overlays used to dim displays without gamma control.</summary>
public interface IShadeHost
{
    /// <summary>Creates the shade if needed and fits it to the display bounds.</summary>
    bool Update(uint displayId);

    float? GetAlpha(uint displayId);

    bool SetAlpha(uint displayId, float alpha);

    bool Destroy(uint displayId);

    bool DestroyAll();
}

/// <summary>Shows the brightness/volume OSD.</summary>
public interface IOsd
{
    void Show(uint displayId, Command command, float value, float maxValue = 1, bool roundChiclet = false, bool locked = false);

    void ShowVolumeDisabled(uint displayId);

    void ShowMuteDisabled(uint displayId);

    void PopEmpty(uint displayId, Command command);
}

/// <summary>UI-thread scheduling (equivalent of DispatchQueue.main).</summary>
public interface IUiDispatcher
{
    void Post(Action action);

    void PostDelayed(TimeSpan delay, Action action);
}

/// <summary>Connected displays and display-related system services.</summary>
public interface IDisplayPlatform
{
    IReadOnlyList<DisplayInfo> EnumerateDisplays();

    /// <summary>Opens a DDC channel. When <paramref name="probe"/> is true the display must answer a read to be considered DDC capable.</summary>
    IDdcChannel? OpenDdc(uint displayId, bool probe);

    IInternalBrightness? OpenInternalBrightness(uint displayId);

    IGammaController? OpenGamma(uint displayId);

    uint? DisplayUnderCursor();

    uint? DisplayWithFocus();

    /// <summary>Toggles Windows display mode between Duplicate and Extend.</summary>
    bool ToggleMirror();
}

/// <summary>A slider shown in the flyout for one or more displays.</summary>
public interface ISliderHandle
{
    void SetValue(float value, uint displayId = 0);
}

/// <summary>User-facing prompts raised by the core logic.</summary>
public interface IAlerts
{
    /// <summary>Gamma interference (f.lux, Night Light…). Returns true to disable gamma control for all displays.</summary>
    bool AskDisableGammaAfterInterference();

    void ShowIncompatiblePreviousVersion();

    void ShowSafeMode();
}

/// <summary>Default audio output device.</summary>
public interface IAudioService
{
    event EventHandler? DefaultOutputChanged;

    /// <summary>Name of the default output device, or null.</summary>
    string? DefaultOutputName { get; }

    /// <summary>The default output is a display audio device (HDMI/DisplayPort), which has no hardware volume of its own.</summary>
    bool DefaultOutputIsDisplay { get; }
}
