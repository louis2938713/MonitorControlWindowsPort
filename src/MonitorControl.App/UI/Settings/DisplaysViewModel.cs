using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorControl.App.Localization;
using MonitorControl.App.Platform;
using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Mathematics;

namespace MonitorControl.App.UI.Settings;

/// <summary>Displays settings page. Port of DisplaysPrefsViewController.swift.</summary>
internal sealed partial class DisplaysViewModel : ObservableObject
{
    private readonly SettingsContext _context;

    [ObservableProperty]
    public partial bool ShowAdvanced { get; set; }

    public DisplaysViewModel(SettingsContext context)
    {
        _context = context;
        ShowAdvanced = context.Prefs.Bool(PrefKey.ShowAdvancedSettings);
        LoadDisplayList();
    }

    public ObservableCollection<DisplayItemViewModel> Displays { get; } = [];

    public void LoadDisplayList()
    {
        Core.Log.Info("Reloading Displays settings display list");
        Displays.Clear();
        foreach (var display in _context.DisplayManager.Displays)
        {
            Displays.Add(new DisplayItemViewModel(_context, display, ShowAdvanced));
        }
    }

    partial void OnShowAdvancedChanged(bool value)
    {
        _context.Prefs.Set(PrefKey.ShowAdvancedSettings, value);
        LoadDisplayList();
    }
}

/// <summary>Type, icon and control method of a display (getDisplayInfo).</summary>
internal sealed record DisplayDescription(string Type, string Glyph, string Method, string Status)
{
    public static DisplayDescription Of(Display display)
    {
        var type = Loc.T("Displays.Type.Other");
        var glyph = UI.Icons.Warning;
        var method = Loc.T("Displays.Method.None") + "  ⚠️";
        var status = Loc.T("Displays.Status.Unspecified");
        if (display.IsVirtual && !display.IsDummy)
        {
            type = Loc.T("Displays.Type.Virtual");
            glyph = UI.Icons.Virtual;
            method = Loc.T("Displays.Method.Shade") + "  ⚠️";
            status = Loc.T("Displays.Status.Virtual");
        }
        else if (display is ExternalDisplay external && !display.IsDummy)
        {
            type = Loc.T("Displays.Type.External");
            glyph = UI.Icons.Display;
            var avoidGamma = external.ReadPrefAsBool(PrefKey.AvoidGamma);
            if (external.IsSwOnly())
            {
                method = Loc.T(avoidGamma ? "Displays.Method.Shade" : "Displays.Method.Gamma") + "  ⚠️";
                glyph = UI.Icons.Warning;
                status = Loc.T("Displays.Status.SoftwareOnly");
            }
            else if (external.IsSw())
            {
                method = Loc.T(avoidGamma ? "Displays.Method.ShadeForced" : "Displays.Method.GammaForced");
                status = Loc.T("Displays.Status.SoftwareForced");
            }
            else
            {
                method = Loc.T("Displays.Method.Ddc");
                status = Loc.T("Displays.Status.Ddc");
            }
        }
        else if (!display.IsDummy && display is InternalDisplay)
        {
            type = Loc.T("Displays.Type.BuiltIn");
            glyph = UI.Icons.Laptop;
            method = Loc.T("Displays.Method.Internal");
            status = Loc.T("Displays.Status.Internal");
        }

        return new DisplayDescription(type, glyph, method, status);
    }
}

/// <summary>One display in the Displays settings page. Port of DisplaysPrefsCellView.swift.</summary>
internal sealed partial class DisplayItemViewModel : ObservableObject
{
    // Polling combo box order → setting value.
    private static readonly PollingMode[] PollingOrder = [PollingMode.None, PollingMode.Minimal, PollingMode.Normal, PollingMode.Heavy, PollingMode.Custom];
    private readonly SettingsContext _context;
    private readonly Display _display;
    private readonly ExternalDisplay? _external;
    private bool _loading;

    [ObservableProperty]
    public partial string FriendlyName { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial bool Ddc { get; set; }

    [ObservableProperty]
    public partial bool AvoidGamma { get; set; }

    [ObservableProperty]
    public partial bool HideOsd { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollingCountEnabled))]
    public partial int PollingModeIndex { get; set; }

    [ObservableProperty]
    public partial string PollingCount { get; set; }

    [ObservableProperty]
    public partial bool LongerDelay { get; set; }

    [ObservableProperty]
    public partial bool EnableMute { get; set; }

    [ObservableProperty]
    public partial int SwitchingPoint { get; set; }

    [ObservableProperty]
    public partial string AudioDeviceNameOverride { get; set; }

    [ObservableProperty]
    public partial DisplayDescription Description { get; set; }

    public DisplayItemViewModel(SettingsContext context, Display display, bool showAdvanced)
    {
        _context = context;
        _display = display;
        _external = display as ExternalDisplay;
        ShowAdvanced = showAdvanced;
        FriendlyName = display.FriendlyName;
        PollingCount = string.Empty;
        AudioDeviceNameOverride = string.Empty;
        Description = DisplayDescription.Of(display);
        Rows =
        [
            new DdcCommandRowViewModel(this, Command.Brightness, Loc.T("Menu.Brightness")),
            new DdcCommandRowViewModel(this, Command.AudioSpeakerVolume, Loc.T("Menu.Volume")),
            new DdcCommandRowViewModel(this, Command.Contrast, Loc.T("Menu.Contrast")),
        ];
        Load();
    }

    public Display Display => _display;

    public SettingsContext Context => _context;

    public string Identifier => _display.Id.ToString(CultureInfo.InvariantCulture);

    public bool ShowAdvanced { get; }

    public bool CanUseDdc => _external is { IsVirtual: false } && !_external.IsSwOnly();

    public bool CanAvoidGamma => _external is { IsVirtual: false };

    public bool CanHideOsd => _external is not null && !_external.IsSw();

    /// <summary>Advanced DDC options are available for DDC capable external displays.</summary>
    public bool AdvancedEnabled => _external is not null && !_external.IsSwOnly();

    public bool PollingCountEnabled => AdvancedEnabled && PollingOrder[Math.Clamp(PollingModeIndex, 0, PollingOrder.Length - 1)] == PollingMode.Custom;

    public IReadOnlyList<DdcCommandRowViewModel> Rows { get; }

    public void Load()
    {
        _loading = true;
        Enabled = !_display.ReadPrefAsBool(PrefKey.IsDisabled);
        AvoidGamma = _display.ReadPrefAsBool(PrefKey.AvoidGamma);
        Ddc = !(_external?.IsSw() ?? true);
        HideOsd = CanHideOsd && _external!.ReadPrefAsBool(PrefKey.HideOsd);
        if (AdvancedEnabled)
        {
            PollingModeIndex = Math.Max(0, Array.IndexOf(PollingOrder, (PollingMode)_external!.ReadPrefAsInt(PrefKey.PollingMode)));
            PollingCount = _external.PollingCount.ToString(CultureInfo.InvariantCulture);
            LongerDelay = _external.ReadPrefAsBool(PrefKey.LongerDelay);
            EnableMute = _external.ReadPrefAsBool(PrefKey.EnableMuteUnmute);
            SwitchingPoint = _external.ReadPrefAsInt(PrefKey.CombinedBrightnessSwitchingPoint);
            AudioDeviceNameOverride = _external.ReadPrefAsString(PrefKey.AudioDeviceNameOverride);
        }
        else
        {
            PollingModeIndex = Array.IndexOf(PollingOrder, PollingMode.Normal);
            PollingCount = string.Empty;
            LongerDelay = false;
            EnableMute = false;
            SwitchingPoint = 0;
            AudioDeviceNameOverride = string.Empty;
        }

        foreach (var row in Rows)
        {
            row.Load();
        }

        Description = DisplayDescription.Of(_display);
        _loading = false;
    }

    internal bool IsLoading => _loading;

    internal void Configure() => _context.Controller.Configure();

    partial void OnFriendlyNameChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            FriendlyName = _display.FriendlyName;
            return;
        }

        if (value != _display.FriendlyName)
        {
            _display.SavePref(value.Trim(), PrefKey.FriendlyName);
            _context.Controller.UpdateMenusAndKeys();
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_loading)
        {
            _display.SavePref(!value, PrefKey.IsDisabled);
        }
    }

    partial void OnDdcChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        if (value)
        {
            _display.SetDirectBrightness(1);
        }

        _display.SavePref(!value, PrefKey.ForceSw);
        _display.SetSwBrightness(1);
        _display.SetDirectBrightness(1);
        Description = DisplayDescription.Of(_display);
        Configure();
    }

    partial void OnAvoidGammaChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _display.SetSwBrightness(1);
        _display.SetDirectBrightness(1);
        _display.SavePref(value, PrefKey.AvoidGamma);
        Description = DisplayDescription.Of(_display);
        Configure();
    }

    partial void OnHideOsdChanged(bool value)
    {
        if (!_loading)
        {
            _display.SavePref(value, PrefKey.HideOsd);
        }
    }

    partial void OnPollingModeIndexChanged(int value)
    {
        if (_loading || _external is null)
        {
            return;
        }

        _external.SavePref((int)PollingOrder[Math.Clamp(value, 0, PollingOrder.Length - 1)], PrefKey.PollingMode);
        PollingCount = _external.PollingCount.ToString(CultureInfo.InvariantCulture);
    }

    partial void OnPollingCountChanged(string value)
    {
        if (_loading || _external is null)
        {
            return;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count is >= 0 and <= 100)
        {
            _external.PollingCount = count;
        }
        else
        {
            PollingCount = _external.PollingCount.ToString(CultureInfo.InvariantCulture);
        }
    }

    partial void OnLongerDelayChanged(bool value)
    {
        if (_loading || _external is null)
        {
            return;
        }

        if (value)
        {
            var answer = AlertWindow.Show(Loc.T("Alert.LongerDelay.Title"), Loc.T("Alert.LongerDelay.Message"), Loc.T("Common.Yes"), Loc.T("Common.No"));
            if (answer != 0)
            {
                _loading = true;
                LongerDelay = false;
                _loading = false;
                return;
            }

            StartupRegistration.Set(false);
        }

        _external.SavePref(value, PrefKey.LongerDelay);
    }

    partial void OnEnableMuteChanged(bool value)
    {
        if (_loading || _external is null)
        {
            return;
        }

        // Unmute first so the display does not stay muted once the command is disabled.
        if (!value && _external.ReadPrefAsInt(command: Command.AudioMuteScreenBlank) == 1)
        {
            _external.ToggleMute();
        }

        _external.SavePref(value, PrefKey.EnableMuteUnmute);
    }

    partial void OnSwitchingPointChanged(int value)
    {
        if (!_loading)
        {
            _external?.SavePref(value, PrefKey.CombinedBrightnessSwitchingPoint);
        }
    }

    partial void OnAudioDeviceNameOverrideChanged(string value)
    {
        if (_loading || _external is null)
        {
            return;
        }

        _external.SavePref(value.Trim(), PrefKey.AudioDeviceNameOverride);
        Configure();
    }

    [RelayCommand]
    private void GetCurrentAudioName()
    {
        if (_context.Audio.DefaultOutputName is { } name)
        {
            AudioDeviceNameOverride = name;
        }
    }

    [RelayCommand]
    private void ResetName()
    {
        _display.RemovePref(PrefKey.FriendlyName);
        _loading = true;
        FriendlyName = _display.FriendlyName;
        _loading = false;
        _context.Controller.UpdateMenusAndKeys();
    }

    [RelayCommand]
    private void ResetSettings()
    {
        if (CanUseDdc)
        {
            _display.SetDirectBrightness(1);
            _display.SetSwBrightness(1);
            foreach (var key in new[] { PrefKey.ForceSw, PrefKey.AvoidGamma, PrefKey.HideOsd, PrefKey.PollingMode, PrefKey.PollingCount, PrefKey.LongerDelay, PrefKey.CombinedBrightnessSwitchingPoint, PrefKey.AudioDeviceNameOverride })
            {
                _display.RemovePref(key);
            }

            if (_external!.ReadPrefAsInt(command: Command.AudioMuteScreenBlank) == 1)
            {
                _external.ToggleMute();
            }

            _display.RemovePref(PrefKey.EnableMuteUnmute);
            foreach (var command in new[] { Command.Brightness, Command.AudioSpeakerVolume, Command.Contrast })
            {
                foreach (var key in new[] { PrefKey.UnavailableDdc, PrefKey.MinDdcOverride, PrefKey.MaxDdcOverride, PrefKey.CurveDdc, PrefKey.InvertDdc, PrefKey.RemapDdc })
                {
                    _display.RemovePref(key, command);
                }
            }
        }

        _display.RemovePref(PrefKey.UnavailableDdc, Command.Brightness);
        _display.RemovePref(PrefKey.FriendlyName);
        _display.RemovePref(PrefKey.IsDisabled);
        FriendlyName = _display.FriendlyName;
        Configure();
        Load();
    }
}

/// <summary>Per command (brightness, volume, contrast) advanced DDC options.</summary>
internal sealed partial class DdcCommandRowViewModel(DisplayItemViewModel owner, Command command, string label) : ObservableObject
{
    [ObservableProperty]
    public partial bool Available { get; set; }

    [ObservableProperty]
    public partial string MinDdc { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MaxDdc { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int Curve { get; set; } = 5;

    [ObservableProperty]
    public partial bool Invert { get; set; }

    [ObservableProperty]
    public partial string Remap { get; set; } = string.Empty;

    public string Label { get; } = label;

    public Command Command => command;

    /// <summary>"Available" also applies to the brightness of non-DDC displays; everything else needs DDC.</summary>
    public bool AvailableEnabled => command == Command.Brightness || owner.AdvancedEnabled;

    public bool DdcEnabled => owner.AdvancedEnabled;

    private Display Display => owner.Display;

    public void Load()
    {
        Available = !Display.ReadPrefAsBool(PrefKey.UnavailableDdc, command);
        if (DdcEnabled)
        {
            MinDdc = Display.PrefExists(PrefKey.MinDdcOverride, command) ? Display.ReadPrefAsString(PrefKey.MinDdcOverride, command) : string.Empty;
            MaxDdc = Display.PrefExists(PrefKey.MaxDdcOverride, command) ? Display.ReadPrefAsString(PrefKey.MaxDdcOverride, command) : string.Empty;
            Curve = Display.ReadPrefAsInt(PrefKey.CurveDdc, command) is var curve && curve != 0 ? curve : 5;
            Invert = Display.ReadPrefAsBool(PrefKey.InvertDdc, command);
            Remap = Display.ReadPrefAsString(PrefKey.RemapDdc, command);
        }
        else
        {
            MinDdc = MaxDdc = Remap = string.Empty;
            Curve = 5;
            Invert = false;
        }
    }

    partial void OnAvailableChanged(bool value)
    {
        if (owner.IsLoading)
        {
            return;
        }

        Display.SavePref(!value, PrefKey.UnavailableDdc, command);
        Display.SetDirectBrightness(1);
        Display.SetSwBrightness(1);
        owner.Configure();
    }

    partial void OnMinDdcChanged(string value) => SaveOverride(PrefKey.MinDdcOverride, value, text => MinDdc = text);

    partial void OnMaxDdcChanged(string value) => SaveOverride(PrefKey.MaxDdcOverride, value, text => MaxDdc = text);

    partial void OnCurveChanged(int value)
    {
        if (!owner.IsLoading && DdcEnabled)
        {
            Display.SavePref(Math.Clamp(value, 1, 9), PrefKey.CurveDdc, command);
        }
    }

    partial void OnInvertChanged(bool value)
    {
        if (owner.IsLoading || !DdcEnabled)
        {
            return;
        }

        Display.SavePref(value, PrefKey.InvertDdc, command);
        owner.Configure();
    }

    partial void OnRemapChanged(string value)
    {
        if (owner.IsLoading || !DdcEnabled)
        {
            return;
        }

        var normalized = DdcMath.NormalizeRemapCodes(value);
        Display.SavePref(normalized, PrefKey.RemapDdc, command);
        if (normalized != value)
        {
            Remap = normalized;
        }
    }

    private void SaveOverride(PrefKey key, string value, Action<string> setText)
    {
        if (owner.IsLoading || !DdcEnabled)
        {
            return;
        }

        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 65535)
        {
            Display.SavePref(number, key, command);
        }
        else
        {
            Display.RemovePref(key, command);
        }

        owner.Configure();
        var text = Display.PrefExists(key, command) ? Display.ReadPrefAsString(key, command) : string.Empty;
        if (text != value)
        {
            setText(text);
        }
    }
}
