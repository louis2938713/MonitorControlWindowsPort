using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;

namespace MonitorControl.Core.Input;

/// <summary>
/// Custom keyboard shortcuts with press-and-hold repeat. Port of KeyboardShortcutsManager.swift.
/// The keyboard hook reports key down/up; repeats are generated here at the system key repeat rate.
/// </summary>
public sealed class ShortcutManager(DisplayManager displayManager, IUiDispatcher ui, IKeyboardShell shell, Func<(TimeSpan InitialDelay, TimeSpan Interval)> repeatRate)
{
    private const int MaxRepeatCount = 100;
    private static readonly ShortcutName[] BrightnessShortcuts = [ShortcutName.BrightnessUp, ShortcutName.BrightnessDown, ShortcutName.ContrastUp, ShortcutName.ContrastDown];
    private static readonly ShortcutName[] VolumeShortcuts = [ShortcutName.VolumeUp, ShortcutName.VolumeDown, ShortcutName.Mute];

    private ShortcutName? _currentCommand;
    private bool _isFirstKeypress;
    private bool _isHold;
    private bool _isSmallIncrement;
    private int _currentEventId;
    private int _keyRepeatCount;
    private TimeSpan _initialKeyRepeat;
    private TimeSpan _keyRepeat;

    private Prefs Prefs => displayManager.Prefs;

    private AppState State => displayManager.State;

    public bool IsEnabled(ShortcutName shortcut) =>
        BrightnessShortcuts.Contains(shortcut) ? Prefs.CustomBrightnessKeysEnabled
        : VolumeShortcuts.Contains(shortcut) && Prefs.CustomVolumeKeysEnabled;

    /// <summary>Key down of a custom shortcut. <paramref name="fine"/> = Win was held in addition.</summary>
    public void KeyDown(ShortcutName shortcut, bool fine)
    {
        if (shortcut == ShortcutName.Mute)
        {
            Mute();
            return;
        }

        Engage(shortcut, fine);
    }

    public void KeyUp(ShortcutName shortcut)
    {
        if (shortcut != ShortcutName.Mute)
        {
            Disengage();
        }
    }

    public void Disengage()
    {
        _isHold = false;
        _isFirstKeypress = false;
        _currentCommand = null;
        _keyRepeatCount = 0;
    }

    private void Engage(ShortcutName shortcut, bool fine)
    {
        if (!IsEnabled(shortcut))
        {
            return;
        }

        (_initialKeyRepeat, _keyRepeat) = repeatRate();
        _currentCommand = shortcut;
        _isFirstKeypress = true;
        _isHold = true;
        _isSmallIncrement = fine;
        _currentEventId++;
        _keyRepeatCount = 0;
        Apply(shortcut, _currentEventId);
    }

    private void Apply(ShortcutName shortcut, int eventId)
    {
        if (!State.IsActive || _keyRepeatCount > MaxRepeatCount)
        {
            Disengage();
            return;
        }

        if (_currentCommand != shortcut || !_isHold || eventId != _currentEventId)
        {
            if (shortcut is ShortcutName.VolumeUp or ShortcutName.VolumeDown)
            {
                Volume(isUp: true, isPressed: false);
            }

            return;
        }

        var delay = _isFirstKeypress ? _initialKeyRepeat : _keyRepeat;
        _isFirstKeypress = false;
        ui.PostDelayed(delay, () => Apply(shortcut, eventId));
        _keyRepeatCount++;
        switch (shortcut)
        {
            case ShortcutName.BrightnessUp: Brightness(isUp: true); break;
            case ShortcutName.BrightnessDown: Brightness(isUp: false); break;
            case ShortcutName.ContrastUp: Contrast(isUp: true); break;
            case ShortcutName.ContrastDown: Contrast(isUp: false); break;
            case ShortcutName.VolumeUp: Volume(isUp: true, isPressed: true); break;
            case ShortcutName.VolumeDown: Volume(isUp: false, isPressed: true); break;
            case ShortcutName.Mute:
            default: break;
        }
    }

    private bool Fine(PrefKey fineScalePref) => Prefs.Bool(fineScalePref) != _isSmallIncrement;

    private void Brightness(bool isUp)
    {
        if (!Prefs.CustomBrightnessKeysEnabled)
        {
            Disengage();
            return;
        }

        var affected = displayManager.GetAffectedDisplays(isBrightness: true);
        foreach (var display in affected.Where(d => !d.ReadPrefAsBool(PrefKey.IsDisabled)))
        {
            static bool InSwAfterBrightness(Display d) => d is ExternalDisplay e && e.IsSwBrightnessNotDefault() && !e.IsSw();
            var anyInSwAfterBrightness = Prefs.Bool(PrefKey.SeparateCombinedScale) && affected.Any(InSwAfterBrightness);
            if (!(anyInSwAfterBrightness && !InSwAfterBrightness(display)))
            {
                display.StepBrightness(isUp, Fine(PrefKey.UseFineScaleBrightness));
            }
        }
    }

    private void Contrast(bool isUp)
    {
        if (!Prefs.CustomBrightnessKeysEnabled)
        {
            Disengage();
            return;
        }

        foreach (var display in displayManager.GetAffectedDisplays(isBrightness: true).OfType<ExternalDisplay>().Where(d => !d.ReadPrefAsBool(PrefKey.IsDisabled)))
        {
            display.StepContrast(isUp, Fine(PrefKey.UseFineScaleBrightness));
        }
    }

    private void Volume(bool isUp, bool isPressed)
    {
        if (!Prefs.CustomVolumeKeysEnabled)
        {
            Disengage();
            return;
        }

        var soundPlayed = false;
        foreach (var display in displayManager.GetAffectedDisplays(isVolume: true).OfType<ExternalDisplay>().Where(d => !d.ReadPrefAsBool(PrefKey.IsDisabled)))
        {
            if (isPressed)
            {
                display.StepVolume(isUp, Fine(PrefKey.UseFineScaleVolume));
            }
            else if (!soundPlayed && !display.ReadPrefAsBool(PrefKey.UnavailableDdc, Command.AudioSpeakerVolume))
            {
                shell.PlayVolumeChangedSound();
                soundPlayed = true;
            }
        }
    }

    private void Mute()
    {
        if (!State.IsActive || !Prefs.CustomVolumeKeysEnabled)
        {
            return;
        }

        var soundPlayed = false;
        foreach (var display in displayManager.GetAffectedDisplays(isVolume: true).OfType<ExternalDisplay>().Where(d => !d.ReadPrefAsBool(PrefKey.IsDisabled)))
        {
            display.ToggleMute();
            if (!soundPlayed && display.ReadPrefAsInt(command: Command.AudioMuteScreenBlank) != 1 && !display.ReadPrefAsBool(PrefKey.UnavailableDdc, Command.AudioSpeakerVolume))
            {
                shell.PlayVolumeChangedSound();
                soundPlayed = true;
            }
        }
    }
}
