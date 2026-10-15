using MonitorControl.Core.Enums;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;

namespace MonitorControl.Core.Displays;

/// <summary>Owns the display objects and the shared services they use. Ported from DisplayManager.swift.</summary>
public sealed class DisplayManager(
    Prefs prefs,
    AppState state,
    IDisplayPlatform platform,
    IShadeHost shades,
    IOsd osd,
    IUiDispatcher ui,
    IAlerts alerts) : IDisposable
{
    private List<Display> _displays = [];

    /// <summary>Raised when per-display settings changed from the core (the Displays settings page reloads).</summary>
    public event EventHandler? DisplaySettingsChanged;

    public Prefs Prefs { get; } = prefs;

    public AppState State { get; } = state;

    public IDisplayPlatform Platform { get; } = platform;

    public IOsd Osd { get; } = osd;

    public IUiDispatcher Ui { get; } = ui;

    public IAlerts Alerts { get; } = alerts;

    /// <summary>Global DDC queue: every DDC transaction runs here, one at a time.</summary>
    public SerialWorker DdcWorker { get; } = new("Global DDC queue");

    public IReadOnlyList<Display> Displays => _displays;

    public List<ExternalDisplay> AudioControlTargetDisplays { get; } = [];

    public int GammaInterferenceCounter { get; set; }

    public bool GammaInterferenceWarningShown { get; set; }

    public void NotifyDisplaySettingsChanged() => DisplaySettingsChanged?.Invoke(this, EventArgs.Empty);

    // MARK: - Shades

    public bool IsDisqualifiedFromShade(uint displayId)
    {
        var info = InfoOf(displayId);
        if (info is null || !info.IsInMirrorSet)
        {
            return false;
        }

        // In a mirror set only a virtual/dummy main display may be shaded (the shade would dim every copy otherwise).
        return !(displayId == ResolveEffectiveDisplayId(displayId) && (info.IsVirtual || info.IsDummy));
    }

    public bool UpdateShade(uint displayId) => !IsDisqualifiedFromShade(displayId) && shades.Update(displayId);

    public bool DestroyShade(uint displayId) => shades.Destroy(displayId);

    public bool DestroyAllShades()
    {
        var destroyed = shades.DestroyAll();
        Log.Info(destroyed ? "Destroyed all shades." : "No shades were found to be destroyed.");
        return destroyed;
    }

    public float? GetShadeAlpha(uint displayId) => IsDisqualifiedFromShade(displayId) ? 1 : shades.GetAlpha(displayId);

    public bool SetShadeAlpha(float value, uint displayId) => !IsDisqualifiedFromShade(displayId) && shades.SetAlpha(displayId, value);

    // MARK: - Configuration

    public void ConfigureDisplays()
    {
        ClearDisplays();
        foreach (var info in Platform.EnumerateDisplays())
        {
            Display display;
            var internalBrightness = info.IsBuiltIn ? Platform.OpenInternalBrightness(info.Id) : null;
            if (internalBrightness is not null)
            {
                display = new InternalDisplay(info, this, internalBrightness);
                Log.Info($"Built-in display found - ID: {info.Id}, Name: {info.Name}");
            }
            else
            {
                var ddc = info.IsVirtual || info.IsDummy ? null : DdcWorker.Run(() => Platform.OpenDdc(info.Id, probe: !State.SafeMode));
                display = new ExternalDisplay(info, this, ddc);
                Log.Info($"Other display found - ID: {info.Id}, Name: {info.Name}, DDC: {ddc is not null}");
            }

            _displays.Add(display);
            display.Initialize();
        }
    }

    public void SetupOtherDisplays(bool firstRun = false)
    {
        foreach (var otherDisplay in GetExternalDisplays())
        {
            foreach (var command in new[] { Command.AudioSpeakerVolume, Command.Contrast })
            {
                if (!otherDisplay.ReadPrefAsBool(PrefKey.UnavailableDdc, command) && !otherDisplay.IsSw())
                {
                    otherDisplay.SetupCurrentAndMaxValues(command, firstRun);
                }
            }

            if ((!otherDisplay.IsSw() && !otherDisplay.ReadPrefAsBool(PrefKey.UnavailableDdc, Command.Brightness)) || otherDisplay.IsSw())
            {
                otherDisplay.SetupCurrentAndMaxValues(Command.Brightness, firstRun);
                otherDisplay.BrightnessSyncSourceValue = otherDisplay.ReadPrefAsFloat(command: Command.Brightness);
            }
        }
    }

    public void RestoreOtherDisplays()
    {
        foreach (var otherDisplay in GetDdcCapableDisplays())
        {
            foreach (var command in new[] { Command.Contrast, Command.Brightness })
            {
                if (!otherDisplay.ReadPrefAsBool(PrefKey.UnavailableDdc, command))
                {
                    otherDisplay.RestoreDdcSettingsToDisplay(command);
                }
            }
        }
    }

    public static string NormalizedName(string name) =>
        new(name.Where(c => c is not '(' and not ')' and not ' ' && !char.IsAsciiDigit(c)).ToArray());

    /// <summary>
    /// Finds the displays whose audio device is the given output device. Windows names display audio endpoints
    /// after the monitor ("DELL U2720Q (NVIDIA High Definition Audio)"), so the display name is looked up inside it.
    /// </summary>
    public int UpdateAudioControlTargetDisplays(string deviceName)
    {
        AudioControlTargetDisplays.Clear();
        Log.Info("Detecting displays for audio control via audio device name matching...");
        var normalizedDevice = NormalizedName(deviceName);
        foreach (var display in GetDdcCapableDisplays())
        {
            var displayAudioDeviceName = display.ReadPrefAsString(PrefKey.AudioDeviceNameOverride);
            if (displayAudioDeviceName.Length == 0)
            {
                displayAudioDeviceName = display.Info.RawName;
            }

            var normalizedDisplay = NormalizedName(displayAudioDeviceName);
            if (normalizedDisplay.Length > 0 && normalizedDevice.Contains(normalizedDisplay, StringComparison.OrdinalIgnoreCase))
            {
                AudioControlTargetDisplays.Add(display);
                Log.Info($"Added display for audio control - {display.Name}");
            }
        }

        return AudioControlTargetDisplays.Count;
    }

    public IReadOnlyList<ExternalDisplay> GetExternalDisplays() => _displays.OfType<ExternalDisplay>().ToList();

    public IReadOnlyList<InternalDisplay> GetInternalDisplays() => _displays.OfType<InternalDisplay>().ToList();

    public IReadOnlyList<ExternalDisplay> GetDdcCapableDisplays() => _displays.OfType<ExternalDisplay>().Where(d => !d.IsSw()).ToList();

    /// <summary>Displays sorted by the name shown to the user.</summary>
    public IReadOnlyList<Display> SortDisplaysByFriendlyName() =>
        _displays.OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase).ToList();

    public Display? GetBuiltInDisplay() => _displays.FirstOrDefault(d => d.IsBuiltIn);

    public Display? GetCurrentDisplay(bool byFocus = false)
    {
        var id = byFocus ? Platform.DisplayWithFocus() : Platform.DisplayUnderCursor();
        return id is { } displayId ? _displays.FirstOrDefault(d => d.Id == displayId) : null;
    }

    public void ClearDisplays() => _displays = [];

    public void AddDisplayCounterSuffixes()
    {
        foreach (var group in _displays.GroupBy(d => d.Name).Where(g => g.Count() > 1))
        {
            var i = 0;
            foreach (var display in group)
            {
                i++;
                display.Name = $"{display.Name} ({i})";
            }
        }
    }

    public void ResetSwBrightnessForAllDisplays(bool prefsOnly = false, bool noPrefSave = false, bool async = false)
    {
        foreach (var otherDisplay in GetExternalDisplays())
        {
            if (!prefsOnly)
            {
                otherDisplay.SetSwBrightness(1, smooth: async, noPrefSave: noPrefSave);
                if (!noPrefSave)
                {
                    otherDisplay.SmoothBrightnessTransient = 1;
                }
            }
            else if (!noPrefSave)
            {
                otherDisplay.SavePref(1f, PrefKey.SwBrightness);
                otherDisplay.SmoothBrightnessTransient = 1;
            }

            if (otherDisplay.IsSw() && !noPrefSave)
            {
                otherDisplay.SavePref(1f, command: Command.Brightness);
            }
        }
    }

    public void RestoreSwBrightnessForAllDisplays(bool async = false)
    {
        var combined = !Prefs.Bool(PrefKey.DisableCombinedBrightness);
        foreach (var otherDisplay in GetExternalDisplays())
        {
            var brightness = otherDisplay.ReadPrefAsFloat(command: Command.Brightness);
            if ((brightness == 0 && combined)
                || (brightness < otherDisplay.CombinedBrightnessSwitchingValue() && !Prefs.Bool(PrefKey.SeparateCombinedScale) && combined)
                || otherDisplay.IsSw())
            {
                var savedPrefValue = otherDisplay.ReadPrefAsFloat(PrefKey.SwBrightness);
                if (otherDisplay.GetSwBrightness() != savedPrefValue)
                {
                    Osd.PopEmpty(otherDisplay.Id, Command.Brightness); // Hints why the brightness suddenly changes.
                }

                otherDisplay.SavePref(otherDisplay.GetSwBrightness(), PrefKey.SwBrightness);
                Log.Info($"Restoring sw brightness to {savedPrefValue} on other display {otherDisplay.Id}");
                otherDisplay.SetSwBrightness(savedPrefValue, smooth: async);
                if (otherDisplay.IsSw() && otherDisplay.SliderHandler.TryGetValue(Command.Brightness, out var slider))
                {
                    slider.SetValue(savedPrefValue, otherDisplay.Id);
                }
            }
            else
            {
                otherDisplay.SetSwBrightness(1);
            }
        }
    }

    /// <summary>Displays affected by a keyboard action, depending on the "screen to control" settings.</summary>
    public IReadOnlyList<Display> GetAffectedDisplays(bool isBrightness = false, bool isVolume = false)
    {
        Display? currentDisplay = null;
        if (isBrightness)
        {
            if (Prefs.MultiKeyboardBrightness == MultiKeyboardBrightness.AllScreens)
            {
                return _displays;
            }

            currentDisplay = GetCurrentDisplay(byFocus: Prefs.MultiKeyboardBrightness == MultiKeyboardBrightness.FocusInsteadOfMouse);
        }

        if (isVolume)
        {
            if (Prefs.MultiKeyboardVolume == MultiKeyboardVolume.AllScreens)
            {
                return _displays;
            }

            if (Prefs.MultiKeyboardVolume == MultiKeyboardVolume.AudioDeviceNameMatching)
            {
                return AudioControlTargetDisplays;
            }

            currentDisplay = GetCurrentDisplay(byFocus: false);
        }

        if (currentDisplay is null)
        {
            return [];
        }

        var affected = new List<Display> { currentDisplay };
        if (currentDisplay.Info.IsInMirrorSet && currentDisplay.Info.MirrorOf == 0)
        {
            affected.AddRange(_displays.Where(d => d.Info.MirrorOf == currentDisplay.Id));
        }

        return affected;
    }

    public bool EngageMirror() => Platform.ToggleMirror();

    /// <summary>For a display mirroring another one, the id of the display actually showing the content.</summary>
    public uint ResolveEffectiveDisplayId(uint displayId) =>
        InfoOf(displayId) is { IsInMirrorSet: true, MirrorOf: not 0 } info ? info.MirrorOf : displayId;

    /// <summary>Restores every display to its original state (used when quitting or crashing).</summary>
    public void RestoreAllForExit()
    {
        ResetSwBrightnessForAllDisplays(noPrefSave: true);
        foreach (var display in _displays)
        {
            display.RestoreDefaultGamma();
        }

        DestroyAllShades();
    }

    public void Dispose() => DdcWorker.Dispose();

    private DisplayInfo? InfoOf(uint displayId) => _displays.FirstOrDefault(d => d.Id == displayId)?.Info;
}
