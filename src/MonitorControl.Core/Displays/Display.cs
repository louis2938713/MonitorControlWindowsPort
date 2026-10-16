using System.Globalization;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Mathematics;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;

namespace MonitorControl.Core.Displays;

/// <summary>
/// Base display: per-display settings, software brightness (gamma table or shade) and smooth brightness transitions.
/// Ported from Display.swift.
/// </summary>
public class Display : IEquatable<Display>
{
    private readonly object _swBrightnessLock = new();
    private GammaTable _defaultGammaTable = GammaTable.Identity();
    private float _defaultGammaTablePeak = 1;
    private bool _gammaRefused;

    protected Display(DisplayInfo info, DisplayManager manager)
    {
        Info = info;
        Manager = manager;
        Id = info.Id;
        Name = info.Name;
        IsVirtual = info.IsVirtual;
        IsDummy = info.IsDummy;
        var compactName = new string(info.Name.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var suffix = IsVirtual ? (info.SerialNumber ?? 9999).ToString(CultureInfo.InvariantCulture) : Id.ToString(CultureInfo.InvariantCulture);
        PrefsId = $"({compactName}{info.VendorNumber ?? 0}{info.ModelNumber ?? 0}@{suffix})";
    }

    public uint Id { get; }

    public string PrefsId { get; }

    public string Name { get; set; }

    public DisplayInfo Info { get; }

    public bool IsVirtual { get; }

    public bool IsDummy { get; protected set; }

    public float SmoothBrightnessTransient { get; set; } = 1;

    public bool SmoothBrightnessRunning { get; set; }

    public bool SmoothBrightnessSlow { get; set; }

    public float BrightnessSyncSourceValue { get; set; } = 1;

    public Dictionary<Command, ISliderHandle> SliderHandler { get; } = [];

    /// <summary>
    /// Translates generic display names ("Built-in Display", "Unknown") for the UI. The untranslated name stays the
    /// settings identifier so changing the language does not lose per-display settings.
    /// </summary>
    public static Func<string, string> NameLocalizer { get; set; } = name => name;

    /// <summary>Name shown to the user: the friendly name override, or the (localized) display name.</summary>
    public string FriendlyName => ReadPrefAsString(PrefKey.FriendlyName) is { Length: > 0 } friendly ? friendly : NameLocalizer(Name);

    protected DisplayManager Manager { get; }

    protected Prefs Prefs => Manager.Prefs;

    protected AppState State => Manager.State;

    protected IGammaController? Gamma { get; private set; }

    /// <summary>Software dimming uses a shade instead of the gamma table.</summary>
    protected bool UsesShade => IsVirtual || ReadPrefAsBool(PrefKey.AvoidGamma) || Info.IsHdr || _gammaRefused || Gamma is null;

    public bool IsBuiltIn => Info.IsBuiltIn;

    /// <summary>Second-stage initialization (virtual calls are not safe in constructors).</summary>
    public virtual void Initialize()
    {
        Gamma = IsVirtual ? null : Manager.Platform.OpenGamma(Id);
        Log.Info($"Display init with prefsIdentifier {PrefsId}");
        SwUpdateDefaultGammaTable();
        SmoothBrightnessTransient = GetBrightness();
        if (UsesShade && !IsDummy)
        {
            Log.Info($"Creating or updating shade for display {Id}");
            Manager.UpdateShade(Id);
        }
        else
        {
            Log.Info($"Destroying shade (if exists) for display {Id}");
            Manager.DestroyShade(Id);
        }

        BrightnessSyncSourceValue = GetBrightness();
    }

    // MARK: - Settings

    public bool PrefExists(PrefKey? key = null, Command? command = null) => Prefs.Store.Contains(GetKey(key, command));

    public void RemovePref(PrefKey key, Command? command = null) => Prefs.Store.Remove(GetKey(key, command));

    public void SavePref(bool value, PrefKey? key = null, Command? command = null) => Prefs.Store.Set(GetKey(key, command), value);

    public void SavePref(int value, PrefKey? key = null, Command? command = null) => Prefs.Store.Set(GetKey(key, command), value);

    public void SavePref(float value, PrefKey? key = null, Command? command = null) => Prefs.Store.Set(GetKey(key, command), value);

    public void SavePref(string value, PrefKey? key = null, Command? command = null) => Prefs.Store.Set(GetKey(key, command), value);

    public float ReadPrefAsFloat(PrefKey? key = null, Command? command = null) => Prefs.Store.GetFloat(GetKey(key, command));

    public int ReadPrefAsInt(PrefKey? key = null, Command? command = null) => Prefs.Store.GetInt(GetKey(key, command));

    public bool ReadPrefAsBool(PrefKey? key = null, Command? command = null) => Prefs.Store.GetBool(GetKey(key, command));

    public string ReadPrefAsString(PrefKey? key = null, Command? command = null) => Prefs.Store.GetString(GetKey(key, command));

    private string GetKey(PrefKey? key, Command? command) =>
        Prefs.Name(key ?? PrefKey.Value)
        + (command is { } c ? ((byte)c).ToString(CultureInfo.InvariantCulture) : string.Empty)
        + PrefsId;

    // MARK: - Brightness

    public float CalcNewBrightness(bool isUp, bool isSmallIncrement) => StepMath.CalcNewBrightness(GetBrightness(), isUp, isSmallIncrement);

    /// <summary>True when Windows already shows its own brightness OSD for this display's brightness changes.</summary>
    protected virtual bool HasNativeBrightnessOsd => false;

    public virtual void StepBrightness(bool isUp, bool isSmallIncrement)
    {
        if (ReadPrefAsBool(PrefKey.UnavailableDdc, Command.Brightness))
        {
            return;
        }

        var value = CalcNewBrightness(isUp, isSmallIncrement);
        if (SetBrightness(value))
        {
            if (!HasNativeBrightnessOsd)
            {
                Manager.Osd.Show(Id, Command.Brightness, value * 64, maxValue: 64);
            }

            if (SliderHandler.TryGetValue(Command.Brightness, out var slider))
            {
                slider.SetValue(value, Id);
                BrightnessSyncSourceValue = value;
            }
        }
    }

    public virtual bool SetBrightness(float to = -1, bool slow = false) =>
        !Prefs.Bool(PrefKey.DisableSmoothBrightness) ? SetSmoothBrightness(to, slow) : SetDirectBrightness(to);

    public bool SetSmoothBrightness(float to = -1, bool slow = false)
    {
        if (!State.IsActive)
        {
            SavePref(SmoothBrightnessTransient, command: Command.Brightness);
            SmoothBrightnessRunning = false;
            Log.Info($"Pushing brightness stopped for Display {Id} because of sleep or reconfiguration");
            return false;
        }

        if (slow)
        {
            SmoothBrightnessSlow = true;
        }

        var stepDivider = SmoothBrightnessSlow ? 16f : 6f;
        var dontPushAgain = false;
        if (to != -1)
        {
            var value = Math.Clamp(to, 0, 1);
            SavePref(value, command: Command.Brightness);
            BrightnessSyncSourceValue = value;
            SmoothBrightnessSlow = slow;
            if (SmoothBrightnessRunning)
            {
                return true;
            }
        }

        var brightness = ReadPrefAsFloat(command: Command.Brightness);
        if (brightness != SmoothBrightnessTransient)
        {
            if (Math.Abs(brightness - SmoothBrightnessTransient) < 0.01f)
            {
                SmoothBrightnessTransient = brightness;
                dontPushAgain = true;
                SmoothBrightnessRunning = false;
            }
            else if (brightness > SmoothBrightnessTransient)
            {
                SmoothBrightnessTransient += Math.Max((brightness - SmoothBrightnessTransient) / stepDivider, 1f / 100);
            }
            else
            {
                SmoothBrightnessTransient += Math.Min((brightness - SmoothBrightnessTransient) / stepDivider, 1f / 100);
            }

            SetDirectBrightness(SmoothBrightnessTransient, transient: true);
            if (!dontPushAgain)
            {
                SmoothBrightnessRunning = true;
                Manager.Ui.PostDelayed(TimeSpan.FromMilliseconds(20), () => SetSmoothBrightness());
            }
        }
        else
        {
            SetDirectBrightness(SmoothBrightnessTransient, transient: true);
            SmoothBrightnessRunning = false;
        }

        return true;
    }

    public virtual bool SetDirectBrightness(float to, bool transient = false)
    {
        var value = Math.Clamp(to, 0, 1);
        if (SetSwBrightness(value))
        {
            if (!transient)
            {
                SavePref(value, command: Command.Brightness);
                BrightnessSyncSourceValue = value;
                SmoothBrightnessTransient = value;
            }

            return true;
        }

        return false;
    }

    public virtual float GetBrightness() =>
        PrefExists(command: Command.Brightness) ? ReadPrefAsFloat(command: Command.Brightness) : GetSwBrightness();

    /// <summary>Delta of an externally caused brightness change since the last check (used for syncing).</summary>
    public virtual float RefreshBrightness() => 0;

    // MARK: - Software brightness

    public void SwUpdateDefaultGammaTable()
    {
        if (IsDummy)
        {
            return;
        }

        var table = Gamma?.Read();
        // A ramp that is already dimmed (e.g. left over after a crash) must not become the reference.
        _defaultGammaTable = table is not null && table.Peak >= 0.9f ? table : GammaTable.Identity();
        _defaultGammaTablePeak = _defaultGammaTable.Peak;
    }

    public float SwBrightnessTransform(float value, bool reverse = false) =>
        DdcMath.SwBrightnessTransform(value, Prefs.Bool(PrefKey.AllowZeroSwBrightness), reverse);

    public bool SetSwBrightness(float value, bool smooth = false, bool noPrefSave = false)
    {
        var brightnessValue = Math.Min(1, value);
        float currentValue;
        lock (_swBrightnessLock)
        {
            currentValue = ReadPrefAsFloat(PrefKey.SwBrightness);
            if (!noPrefSave)
            {
                SavePref(brightnessValue, PrefKey.SwBrightness);
            }
        }

        if (IsDummy)
        {
            return true;
        }

        currentValue = SwBrightnessTransform(currentValue);
        var newValue = SwBrightnessTransform(brightnessValue);
        if (smooth)
        {
            var reconfigureId = State.ReconfigureId;
            Task.Run(() =>
            {
                var step = 0.005f * (currentValue > newValue ? -1 : 1);
                for (var transient = currentValue; currentValue > newValue ? transient > newValue : transient < newValue; transient += step)
                {
                    if (State.ReconfigureId != reconfigureId)
                    {
                        return;
                    }

                    ApplySwFactor(transient);
                    Thread.Sleep(1);
                }

                ApplySwFactor(newValue);
            });
            return true;
        }

        return ApplySwFactor(newValue);
    }

    public float GetSwBrightness()
    {
        if (IsDummy)
        {
            return PrefExists(PrefKey.SwBrightness) ? ReadPrefAsFloat(PrefKey.SwBrightness) : 1;
        }

        lock (_swBrightnessLock)
        {
            if (UsesShade)
            {
                var effectiveId = Manager.ResolveEffectiveDisplayId(Id);
                if (Manager.IsDisqualifiedFromShade(effectiveId))
                {
                    return PrefExists(PrefKey.SwBrightness) ? ReadPrefAsFloat(PrefKey.SwBrightness) : 1;
                }

                var raw = 1 - (Manager.GetShadeAlpha(effectiveId) ?? 0);
                return SwBrightnessTransform(raw, reverse: true);
            }

            var table = Gamma?.Read();
            if (table is null || _defaultGammaTablePeak <= 0)
            {
                return 1;
            }

            var peakRatio = table.Peak / _defaultGammaTablePeak;
            return MathF.Round(SwBrightnessTransform(peakRatio, reverse: true) * 256, MidpointRounding.AwayFromZero) / 256;
        }
    }

    public void CheckGammaInterference()
    {
        if (IsDummy || Manager.GammaInterferenceWarningShown || Prefs.Bool(PrefKey.DisableCombinedBrightness) || UsesShade
            || SmoothBrightnessRunning || !PrefExists(PrefKey.SwBrightness))
        {
            return;
        }

        var currentSwBrightness = GetSwBrightness();
        if (Math.Abs(currentSwBrightness - ReadPrefAsFloat(PrefKey.SwBrightness)) <= 0.02f)
        {
            return;
        }

        Manager.GammaInterferenceCounter += 1;
        SetSwBrightness(1);
        Log.Info($"Gamma table interference detected, number of events: {Manager.GammaInterferenceCounter}");
        if (Manager.GammaInterferenceCounter < 3)
        {
            return;
        }

        Manager.GammaInterferenceWarningShown = true;
        if (Manager.Alerts.AskDisableGammaAfterInterference())
        {
            foreach (var otherDisplay in Manager.GetExternalDisplays())
            {
                otherDisplay.SetSwBrightness(1);
                otherDisplay.SetDirectBrightness(1);
                otherDisplay.SavePref(true, PrefKey.AvoidGamma);
                otherDisplay.SetSwBrightness(1);
            }

            Manager.GammaInterferenceWarningShown = false;
            Manager.GammaInterferenceCounter = 0;
            Manager.NotifyDisplaySettingsChanged();
        }
        else
        {
            Log.Info("We won't watch for gamma table interference anymore");
        }
    }

    public bool ResetSwBrightness() => SetSwBrightness(1);

    public bool IsSwBrightnessNotDefault() => !IsVirtual && !IsDummy && GetSwBrightness() < 1;

    /// <summary>Puts the original gamma ramp back (used when quitting).</summary>
    public void RestoreDefaultGamma()
    {
        if (!IsDummy && Gamma is not null && !_gammaRefused)
        {
            Gamma.Write(_defaultGammaTable);
        }
    }

    private bool ApplySwFactor(float factor)
    {
        if (!UsesShade)
        {
            lock (_swBrightnessLock)
            {
                if (Gamma!.Write(_defaultGammaTable.Scaled(factor)))
                {
                    return true;
                }
            }

            // Windows refused the ramp (limited gamma range, HDR…): fall back to a shade for this display.
            Log.Info($"Gamma ramp refused for display {Id}, switching to shade");
            _gammaRefused = true;
            Gamma.Write(_defaultGammaTable);
            Manager.UpdateShade(Id);
        }

        return Manager.SetShadeAlpha(1 - factor, Manager.ResolveEffectiveDisplayId(Id));
    }

    public bool Equals(Display? other) => other is not null && other.Id == Id;

    public override bool Equals(object? obj) => Equals(obj as Display);

    public override int GetHashCode() => Id.GetHashCode();
}
