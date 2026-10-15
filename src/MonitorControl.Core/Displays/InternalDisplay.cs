using MonitorControl.Core.Enums;
using MonitorControl.Core.Platform;

namespace MonitorControl.Core.Displays;

/// <summary>
/// Built-in panel controlled natively (WMI on Windows). Equivalent of AppleDisplay.swift: its brightness
/// can also change outside the app (Fn keys, adaptive brightness, Quick Settings), which feeds brightness sync.
/// </summary>
public sealed class InternalDisplay(DisplayInfo info, DisplayManager manager, IInternalBrightness brightness) : Display(info, manager)
{
    public float GetInternalBrightness() => IsDummy ? 1 : brightness.Get() ?? 1;

    public void SetInternalBrightness(float value)
    {
        if (!IsDummy)
        {
            brightness.Set(value);
        }
    }

    public override bool SetDirectBrightness(float to, bool transient = false)
    {
        if (IsDummy)
        {
            return false;
        }

        var value = Math.Clamp(to, 0, 1);
        SetInternalBrightness(value);
        if (!transient)
        {
            SavePref(value, command: Command.Brightness);
            BrightnessSyncSourceValue = value;
            SmoothBrightnessTransient = value;
        }

        return true;
    }

    public override float GetBrightness()
    {
        if (IsDummy)
        {
            return 1;
        }

        return PrefExists(command: Command.Brightness) ? ReadPrefAsFloat(command: Command.Brightness) : GetInternalBrightness();
    }

    public override float RefreshBrightness()
    {
        if (SmoothBrightnessRunning)
        {
            return 0;
        }

        var current = GetInternalBrightness();
        var oldValue = BrightnessSyncSourceValue;
        SavePref(current, command: Command.Brightness);
        if (current == oldValue)
        {
            return 0;
        }

        Log.Info($"Pushing slider and reporting delta for built-in display {Id}");
        float newValue;
        if (Math.Abs(current - oldValue) < 0.01f)
        {
            newValue = current;
        }
        else if (current > oldValue)
        {
            newValue = oldValue + Math.Max((current - oldValue) / 3, 0.005f);
        }
        else
        {
            newValue = oldValue + Math.Min((current - oldValue) / 3, -0.005f);
        }

        BrightnessSyncSourceValue = newValue;
        if (SliderHandler.TryGetValue(Command.Brightness, out var slider))
        {
            slider.SetValue(newValue, Id);
        }

        return newValue - oldValue;
    }
}
