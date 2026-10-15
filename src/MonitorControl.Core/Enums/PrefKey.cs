namespace MonitorControl.Core.Enums;

/// <summary>Settings keys. Ported from PrefKey.swift (plus a few Windows-only keys at the end).</summary>
public enum PrefKey
{
    /* -- App-wide settings -- */

    /// <summary>Automatic update checks.</summary>
    EnableAutomaticUpdateChecks,

    /// <summary>Receive beta updates.</summary>
    IsBetaChannel,

    /// <summary>Build number of the version that last wrote the settings.</summary>
    BuildNumber,

    /// <summary>Was the app launched once.</summary>
    AppAlreadyLaunched,

    /// <summary>Notification area icon visibility (<see cref="MenuIcon"/>).</summary>
    MenuIcon,

    /// <summary>Flyout bottom row style (<see cref="MenuItemStyle"/>).</summary>
    MenuItemStyle,

    /// <summary>Brightness keys listened for (<see cref="KeyboardBrightness"/>).</summary>
    KeyboardBrightness,

    /// <summary>Volume keys listened for (<see cref="KeyboardVolume"/>).</summary>
    KeyboardVolume,

    /// <summary>Don't listen to the alternative brightness keys (Scroll Lock / Pause, F14/F15 on Apple keyboards).</summary>
    DisableAltBrightnessKeys,

    /// <summary>Hide brightness sliders.</summary>
    HideBrightness,

    /// <summary>Show contrast sliders.</summary>
    ShowContrast,

    /// <summary>Hide volume sliders.</summary>
    HideVolume,

    /// <summary>Do not lower via software after hardware brightness reached zero.</summary>
    DisableCombinedBrightness,

    /// <summary>Use separated OSD scale for combined brightness.</summary>
    SeparateCombinedScale,

    /// <summary>Do not show sliders for built-in displays in the flyout.</summary>
    HideBuiltInFromMenu,

    /// <summary>Slider snapping.</summary>
    EnableSliderSnap,

    /// <summary>Show percentage next to sliders.</summary>
    EnableSliderPercent,

    /// <summary>Show tick marks on sliders.</summary>
    ShowTickMarks,

    /// <summary>What to do upon startup or wake (<see cref="StartupAction"/>).</summary>
    StartupAction,

    /// <summary>Show advanced options under the Displays settings page.</summary>
    ShowAdvancedSettings,

    /// <summary>Allow zero software brightness.</summary>
    AllowZeroSwBrightness,

    /// <summary>Keyboard brightness control for multiple displays (<see cref="MultiKeyboardBrightness"/>).</summary>
    MultiKeyboardBrightness,

    /// <summary>Keyboard volume control for multiple devices (<see cref="MultiKeyboardVolume"/>).</summary>
    MultiKeyboardVolume,

    /// <summary>Use fine OSD scale for brightness.</summary>
    UseFineScaleBrightness,

    /// <summary>Use fine OSD scale for volume.</summary>
    UseFineScaleVolume,

    /// <summary>Disable smooth brightness transitions.</summary>
    DisableSmoothBrightness,

    /// <summary>Synchronize brightness from built-in displays among all other displays.</summary>
    EnableBrightnessSync,

    /// <summary>Sliders for multiple displays (<see cref="MultiSliders"/>).</summary>
    MultiSliders,

    /* -- Display specific settings -- */

    /// <summary>Enable mute DDC for display.</summary>
    EnableMuteUnmute,

    /// <summary>Hide volume OSD for display.</summary>
    HideOsd,

    /// <summary>Longer delay during DDC reads for display.</summary>
    LongerDelay,

    /// <summary>DDC polling mode for display (<see cref="PollingMode"/>).</summary>
    PollingMode,

    /// <summary>DDC polling count for display.</summary>
    PollingCount,

    /// <summary>Display should avoid gamma table manipulation and use a shade instead.</summary>
    AvoidGamma,

    /// <summary>User assigned audio device name for display.</summary>
    AudioDeviceNameOverride,

    /// <summary>Display disabled for keyboard control.</summary>
    IsDisabled,

    /// <summary>Force software mode for display.</summary>
    ForceSw,

    /// <summary>Software brightness for display.</summary>
    SwBrightness,

    /// <summary>Combined brightness switching point.</summary>
    CombinedBrightnessSwitchingPoint,

    /// <summary>Friendly name.</summary>
    FriendlyName,

    /* -- Display + command specific settings -- */

    /// <summary>Command value for display.</summary>
    Value,

    /// <summary>Was the setting ever changed by the user.</summary>
    IsTouched,

    /// <summary>Min DDC value override.</summary>
    MinDdcOverride,

    /// <summary>Max DDC value (detected).</summary>
    MaxDdc,

    /// <summary>Max DDC value user override.</summary>
    MaxDdcOverride,

    /// <summary>Scale mapping curve.</summary>
    CurveDdc,

    /// <summary>The control is set as unavailable for the display.</summary>
    UnavailableDdc,

    /// <summary>Invert DDC scale.</summary>
    InvertDdc,

    /// <summary>Override DDC control code(s).</summary>
    RemapDdc,

    /* -- Windows only -- */

    /// <summary>UI language ("" = follow Windows).</summary>
    Language,

    /// <summary>Play a sound when the volume is changed with the keyboard.</summary>
    PlayVolumeFeedback,
}
