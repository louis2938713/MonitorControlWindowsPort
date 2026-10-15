using MonitorControl.Core.Enums;

namespace MonitorControl.Core.Settings;

/// <summary>Typed access to app-wide settings (equivalent of <c>prefs</c> + <c>PrefKey</c> in the macOS app).</summary>
public sealed class Prefs(ISettingsStore store)
{
    public ISettingsStore Store { get; } = store;

    public bool Contains(PrefKey key) => Store.Contains(Name(key));

    public bool Bool(PrefKey key) => Store.GetBool(Name(key));

    public int Int(PrefKey key) => Store.GetInt(Name(key));

    public float Float(PrefKey key) => Store.GetFloat(Name(key));

    public string String(PrefKey key) => Store.GetString(Name(key));

    public void Set(PrefKey key, bool value) => Store.Set(Name(key), value);

    public void Set(PrefKey key, int value) => Store.Set(Name(key), value);

    public void Set(PrefKey key, string value) => Store.Set(Name(key), value);

    public void Set<TEnum>(PrefKey key, TEnum value)
        where TEnum : struct, Enum => Store.Set(Name(key), Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));

    public static string Name(PrefKey key) => key.ToString();

    public MenuIcon MenuIcon => (MenuIcon)Int(PrefKey.MenuIcon);

    public MenuItemStyle MenuItemStyle => (MenuItemStyle)Int(PrefKey.MenuItemStyle);

    public KeyboardBrightness KeyboardBrightness => (KeyboardBrightness)Int(PrefKey.KeyboardBrightness);

    public KeyboardVolume KeyboardVolume => (KeyboardVolume)Int(PrefKey.KeyboardVolume);

    public MultiKeyboardBrightness MultiKeyboardBrightness => (MultiKeyboardBrightness)Int(PrefKey.MultiKeyboardBrightness);

    public MultiKeyboardVolume MultiKeyboardVolume => (MultiKeyboardVolume)Int(PrefKey.MultiKeyboardVolume);

    public MultiSliders MultiSliders => (MultiSliders)Int(PrefKey.MultiSliders);

    public StartupAction StartupAction => (StartupAction)Int(PrefKey.StartupAction);

    /// <summary>Media (standard) brightness keys are listened for.</summary>
    public bool MediaBrightnessKeysEnabled => KeyboardBrightness is KeyboardBrightness.Media or KeyboardBrightness.Both;

    /// <summary>Media (standard) volume keys are listened for.</summary>
    public bool MediaVolumeKeysEnabled => KeyboardVolume is KeyboardVolume.Media or KeyboardVolume.Both;

    /// <summary>Custom brightness/contrast shortcuts are enabled.</summary>
    public bool CustomBrightnessKeysEnabled => KeyboardBrightness is KeyboardBrightness.Custom or KeyboardBrightness.Both;

    /// <summary>Custom volume shortcuts are enabled.</summary>
    public bool CustomVolumeKeysEnabled => KeyboardVolume is KeyboardVolume.Custom or KeyboardVolume.Both;

    public bool CombinedBrightnessEnabled => !Bool(PrefKey.DisableCombinedBrightness);
}
