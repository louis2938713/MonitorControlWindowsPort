using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorControl.App.Localization;
using MonitorControl.App.Platform;
using MonitorControl.Core.Enums;

namespace MonitorControl.App.UI.Settings;

/// <summary>A language choice ("" = follow Windows).</summary>
public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>General settings. Port of MainPrefsViewController.swift.</summary>
internal sealed partial class GeneralViewModel : ObservableObject
{
    private readonly SettingsContext _context;
    private bool _loading;

    [ObservableProperty]
    public partial bool StartAtLogin { get; set; }

    [ObservableProperty]
    public partial bool AutomaticUpdateCheck { get; set; }

    [ObservableProperty]
    public partial LanguageOption? Language { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDoNothing), nameof(IsWrite), nameof(IsRead))]
    public partial int StartupAction { get; set; }

    [ObservableProperty]
    public partial bool CombinedBrightness { get; set; }

    [ObservableProperty]
    public partial bool AllowZeroSwBrightness { get; set; }

    [ObservableProperty]
    public partial bool SmoothBrightness { get; set; }

    [ObservableProperty]
    public partial bool BrightnessSync { get; set; }

    public GeneralViewModel(SettingsContext context)
    {
        _context = context;
        Languages = [new LanguageOption(string.Empty, Loc.T("General.LanguageSystem")), .. Loc.Languages.Select(l => new LanguageOption(l.Code, l.NativeName))];
        Load();
    }

    /// <summary>Raised after a reset so every page reloads.</summary>
    public event EventHandler? SettingsReset;

    public IReadOnlyList<LanguageOption> Languages { get; }

    public bool IsDoNothing => StartupAction == (int)Core.Enums.StartupAction.DoNothing;

    public bool IsWrite => StartupAction == (int)Core.Enums.StartupAction.Write;

    public bool IsRead => StartupAction == (int)Core.Enums.StartupAction.Read;

    public void Load()
    {
        _loading = true;
        var prefs = _context.Prefs;
        StartAtLogin = StartupRegistration.IsEnabled;
        AutomaticUpdateCheck = prefs.Bool(PrefKey.EnableAutomaticUpdateChecks);
        Language = Languages.FirstOrDefault(l => l.Code == prefs.String(PrefKey.Language)) ?? Languages[0];
        StartupAction = prefs.Int(PrefKey.StartupAction);
        CombinedBrightness = !prefs.Bool(PrefKey.DisableCombinedBrightness);
        AllowZeroSwBrightness = prefs.Bool(PrefKey.AllowZeroSwBrightness);
        SmoothBrightness = !prefs.Bool(PrefKey.DisableSmoothBrightness);
        BrightnessSync = prefs.Bool(PrefKey.EnableBrightnessSync);
        _loading = false;
    }

    partial void OnStartAtLoginChanged(bool value)
    {
        if (!_loading)
        {
            StartupRegistration.Set(value);
        }
    }

    partial void OnAutomaticUpdateCheckChanged(bool value)
    {
        if (!_loading)
        {
            _context.Prefs.Set(PrefKey.EnableAutomaticUpdateChecks, value);
        }
    }

    partial void OnLanguageChanged(LanguageOption? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        _context.Prefs.Set(PrefKey.Language, value.Code);
        Loc.SetLanguage(value.Code);
        _context.Controller.UpdateMenusAndKeys();
    }

    partial void OnStartupActionChanged(int value)
    {
        if (!_loading)
        {
            _context.Prefs.Set(PrefKey.StartupAction, value);
        }
    }

    partial void OnCombinedBrightnessChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        foreach (var display in _context.DisplayManager.GetDdcCapableDisplays())
        {
            display.SetDirectBrightness(1);
        }

        _context.DisplayManager.ResetSwBrightnessForAllDisplays(async: false);
        _context.Prefs.Set(PrefKey.DisableCombinedBrightness, !value);
        _context.Controller.Configure();
    }

    partial void OnAllowZeroSwBrightnessChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _context.Prefs.Set(PrefKey.AllowZeroSwBrightness, value);
        foreach (var display in _context.DisplayManager.GetExternalDisplays())
        {
            display.SetDirectBrightness(1);
            display.SetSwBrightness(1);
        }

        _context.Controller.Configure();
    }

    partial void OnSmoothBrightnessChanged(bool value)
    {
        if (!_loading)
        {
            _context.Prefs.Set(PrefKey.DisableSmoothBrightness, !value);
        }
    }

    partial void OnBrightnessSyncChanged(bool value)
    {
        if (!_loading)
        {
            _context.Prefs.Set(PrefKey.EnableBrightnessSync, value);
        }
    }

    [RelayCommand]
    private void ResetSettings()
    {
        var answer = AlertWindow.Show(Loc.T("Alert.ResetSettings.Title"), Loc.T("Alert.ResetSettings.Message"), Loc.T("Common.Yes"), Loc.T("Common.No"));
        if (answer != 0)
        {
            return;
        }

        _context.Controller.SettingsReset();
        Loc.SetLanguage(_context.Prefs.String(PrefKey.Language));
        Load();
        SettingsReset?.Invoke(this, EventArgs.Empty);
    }
}
