using CommunityToolkit.Mvvm.ComponentModel;
using MonitorControl.Core.Enums;

namespace MonitorControl.App.UI.Settings;

/// <summary>Keyboard settings. Port of KeyboardPrefsViewController.swift.</summary>
internal sealed partial class KeyboardViewModel : ObservableObject
{
    private readonly SettingsContext _context;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMediaBrightnessText), nameof(ShowAltKeysOption), nameof(ShowBrightnessShortcuts), nameof(BrightnessKeysEnabled))]
    public partial int KeyboardBrightness { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVolumeShortcuts), nameof(VolumeKeysEnabled))]
    public partial int KeyboardVolume { get; set; }

    [ObservableProperty]
    public partial bool DisableAltBrightnessKeys { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFocusText))]
    public partial int MultiKeyboardBrightness { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAudioNameText), nameof(ShowAudioMouseText))]
    public partial int MultiKeyboardVolume { get; set; }

    [ObservableProperty]
    public partial bool UseFineScaleBrightness { get; set; }

    [ObservableProperty]
    public partial bool UseFineScaleVolume { get; set; }

    [ObservableProperty]
    public partial bool SeparateCombinedScale { get; set; }

    [ObservableProperty]
    public partial bool PlayVolumeFeedback { get; set; }

    public KeyboardViewModel(SettingsContext context)
    {
        _context = context;
        Load();
    }

    public SettingsContext Context => _context;

    public bool ShowMediaBrightnessText => KeyboardBrightness == (int)Core.Enums.KeyboardBrightness.Media;

    public bool ShowAltKeysOption => KeyboardBrightness is (int)Core.Enums.KeyboardBrightness.Media or (int)Core.Enums.KeyboardBrightness.Both;

    public bool ShowBrightnessShortcuts => KeyboardBrightness is (int)Core.Enums.KeyboardBrightness.Custom or (int)Core.Enums.KeyboardBrightness.Both;

    public bool BrightnessKeysEnabled => KeyboardBrightness != (int)Core.Enums.KeyboardBrightness.Disabled;

    public bool ShowVolumeShortcuts => KeyboardVolume is (int)Core.Enums.KeyboardVolume.Custom or (int)Core.Enums.KeyboardVolume.Both;

    public bool VolumeKeysEnabled => KeyboardVolume != (int)Core.Enums.KeyboardVolume.Disabled;

    public bool ShowFocusText => MultiKeyboardBrightness == (int)Core.Enums.MultiKeyboardBrightness.FocusInsteadOfMouse;

    public bool ShowAudioNameText => MultiKeyboardVolume == (int)Core.Enums.MultiKeyboardVolume.AudioDeviceNameMatching;

    public bool ShowAudioMouseText => !ShowAudioNameText;

    public void Load()
    {
        _loading = true;
        var prefs = _context.Prefs;
        KeyboardBrightness = prefs.Int(PrefKey.KeyboardBrightness);
        KeyboardVolume = prefs.Int(PrefKey.KeyboardVolume);
        DisableAltBrightnessKeys = prefs.Bool(PrefKey.DisableAltBrightnessKeys);
        MultiKeyboardBrightness = prefs.Int(PrefKey.MultiKeyboardBrightness);
        MultiKeyboardVolume = prefs.Int(PrefKey.MultiKeyboardVolume);
        UseFineScaleBrightness = prefs.Bool(PrefKey.UseFineScaleBrightness);
        UseFineScaleVolume = prefs.Bool(PrefKey.UseFineScaleVolume);
        SeparateCombinedScale = prefs.Bool(PrefKey.SeparateCombinedScale);
        PlayVolumeFeedback = prefs.Bool(PrefKey.PlayVolumeFeedback);
        _loading = false;
    }

    partial void OnKeyboardBrightnessChanged(int value) => Save(() => _context.Prefs.Set(PrefKey.KeyboardBrightness, value), menus: true);

    partial void OnKeyboardVolumeChanged(int value) => Save(() => _context.Prefs.Set(PrefKey.KeyboardVolume, value), menus: true);

    partial void OnDisableAltBrightnessKeysChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.DisableAltBrightnessKeys, value));

    partial void OnMultiKeyboardBrightnessChanged(int value) => Save(() => _context.Prefs.Set(PrefKey.MultiKeyboardBrightness, value));

    partial void OnMultiKeyboardVolumeChanged(int value) => Save(() => _context.Prefs.Set(PrefKey.MultiKeyboardVolume, value));

    partial void OnUseFineScaleBrightnessChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.UseFineScaleBrightness, value));

    partial void OnUseFineScaleVolumeChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.UseFineScaleVolume, value));

    partial void OnSeparateCombinedScaleChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.SeparateCombinedScale, value));

    partial void OnPlayVolumeFeedbackChanged(bool value) => Save(() => _context.Prefs.Set(PrefKey.PlayVolumeFeedback, value));

    private void Save(Action save, bool menus = false)
    {
        if (_loading)
        {
            return;
        }

        save();
        if (menus)
        {
            _context.Controller.UpdateMenusAndKeys();
        }
        else
        {
            _context.Keyboard.Update();
        }
    }
}
