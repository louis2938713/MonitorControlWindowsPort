using System.Diagnostics;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Mathematics;
using MonitorControl.Core.Platform;

namespace MonitorControl.Core.Displays;

/// <summary>
/// External display controlled by DDC/CI (brightness, contrast, volume, mute), with combined hardware + software
/// dimming. Falls back to software control when DDC is unavailable. Ported from OtherDisplay.swift.
/// </summary>
public sealed class ExternalDisplay(DisplayInfo info, DisplayManager manager, IDdcChannel? ddc) : Display(info, manager)
{
    private readonly object _writeLock = new();
    private readonly Dictionary<Command, ushort> _writeDdcNextValue = [];
    private readonly Dictionary<Command, ushort> _writeDdcLastSavedValue = [];
    private readonly object _swAfterOsdAnimationLock = new();
    private long _lastAnimationStartedTicks = Stopwatch.GetTimestamp();

    public IDdcChannel? Ddc { get; } = ddc;

    public bool IsDiscouraged { get; set; }

    public int PollingCount
    {
        get => (PollingMode)ReadPrefAsInt(PrefKey.PollingMode) switch
        {
            PollingMode.None => 0,
            PollingMode.Minimal => 1,
            PollingMode.Normal => 5,
            PollingMode.Heavy => 20,
            PollingMode.Custom => ReadPrefAsInt(PrefKey.PollingCount),
            _ => 0,
        };
        set => SavePref(value, PrefKey.PollingCount);
    }

    public bool IsSwOnly() => Ddc is null || IsVirtual || IsDummy;

    public bool IsSw() => ReadPrefAsBool(PrefKey.ForceSw) || IsSwOnly();

    public float CombinedBrightnessSwitchingValue() => DdcMath.CombinedSwitchingValue(ReadPrefAsInt(PrefKey.CombinedBrightnessSwitchingPoint));

    private bool CombinedEnabled => !Prefs.Bool(PrefKey.DisableCombinedBrightness);

    // MARK: - Setup

    public void ProcessCurrentDdcValue(bool isReadFromDisplay, Command command, bool firstRun, ushort currentDdcValue)
    {
        var switching = CombinedBrightnessSwitchingValue();
        var startupWrite = Prefs.StartupAction == StartupAction.Write;
        float currentValue;
        if (isReadFromDisplay)
        {
            currentValue = ConvDdcToValue(command, currentDdcValue);
            if (CombinedEnabled && command == Command.Brightness)
            {
                Log.Info("- Combined brightness mapping on DDC data.");
                if (currentValue > 0)
                {
                    currentValue = switching + (currentValue * (1 - switching));
                }
                else if (currentValue == 0 && firstRun && !startupWrite)
                {
                    currentValue = switching;
                }
                else if (PrefExists(command: command) && ReadPrefAsFloat(command: command) <= switching)
                {
                    currentValue = ReadPrefAsFloat(command: command);
                }
                else
                {
                    currentValue = switching;
                }
            }
        }
        else
        {
            currentValue = ReadPrefAsFloat(command: command);
            if (CombinedEnabled && command == Command.Brightness)
            {
                Log.Info("- Combined brightness mapping on saved data.");
                if (!PrefExists(command: command))
                {
                    currentValue = switching + (ConvDdcToValue(command, currentDdcValue) * (1 - switching));
                }
                else if (firstRun && currentValue < switching && !startupWrite)
                {
                    currentValue = switching;
                }
            }
            else
            {
                currentValue = PrefExists(command: command) ? ReadPrefAsFloat(command: command) : ConvDdcToValue(command, currentDdcValue);
            }
        }

        SavePref(currentValue, command: command);
        if (command == Command.Brightness)
        {
            SmoothBrightnessTransient = currentValue;
        }
    }

    public ushort GetDdcValueFromPrefs(Command command)
    {
        var value = CombinedEnabled && command == Command.Brightness
            ? Math.Max(0, ReadPrefAsFloat(command: command) - CombinedBrightnessSwitchingValue()) * (1 / (1 - CombinedBrightnessSwitchingValue()))
            : ReadPrefAsFloat(command: command);
        return ConvValueToDdc(command, value);
    }

    public void RestoreDdcSettingsToDisplay(Command command)
    {
        if (SmoothBrightnessRunning || IsSw() || ReadPrefAsBool(PrefKey.UnavailableDdc, command) || !ReadPrefAsBool(PrefKey.IsTouched, command)
            || Prefs.StartupAction != StartupAction.Write || State.SafeMode)
        {
            return;
        }

        var restoreValue = GetDdcValueFromPrefs(command);
        Log.Info($"Restoring {command} DDC value {restoreValue} for {Name}");
        WriteDdcValues(command, restoreValue);
        if (command == Command.AudioSpeakerVolume && ReadPrefAsBool(PrefKey.EnableMuteUnmute))
        {
            var currentMuteValue = CurrentMuteValue();
            Log.Info($"- Writing last saved DDC value for Mute: {currentMuteValue}");
            WriteDdcValues(Command.AudioMuteScreenBlank, (ushort)currentMuteValue);
        }
    }

    public void SetupCurrentAndMaxValues(Command command, bool firstRun = false)
    {
        (ushort Current, ushort Max)? ddcValues = null;
        var maxDdcValue = (ushort)DdcMath.MaxDetectLimit;
        var currentDdcValue = command switch
        {
            Command.AudioSpeakerVolume => (ushort)(DdcMath.MaxDetectLimit * 0.125f), // lower default audio value as high volume might rattle the user
            Command.Contrast => (ushort)(DdcMath.MaxDetectLimit * 0.750f),
            _ => (ushort)DdcMath.MaxDetectLimit,
        };
        Log.Info($"Setting up display {Id} for {command}");
        if (!IsSw())
        {
            if (Prefs.StartupAction == StartupAction.Read && PollingCount != 0 && !State.SafeMode)
            {
                Log.Info($"- Reading DDC from display {PollingCount} times");
                ddcValues = ReadDdcValues(command, PollingCount, ReadPrefAsBool(PrefKey.LongerDelay));
                if (ddcValues is { } values)
                {
                    (currentDdcValue, maxDdcValue) = values;
                    ProcessCurrentDdcValue(true, command, firstRun, currentDdcValue);
                    Log.Info("- DDC read successful.");
                }
                else
                {
                    Log.Info("- DDC read failed.");
                }
            }
            else
            {
                Log.Info("- DDC read disabled.");
            }

            if (ReadPrefAsInt(PrefKey.MaxDdcOverride, command) > ReadPrefAsInt(PrefKey.MinDdcOverride, command))
            {
                SavePref(ReadPrefAsInt(PrefKey.MaxDdcOverride, command), PrefKey.MaxDdc, command);
            }
            else
            {
                SavePref(Math.Min((int)maxDdcValue, DdcMath.MaxDetectLimit), PrefKey.MaxDdc, command);
            }

            if (ddcValues is null)
            {
                ProcessCurrentDdcValue(false, command, firstRun, currentDdcValue);
                currentDdcValue = GetDdcValueFromPrefs(command);
            }

            Log.Info($"- Current DDC value: {currentDdcValue}, min: {ReadPrefAsInt(PrefKey.MinDdcOverride, command)}, max: {ReadPrefAsInt(PrefKey.MaxDdc, command)} (reported {maxDdcValue}), internal: {ReadPrefAsFloat(command: command)}, {(ReadPrefAsBool(PrefKey.IsTouched, command) ? "touched" : "untouched")}");
            if (command == Command.AudioSpeakerVolume)
            {
                SetupMuteUnMute();
            }

            RestoreDdcSettingsToDisplay(command);
        }
        else
        {
            SavePref(PrefExists(command: command) ? ReadPrefAsFloat(command: command) : 1f, command: command);
            SavePref(ReadPrefAsFloat(command: command), PrefKey.SwBrightness);
            BrightnessSyncSourceValue = ReadPrefAsFloat(command: command);
            SmoothBrightnessTransient = ReadPrefAsFloat(command: command);
            Log.Info($"- Software controlled display current internal value: {ReadPrefAsFloat(command: command)}");
        }
    }

    public void SetupMuteUnMute()
    {
        if (IsSw() || ReadPrefAsBool(PrefKey.UnavailableDdc, Command.AudioSpeakerVolume) || !ReadPrefAsBool(PrefKey.EnableMuteUnmute))
        {
            return;
        }

        var currentMuteValue = CurrentMuteValue();
        if (PollingCount != 0 && !State.SafeMode && Prefs.StartupAction == StartupAction.Read)
        {
            Log.Info($"- Reading DDC from display {PollingCount} times for Mute");
            if (ReadDdcValues(Command.AudioMuteScreenBlank, PollingCount, ReadPrefAsBool(PrefKey.LongerDelay)) is { } muteValues)
            {
                Log.Info($"- Success, current Mute setting: {muteValues.Current}");
                currentMuteValue = muteValues.Current;
            }
            else
            {
                Log.Info("- Mute read failed");
            }
        }

        SavePref(currentMuteValue, command: Command.AudioMuteScreenBlank);
    }

    public float SetupSliderCurrentValue(Command command) =>
        command == Command.AudioSpeakerVolume && ReadPrefAsBool(PrefKey.EnableMuteUnmute) && ReadPrefAsInt(command: Command.AudioMuteScreenBlank) == 1
            ? 0
            : ReadPrefAsFloat(command: command);

    /// <summary>Saved mute state: 1 = muted, 2 = unmuted (0 = never saved, treated as unmuted).</summary>
    private int CurrentMuteValue() => ReadPrefAsInt(command: Command.AudioMuteScreenBlank) is 0 ? 2 : ReadPrefAsInt(command: Command.AudioMuteScreenBlank);

    // MARK: - Volume, contrast and mute

    public void StepVolume(bool isUp, bool isSmallIncrement)
    {
        if (ReadPrefAsBool(PrefKey.UnavailableDdc, Command.AudioSpeakerVolume))
        {
            Manager.Osd.ShowVolumeDisabled(Id);
            return;
        }

        var currentValue = ReadPrefAsFloat(command: Command.AudioSpeakerVolume);
        int? muteValue = null;
        var volumeOsdValue = StepMath.CalcNewValue(currentValue, isUp, isSmallIncrement);
        var muted = ReadPrefAsInt(command: Command.AudioMuteScreenBlank) == 1;
        if (muted && volumeOsdValue > 0)
        {
            muteValue = 2;
        }
        else if (!muted && volumeOsdValue == 0)
        {
            muteValue = 1;
        }

        var isAlreadySet = volumeOsdValue == ReadPrefAsFloat(command: Command.AudioSpeakerVolume);
        if (!isAlreadySet)
        {
            if (muteValue is { } mute && ReadPrefAsBool(PrefKey.EnableMuteUnmute))
            {
                WriteDdcValues(Command.AudioMuteScreenBlank, (ushort)mute);
                SavePref(mute, command: Command.AudioMuteScreenBlank);
            }

            if (!ReadPrefAsBool(PrefKey.EnableMuteUnmute) || volumeOsdValue != 0)
            {
                WriteDdcValues(Command.AudioSpeakerVolume, ConvValueToDdc(Command.AudioSpeakerVolume, volumeOsdValue));
            }
        }

        if (!ReadPrefAsBool(PrefKey.HideOsd))
        {
            Manager.Osd.Show(Id, Command.AudioSpeakerVolume, volumeOsdValue, roundChiclet: !isSmallIncrement);
        }

        if (!isAlreadySet)
        {
            SavePref(volumeOsdValue, command: Command.AudioSpeakerVolume);
            if (SliderHandler.TryGetValue(Command.AudioSpeakerVolume, out var slider))
            {
                slider.SetValue(volumeOsdValue, Id);
            }
        }
    }

    public void StepContrast(bool isUp, bool isSmallIncrement)
    {
        if (ReadPrefAsBool(PrefKey.UnavailableDdc, Command.Contrast) || IsSw())
        {
            return;
        }

        var currentValue = ReadPrefAsFloat(command: Command.Contrast);
        var contrastOsdValue = StepMath.CalcNewValue(currentValue, isUp, isSmallIncrement);
        var isAlreadySet = contrastOsdValue == ReadPrefAsFloat(command: Command.Contrast);
        if (!isAlreadySet)
        {
            WriteDdcValues(Command.Contrast, ConvValueToDdc(Command.Contrast, contrastOsdValue));
        }

        Manager.Osd.Show(Id, Command.Contrast, contrastOsdValue, roundChiclet: !isSmallIncrement);
        if (!isAlreadySet)
        {
            SavePref(contrastOsdValue, command: Command.Contrast);
            if (SliderHandler.TryGetValue(Command.Contrast, out var slider))
            {
                slider.SetValue(contrastOsdValue, Id);
            }
        }
    }

    public void ToggleMute(bool fromVolumeSlider = false)
    {
        if (ReadPrefAsBool(PrefKey.UnavailableDdc, Command.AudioSpeakerVolume))
        {
            Manager.Osd.ShowMuteDisabled(Id);
            return;
        }

        int muteValue;
        float volumeOsdValue;
        if (ReadPrefAsInt(command: Command.AudioMuteScreenBlank) != 1)
        {
            muteValue = 1;
            volumeOsdValue = 0;
        }
        else
        {
            muteValue = 2;
            volumeOsdValue = ReadPrefAsFloat(command: Command.AudioSpeakerVolume);
            // The volume set right after unmuting from 0 is unpredictable, so use a single filled chiclet.
            if (volumeOsdValue == 0)
            {
                volumeOsdValue = 1 / StepMath.ChicletCount;
                SavePref(volumeOsdValue, command: Command.AudioSpeakerVolume);
            }
        }

        if (ReadPrefAsBool(PrefKey.EnableMuteUnmute))
        {
            WriteDdcValues(Command.AudioMuteScreenBlank, (ushort)muteValue);
        }

        SavePref(muteValue, command: Command.AudioMuteScreenBlank);
        if (!ReadPrefAsBool(PrefKey.EnableMuteUnmute) || volumeOsdValue > 0)
        {
            WriteDdcValues(Command.AudioSpeakerVolume, ConvValueToDdc(Command.AudioSpeakerVolume, volumeOsdValue));
        }

        if (!fromVolumeSlider)
        {
            if (!ReadPrefAsBool(PrefKey.HideOsd))
            {
                Manager.Osd.Show(Id, volumeOsdValue > 0 ? Command.AudioSpeakerVolume : Command.AudioMuteScreenBlank, volumeOsdValue, roundChiclet: true);
            }

            if (SliderHandler.TryGetValue(Command.AudioSpeakerVolume, out var slider))
            {
                slider.SetValue(volumeOsdValue);
            }
        }
    }

    // MARK: - Brightness

    /// <summary>OSD animation hinting that software dimming took over after hardware brightness reached zero.</summary>
    public void DoSwAfterOsdAnimation()
    {
        _lastAnimationStartedTicks = Stopwatch.GetTimestamp();
        Task.Run(() =>
        {
            lock (_swAfterOsdAnimationLock)
            {
                if (Stopwatch.GetElapsedTime(_lastAnimationStartedTicks) > TimeSpan.FromMilliseconds(50))
                {
                    return;
                }

                foreach (var value in Enumerable.Range(1, 5).Concat(Enumerable.Range(1, 5).Reverse()))
                {
                    if (ReadPrefAsFloat(command: Command.Brightness) > CombinedBrightnessSwitchingValue())
                    {
                        return;
                    }

                    Manager.Ui.Post(() => Manager.Osd.Show(Id, Command.Brightness, value, maxValue: 100, roundChiclet: false));
                    Thread.Sleep(TimeSpan.FromSeconds(value * 2 / 300.0));
                }

                Manager.Ui.Post(() => Manager.Osd.Show(Id, Command.Brightness, 0, roundChiclet: true));
            }
        });
    }

    public override void StepBrightness(bool isUp, bool isSmallIncrement)
    {
        if (IsSw())
        {
            base.StepBrightness(isUp, isSmallIncrement);
            return;
        }

        if (ReadPrefAsBool(PrefKey.UnavailableDdc, Command.Brightness))
        {
            return;
        }

        var currentValue = ReadPrefAsFloat(command: Command.Brightness);
        float osdValue;
        if (CombinedEnabled && Prefs.Bool(PrefKey.SeparateCombinedScale))
        {
            osdValue = StepMath.CalcNewValue(currentValue, isUp, isSmallIncrement, half: true);
            SetBrightness(osdValue);
            var switching = CombinedBrightnessSwitchingValue();
            if (osdValue > switching)
            {
                Manager.Osd.Show(Id, Command.Brightness, osdValue - switching, maxValue: switching, roundChiclet: !isSmallIncrement);
            }
            else
            {
                DoSwAfterOsdAnimation();
            }
        }
        else
        {
            osdValue = StepMath.CalcNewValue(currentValue, isUp, isSmallIncrement);
            SetBrightness(osdValue);
            Manager.Osd.Show(Id, Command.Brightness, osdValue, roundChiclet: !isSmallIncrement);
        }

        if (SliderHandler.TryGetValue(Command.Brightness, out var slider))
        {
            slider.SetValue(osdValue, Id);
            BrightnessSyncSourceValue = osdValue;
        }
    }

    public override bool SetBrightness(float to = -1, bool slow = false)
    {
        CheckGammaInterference();
        return base.SetBrightness(to, slow);
    }

    public override bool SetDirectBrightness(float to, bool transient = false)
    {
        var value = Math.Clamp(to, 0, 1);
        if (IsSw())
        {
            return base.SetDirectBrightness(to, transient);
        }

        if (CombinedEnabled)
        {
            var switching = CombinedBrightnessSwitchingValue();
            float brightnessValue;
            float brightnessSwValue;
            if (value >= switching)
            {
                brightnessValue = (value - switching) * (1 / (1 - switching));
                brightnessSwValue = 1;
            }
            else
            {
                brightnessValue = 0;
                brightnessSwValue = value / switching;
            }

            WriteDdcValues(Command.Brightness, ConvValueToDdc(Command.Brightness, brightnessValue));
            if (ReadPrefAsFloat(PrefKey.SwBrightness) != brightnessSwValue)
            {
                SetSwBrightness(brightnessSwValue);
            }
        }
        else
        {
            WriteDdcValues(Command.Brightness, ConvValueToDdc(Command.Brightness, value));
        }

        if (!transient)
        {
            SavePref(value, command: Command.Brightness);
            SmoothBrightnessTransient = value;
        }

        return true;
    }

    public override float GetBrightness() => PrefExists(command: Command.Brightness) ? ReadPrefAsFloat(command: Command.Brightness) : 1;

    // MARK: - DDC

    public IReadOnlyList<byte> GetRemapControlCodes(Command command) => DdcMath.ParseRemapCodes(ReadPrefAsString(PrefKey.RemapDdc, command));

    public void WriteDdcValues(Command command, ushort value)
    {
        if (!State.IsActive || ReadPrefAsBool(PrefKey.ForceSw) || ReadPrefAsBool(PrefKey.UnavailableDdc, command))
        {
            return;
        }

        lock (_writeLock)
        {
            _writeDdcNextValue[command] = value;
        }

        Manager.DdcWorker.Enqueue(() => AsyncPerformWriteDdcValues(command));
    }

    private void AsyncPerformWriteDdcValues(Command command)
    {
        ushort value;
        lock (_writeLock)
        {
            if (!_writeDdcNextValue.TryGetValue(command, out value)
                || (_writeDdcLastSavedValue.TryGetValue(command, out var lastValue) && lastValue == value))
            {
                return;
            }

            _writeDdcLastSavedValue[command] = value;
        }

        SavePref(true, PrefKey.IsTouched, command);
        var controlCodes = GetRemapControlCodes(command);
        if (controlCodes.Count == 0)
        {
            controlCodes = [(byte)command];
        }

        foreach (var controlCode in controlCodes)
        {
            if (Ddc is null)
            {
                return;
            }

            if (!Ddc.Write(controlCode, value))
            {
                Thread.Sleep(20);
                Ddc.Write(controlCode, value);
            }
        }
    }

    public (ushort Current, ushort Max)? ReadDdcValues(Command command, int tries, bool longerDelay)
    {
        if (!State.IsActive || ReadPrefAsBool(PrefKey.ForceSw) || ReadPrefAsBool(PrefKey.UnavailableDdc, command) || Ddc is null)
        {
            return null;
        }

        var controlCodes = GetRemapControlCodes(command);
        var controlCode = controlCodes.Count == 0 ? (byte)command : controlCodes[0];
        var ddcChannel = Ddc;
        return Manager.DdcWorker.Run(() =>
        {
            for (var attempt = 1; attempt <= Math.Max(1, tries); attempt++)
            {
                Thread.Sleep(longerDelay ? 50 : 10);
                if (ddcChannel.Read(controlCode) is { } values)
                {
                    if (attempt > 1)
                    {
                        Log.Info($"Reading {command} took {attempt} tries.");
                    }

                    return values;
                }
            }

            return ((ushort Current, ushort Max)?)null;
        });
    }

    public ushort ConvValueToDdc(Command command, float value) => DdcMath.ValueToDdc(
        value,
        ReadPrefAsInt(PrefKey.MinDdcOverride, command),
        ReadPrefAsInt(PrefKey.MaxDdc, command),
        ReadPrefAsInt(PrefKey.CurveDdc, command),
        ReadPrefAsBool(PrefKey.InvertDdc, command),
        neverZero: command == Command.AudioSpeakerVolume);

    public float ConvDdcToValue(Command command, ushort value) => DdcMath.DdcToValue(
        value,
        ReadPrefAsInt(PrefKey.MinDdcOverride, command),
        ReadPrefAsInt(PrefKey.MaxDdc, command),
        ReadPrefAsInt(PrefKey.CurveDdc, command),
        ReadPrefAsBool(PrefKey.InvertDdc, command));
}
