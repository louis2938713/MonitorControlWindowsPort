using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorControl.Core.Enums;

namespace MonitorControl.App.UI.Settings;

/// <summary>App menu (flyout) settings. Port of MenuslidersPrefsViewController.swift.</summary>
internal sealed partial class AppMenuViewModel : ObservableObject
{
    // Combo box order → setting value.
    private static readonly MenuIcon[] MenuIconOrder = [MenuIcon.Show, MenuIcon.SliderOnly, MenuIcon.ExternalOnly, MenuIcon.Hide];
    private readonly SettingsContext _context;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuitRow))]
    public partial int MenuIconIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuitRow))]
    public partial int MenuItemStyle { get; set; }

    [ObservableProperty]
    public partial bool ShowBrightness { get; set; }

    [ObservableProperty]
    public partial bool ShowBuiltIn { get; set; }

    [ObservableProperty]
    public partial bool ShowVolume { get; set; }

    [ObservableProperty]
    public partial bool ShowContrast { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCombine))]
    public partial int MultiSliders { get; set; }

    [ObservableProperty]
    public partial bool SliderSnap { get; set; }

    [ObservableProperty]
    public partial bool TickMarks { get; set; }

    [ObservableProperty]
    public partial bool Percentages { get; set; }

    public AppMenuViewModel(SettingsContext context)
    {
        _context = context;
        Load();
    }

    /// <summary>The quit button is offered when the flyout may not be reachable.</summary>
    public bool ShowQuitRow => MenuIconOrder[Math.Clamp(MenuIconIndex, 0, MenuIconOrder.Length - 1)] != MenuIcon.Show || MenuItemStyle == (int)Core.Enums.MenuItemStyle.Hide;

    public bool IsCombine => MultiSliders == (int)Core.Enums.MultiSliders.Combine;

    public void Load()
    {
        _loading = true;
        var prefs = _context.Prefs;
        MenuIconIndex = Math.Max(0, Array.IndexOf(MenuIconOrder, prefs.MenuIcon));
        MenuItemStyle = prefs.Int(PrefKey.MenuItemStyle);
        ShowBrightness = !prefs.Bool(PrefKey.HideBrightness);
        ShowBuiltIn = !prefs.Bool(PrefKey.HideBuiltInFromMenu);
        ShowVolume = !prefs.Bool(PrefKey.HideVolume);
        ShowContrast = prefs.Bool(PrefKey.ShowContrast);
        MultiSliders = prefs.Int(PrefKey.MultiSliders);
        SliderSnap = prefs.Bool(PrefKey.EnableSliderSnap);
        TickMarks = prefs.Bool(PrefKey.ShowTickMarks);
        Percentages = prefs.Bool(PrefKey.EnableSliderPercent);
        _loading = false;
    }

    partial void OnMenuIconIndexChanged(int value) => Save(() => _context.Prefs.Set(PrefKey.MenuIcon, MenuIconOrder[Math.Clamp(value, 0, MenuIconOrder.Length - 1)]));

    partial void OnMenuItemStyleChanged(int value) => Save(() => _context.Prefs.Set(PrefKey.MenuItemStyle, value));

    partial void OnShowBrightnessChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.HideBrightness, !value));

    partial void OnShowBuiltInChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.HideBuiltInFromMenu, !value));

    partial void OnShowVolumeChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.HideVolume, !value));

    partial void OnShowContrastChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.ShowContrast, value));

    partial void OnMultiSlidersChanged(int value) => Save(() => _context.Prefs.Set(PrefKey.MultiSliders, value));

    partial void OnSliderSnapChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.EnableSliderSnap, value));

    partial void OnTickMarksChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.ShowTickMarks, value));

    partial void OnPercentagesChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.EnableSliderPercent, value));

    [RelayCommand]
    private void Quit() => _context.Quit();

    private void Save(Action save)
    {
        if (_loading)
        {
            return;
        }

        save();
        _context.Controller.UpdateMenusAndKeys();
    }
}
