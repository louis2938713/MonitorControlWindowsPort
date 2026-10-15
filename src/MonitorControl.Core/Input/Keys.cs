using System.Globalization;

namespace MonitorControl.Core.Input;

/// <summary>Standard keys MonitorControl listens for (equivalent of MediaKeyTap's MediaKey).</summary>
public enum MediaKey
{
    BrightnessUp,
    BrightnessDown,
    VolumeUp,
    VolumeDown,
    Mute,
}

/// <summary>Modifier keys. On Windows: Control ↔ Ctrl, Option ↔ Alt, Command ↔ Win.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>Named custom shortcuts (KeyboardShortcuts.Name).</summary>
public enum ShortcutName
{
    BrightnessUp,
    BrightnessDown,
    ContrastUp,
    ContrastDown,
    VolumeUp,
    VolumeDown,
    Mute,
}

/// <summary>A key combination: a virtual key code plus modifiers.</summary>
public readonly record struct Shortcut(int VirtualKey, KeyModifiers Modifiers)
{
    // Virtual key codes used for the defaults.
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkM = 0x4D;

    /// <summary>Universal defaults that exist on every keyboard.</summary>
    public static IReadOnlyDictionary<ShortcutName, Shortcut> Defaults { get; } = new Dictionary<ShortcutName, Shortcut>
    {
        [ShortcutName.BrightnessUp] = new(VkUp, KeyModifiers.Control | KeyModifiers.Alt),
        [ShortcutName.BrightnessDown] = new(VkDown, KeyModifiers.Control | KeyModifiers.Alt),
        [ShortcutName.ContrastUp] = new(VkUp, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift),
        [ShortcutName.ContrastDown] = new(VkDown, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift),
        [ShortcutName.VolumeUp] = new(VkRight, KeyModifiers.Control | KeyModifiers.Alt),
        [ShortcutName.VolumeDown] = new(VkLeft, KeyModifiers.Control | KeyModifiers.Alt),
        [ShortcutName.Mute] = new(VkM, KeyModifiers.Control | KeyModifiers.Alt),
    };

    /// <summary>Serialized form stored in settings: "modifiers,virtualKey".</summary>
    public string Serialize() => string.Create(CultureInfo.InvariantCulture, $"{(int)Modifiers},{VirtualKey}");

    public static Shortcut? Parse(string? text)
    {
        var parts = text?.Split(',');
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var modifiers)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var key)
            || key is <= 0 or > 0xFE)
        {
            return null;
        }

        return new Shortcut(key, (KeyModifiers)modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win));
    }

    /// <summary>F1–F24 may be used without modifiers; other keys need at least one modifier besides Shift.</summary>
    public bool IsValid => VirtualKey is >= 0x70 and <= 0x87 || (Modifiers & ~KeyModifiers.Shift) != KeyModifiers.None;
}
