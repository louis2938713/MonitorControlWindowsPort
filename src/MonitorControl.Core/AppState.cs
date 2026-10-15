namespace MonitorControl.Core;

/// <summary>Global runtime state shared by the core logic (the app/AppDelegate fields of the macOS app).</summary>
public sealed class AppState
{
    /// <summary>Non-zero while a display reconfiguration is pending (dispatched reconfigure command id).</summary>
    public int ReconfigureId { get; set; }

    /// <summary>Non-zero while the system or the displays are asleep.</summary>
    public int SleepId { get; set; }

    /// <summary>Shift was held at launch: defaults reloaded, DDC reads blocked.</summary>
    public bool SafeMode { get; set; }

    /// <summary>True when nothing blocks display control (not asleep, not reconfiguring).</summary>
    public bool IsActive => SleepId == 0 && ReconfigureId == 0;
}
