using System.Text;
using MonitorControl.Core.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace MonitorControl.App.Localization;

/// <summary>Human-readable names for shortcuts ("Ctrl+Alt+↑") and shortcut actions.</summary>
internal static class ShortcutNames
{
    public static string Display(ShortcutName name) => name switch
    {
        ShortcutName.BrightnessUp => Loc.T("Keyboard.ShortcutBrightness") + " " + Loc.T("Keyboard.Increase"),
        ShortcutName.BrightnessDown => Loc.T("Keyboard.ShortcutBrightness") + " " + Loc.T("Keyboard.Decrease"),
        ShortcutName.ContrastUp => Loc.T("Keyboard.ShortcutContrast") + " " + Loc.T("Keyboard.Increase"),
        ShortcutName.ContrastDown => Loc.T("Keyboard.ShortcutContrast") + " " + Loc.T("Keyboard.Decrease"),
        ShortcutName.VolumeUp => Loc.T("Keyboard.ShortcutVolume") + " " + Loc.T("Keyboard.Increase"),
        ShortcutName.VolumeDown => Loc.T("Keyboard.ShortcutVolume") + " " + Loc.T("Keyboard.Decrease"),
        _ => Loc.T("Keyboard.Mute"),
    };

    public static string Display(Shortcut shortcut)
    {
        var text = new StringBuilder();
        if (shortcut.Modifiers.HasFlag(KeyModifiers.Control))
        {
            text.Append(KeyName(0x11, "Ctrl")).Append('+');
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            text.Append(KeyName(0x12, "Alt")).Append('+');
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            text.Append(KeyName(0x10, "Shift")).Append('+');
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Win))
        {
            text.Append("Win+");
        }

        text.Append(KeyName(shortcut.VirtualKey, "?"));
        return text.ToString();
    }

    private static string KeyName(int vk, string fallback)
    {
        switch (vk)
        {
            case 0x25: return "←";
            case 0x26: return "↑";
            case 0x27: return "→";
            case 0x28: return "↓";
            case >= 0x70 and <= 0x87: return "F" + (vk - 0x6F).ToString(System.Globalization.CultureInfo.InvariantCulture);
            default: break;
        }

        // Name from the active keyboard layout (localized: "Maj", "Strg"…).
        var scanCode = PInvoke.MapVirtualKey((uint)vk, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);
        if (scanCode == 0)
        {
            return fallback;
        }

        var extended = vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x2D or 0x2E or 0x6F or 0x90;
        var lParam = (int)((scanCode << 16) | (extended ? 1u << 24 : 0));
        Span<char> buffer = stackalloc char[64];
        var length = PInvoke.GetKeyNameText(lParam, buffer);
        return length > 0 ? new string(buffer[..length]) : fallback;
    }
}
