using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;

namespace MonitorControl.Core.Input;

/// <summary>UI services needed by keyboard handling.</summary>
public interface IKeyboardShell
{
    /// <summary>Opens the Windows display or sound settings (Alt + standard key).</summary>
    void OpenSystemSettings(bool sound);

    /// <summary>Volume change feedback sound (when enabled).</summary>
    void PlayVolumeChangedSound();
}

/// <summary>
/// Standard (media) keys: brightness, volume and mute with their modifier combinations.
/// Port of MediaKeyTapManager.swift.
/// </summary>
public sealed class MediaKeyHandler(DisplayManager displayManager, IAudioService audio, IKeyboardShell shell)
{
    private Prefs Prefs => displayManager.Prefs;

    private AppState State => displayManager.State;

    /// <summary>Keys currently intercepted (and swallowed). Recomputed by <see cref="UpdateKeys"/>.</summary>
    public IReadOnlySet<MediaKey> ActiveKeys { get; private set; } = new HashSet<MediaKey>();

    /// <summary>Use the alternative brightness keys (Scroll Lock / Pause, F14/F15 on Apple keyboards).</summary>
    public bool UseAlternateBrightnessKeys => !Prefs.Bool(PrefKey.DisableAltBrightnessKeys);

    /// <summary>Recomputes which standard keys to intercept (updateMediaKeyTap).</summary>
    public void UpdateKeys()
    {
        var keys = new HashSet<MediaKey>();
        if (Prefs.MediaBrightnessKeysEnabled)
        {
            keys.Add(MediaKey.BrightnessUp);
            keys.Add(MediaKey.BrightnessDown);
        }

        if (Prefs.MediaVolumeKeysEnabled)
        {
            keys.Add(MediaKey.Mute);
            keys.Add(MediaKey.VolumeUp);
            keys.Add(MediaKey.VolumeDown);
        }

        // Leave brightness keys alone when there is nothing but built-in displays, and while asleep/reconfiguring.
        var disengageBrightness = !displayManager.Displays.Any(d => !d.IsBuiltIn) || !State.IsActive;
        if (disengageBrightness && !Prefs.Bool(PrefKey.UseFineScaleBrightness))
        {
            keys.Remove(MediaKey.BrightnessUp);
            keys.Remove(MediaKey.BrightnessDown);
        }

        // Volume keys are only taken over when the sound goes to a display without its own (software) volume.
        if (audio.DefaultOutputName is { } deviceName)
        {
            var release = Prefs.MultiKeyboardVolume == MultiKeyboardVolume.AudioDeviceNameMatching
                ? displayManager.UpdateAudioControlTargetDisplays(deviceName) == 0
                : !audio.DefaultOutputIsDisplay;
            if (release)
            {
                keys.Remove(MediaKey.Mute);
                keys.Remove(MediaKey.VolumeUp);
                keys.Remove(MediaKey.VolumeDown);
            }
        }

        ActiveKeys = keys;
        Log.Info($"Standard keys listened for: {string.Join(", ", keys)}");
    }

    /// <summary>Handles a standard key event. Returns true when the key was used.</summary>
    public void Handle(MediaKey key, bool isPressed, bool isRepeat, KeyModifiers modifiers)
    {
        var isControl = modifiers.HasFlag(KeyModifiers.Control);
        var isCommand = modifiers.HasFlag(KeyModifiers.Win);
        var isOption = modifiers.HasFlag(KeyModifiers.Alt);
        var isShift = modifiers.HasFlag(KeyModifiers.Shift);
        if (isPressed && isCommand && !isControl && key == MediaKey.BrightnessDown && displayManager.EngageMirror())
        {
            return;
        }

        if (!State.IsActive)
        {
            return;
        }

        if (isPressed && HandleOpenSettings(key, isRepeat, modifiers))
        {
            return;
        }

        var isSmallIncrement = isOption && isShift;
        var isContrast = isControl && isOption && isCommand;
        if (key is MediaKey.BrightnessUp or MediaKey.BrightnessDown && Prefs.Bool(PrefKey.UseFineScaleBrightness))
        {
            isSmallIncrement = !isSmallIncrement;
        }

        if (key is MediaKey.VolumeUp or MediaKey.VolumeDown or MediaKey.Mute && Prefs.Bool(PrefKey.UseFineScaleVolume))
        {
            isSmallIncrement = !isSmallIncrement;
        }

        if (isPressed && isControl && !isOption && key is MediaKey.BrightnessUp or MediaKey.BrightnessDown)
        {
            HandleDirectedBrightness(isCommand, key == MediaKey.BrightnessUp, isSmallIncrement);
            return;
        }

        SendDisplayCommandVolumeMute(key, isRepeat, isSmallIncrement, isPressed);
        SendDisplayCommandBrightnessContrast(key, isSmallIncrement, isPressed, isContrast);
    }

    private void HandleDirectedBrightness(bool isCommandModifier, bool isUp, bool isSmallIncrement)
    {
        if (isCommandModifier)
        {
            foreach (var display in displayManager.Displays.Where(d => !d.IsBuiltIn))
            {
                display.StepBrightness(isUp, isSmallIncrement);
            }
        }
        else if (displayManager.GetBuiltInDisplay() is InternalDisplay builtIn)
        {
            builtIn.StepBrightness(isUp, isSmallIncrement);
        }
    }

    private void SendDisplayCommandVolumeMute(MediaKey key, bool isRepeat, bool isSmallIncrement, bool isPressed)
    {
        if (key is not (MediaKey.VolumeUp or MediaKey.VolumeDown or MediaKey.Mute) || !State.IsActive)
        {
            return;
        }

        var soundPlayed = false;
        foreach (var display in displayManager.GetAffectedDisplays(isVolume: true).Where(d => !d.ReadPrefAsBool(PrefKey.IsDisabled)))
        {
            if (display is not ExternalDisplay external)
            {
                continue;
            }

            if (key == MediaKey.Mute)
            {
                // The mute key does not respond to press + hold or key up.
                if (!isRepeat && isPressed)
                {
                    external.ToggleMute();
                    if (!soundPlayed && external.ReadPrefAsInt(command: Command.AudioMuteScreenBlank) != 1 && !external.ReadPrefAsBool(PrefKey.UnavailableDdc, Command.AudioSpeakerVolume))
                    {
                        shell.PlayVolumeChangedSound();
                        soundPlayed = true;
                    }
                }
            }
            else if (isPressed)
            {
                external.StepVolume(key == MediaKey.VolumeUp, isSmallIncrement);
            }
            else if (!soundPlayed && !external.ReadPrefAsBool(PrefKey.UnavailableDdc, Command.AudioSpeakerVolume))
            {
                shell.PlayVolumeChangedSound();
                soundPlayed = true;
            }
        }
    }

    private void SendDisplayCommandBrightnessContrast(MediaKey key, bool isSmallIncrement, bool isPressed, bool isContrast)
    {
        if (key is not (MediaKey.BrightnessUp or MediaKey.BrightnessDown) || !State.IsActive || !isPressed)
        {
            return;
        }

        var affected = displayManager.GetAffectedDisplays(isBrightness: true);
        foreach (var display in affected.Where(d => !d.ReadPrefAsBool(PrefKey.IsDisabled)))
        {
            if (isContrast && display is ExternalDisplay external)
            {
                external.StepContrast(key == MediaKey.BrightnessUp, isSmallIncrement);
            }
            else if (key == MediaKey.BrightnessDown || !BlockedBySwAfterBrightness(affected, display))
            {
                display.StepBrightness(key == MediaKey.BrightnessUp, isSmallIncrement);
            }
        }
    }

    /// <summary>
    /// With separate combined scales, while some displays are in software dimming, brightness up only raises those
    /// first so all displays leave software dimming together.
    /// </summary>
    internal bool BlockedBySwAfterBrightness(IEnumerable<Display> affected, Display display)
    {
        static bool InSwAfterBrightness(Display d) => d is ExternalDisplay e && e.IsSwBrightnessNotDefault() && !e.IsSw();
        var anyInSwAfterBrightness = Prefs.Bool(PrefKey.SeparateCombinedScale) && affected.Any(InSwAfterBrightness);
        return anyInSwAfterBrightness && !InSwAfterBrightness(display);
    }

    private bool HandleOpenSettings(MediaKey key, bool isRepeat, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.Alt || isRepeat)
        {
            return false;
        }

        shell.OpenSystemSettings(sound: key is MediaKey.Mute or MediaKey.VolumeUp or MediaKey.VolumeDown);
        return true;
    }
}
