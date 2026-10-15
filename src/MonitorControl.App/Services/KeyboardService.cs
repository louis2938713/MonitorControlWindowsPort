using System.Diagnostics;
using MonitorControl.App.Platform;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Input;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MonitorControl.App.Services;

/// <summary>
/// Keyboard input: standard keys (volume/mute, Scroll Lock/Pause, HID brightness keys) and custom shortcuts,
/// plus shortcut recording for the settings page.
/// </summary>
internal sealed class KeyboardService : IKeyboardShell, IDisposable
{
    private const int VkVolumeMute = 0xAD;
    private const int VkVolumeDown = 0xAE;
    private const int VkVolumeUp = 0xAF;
    private const int VkScrollLock = 0x91;
    private const int VkPause = 0x13;
    private const int VkF14 = 0x7D;
    private const int VkF15 = 0x7E;

    private readonly Prefs _prefs;
    private readonly IUiDispatcher _ui;
    private readonly MessageWindow _window;
    private readonly KeyboardHook _hook;
    private readonly RawInputBrightnessKeys _rawInput;
    private readonly Dictionary<int, ShortcutName> _engaged = [];
    private readonly HashSet<int> _swallowedDown = [];
    private Dictionary<ShortcutName, Shortcut> _shortcuts = [];
    private Action<KeyEvent>? _recorder;

    public KeyboardService(Prefs prefs, IUiDispatcher ui, MessageWindow window, ShortcutStore shortcuts, Func<IKeyboardShell, (MediaKeyHandler, ShortcutManager)> createHandlers)
    {
        _prefs = prefs;
        _ui = ui;
        _window = window;
        Shortcuts = shortcuts;
        (MediaKeys, ShortcutManager) = createHandlers(this);
        Shortcuts.Changed += (_, _) => ReloadShortcuts();
        ReloadShortcuts();
        _hook = new KeyboardHook(OnKey);
        _rawInput = new RawInputBrightnessKeys(window, key =>
        {
            if (MediaKeys.ActiveKeys.Contains(key))
            {
                MediaKeys.Handle(key, isPressed: true, isRepeat: false, KeyboardHook.CurrentModifiers());
            }
        });
    }

    /// <summary>Raised when some shortcuts are also registered by other applications.</summary>
    public event EventHandler<IReadOnlyList<ShortcutName>>? ConflictsDetected;

    public ShortcutStore Shortcuts { get; }

    public MediaKeyHandler MediaKeys { get; }

    public ShortcutManager ShortcutManager { get; }

    public static (TimeSpan InitialDelay, TimeSpan Interval) RepeatRate()
    {
        // SPI_GETKEYBOARDDELAY: 0..3 → 250..1000 ms. SPI_GETKEYBOARDSPEED: 0..31 → ~2.5..30 repeats per second.
        uint delay = 1, speed = 31;
        unsafe
        {
            PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETKEYBOARDDELAY, 0, &delay, 0);
            PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETKEYBOARDSPEED, 0, &speed, 0);
        }

        var perSecond = 2.5 + (Math.Min(speed, 31u) * (27.5 / 31));
        return (TimeSpan.FromMilliseconds(250 * (Math.Min(delay, 3u) + 1)), TimeSpan.FromSeconds(1 / perSecond));
    }

    /// <summary>Re-evaluates the keys listened for (updateMediaKeyTap + shortcut registrations).</summary>
    public void Update()
    {
        MediaKeys.UpdateKeys();
        ShortcutManager.Disengage();
        CheckConflicts();
    }

    /// <summary>Captures the next key combination for the shortcut recorder (all other handling is paused).</summary>
    public void StartRecording(Action<KeyEvent> onKey) => _recorder = onKey;

    public void StopRecording() => _recorder = null;

    public void OpenSystemSettings(bool sound) =>
        Process.Start(new ProcessStartInfo(sound ? "ms-settings:sound" : "ms-settings:display") { UseShellExecute = true })?.Dispose();

    public void PlayVolumeChangedSound()
    {
        if (_prefs.Bool(PrefKey.PlayVolumeFeedback))
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    public void Dispose()
    {
        _rawInput.Dispose();
        _hook.Dispose();
    }

    private void ReloadShortcuts()
    {
        _shortcuts = Enum.GetValues<ShortcutName>()
            .Select(name => (name, shortcut: Shortcuts.Get(name)))
            .Where(x => x.shortcut is not null)
            .ToDictionary(x => x.name, x => x.shortcut!.Value);
        ShortcutManager.Disengage();
    }

    /// <summary>Runs on the hook thread: decide quickly whether to swallow, post the actual work.</summary>
    private bool OnKey(KeyEvent e)
    {
        if (_recorder is { } recorder)
        {
            if (e.IsDown && !e.IsRepeat)
            {
                _ui.Post(() => recorder(e));
            }

            return true;
        }

        if (!e.IsDown)
        {
            if (_engaged.Remove(e.VirtualKey, out var released))
            {
                _ui.Post(() => ShortcutManager.KeyUp(released));
                return true;
            }

            if (_swallowedDown.Remove(e.VirtualKey) && ToMediaKey(e.VirtualKey) is { } upKey)
            {
                _ui.Post(() => MediaKeys.Handle(upKey, isPressed: false, isRepeat: false, e.Modifiers));
                return true;
            }

            return false;
        }

        if (MatchShortcut(e) is { } match)
        {
            if (!e.IsRepeat)
            {
                _engaged[e.VirtualKey] = match.Name;
                _ui.Post(() => ShortcutManager.KeyDown(match.Name, match.Fine));
            }

            return true;
        }

        if (ToMediaKey(e.VirtualKey) is { } key && MediaKeys.ActiveKeys.Contains(key))
        {
            _swallowedDown.Add(e.VirtualKey);
            _ui.Post(() => MediaKeys.Handle(key, isPressed: true, e.IsRepeat, e.Modifiers));
            return true;
        }

        return false;
    }

    private (ShortcutName Name, bool Fine)? MatchShortcut(KeyEvent e)
    {
        foreach (var (name, shortcut) in _shortcuts)
        {
            if (shortcut.VirtualKey != e.VirtualKey || !ShortcutManager.IsEnabled(name))
            {
                continue;
            }

            if (shortcut.Modifiers == e.Modifiers)
            {
                return (name, false);
            }

            // Holding Win in addition gives fine control.
            if (!shortcut.Modifiers.HasFlag(KeyModifiers.Win) && (shortcut.Modifiers | KeyModifiers.Win) == e.Modifiers)
            {
                return (name, true);
            }
        }

        return null;
    }

    private MediaKey? ToMediaKey(int vk) => vk switch
    {
        VkVolumeUp => MediaKey.VolumeUp,
        VkVolumeDown => MediaKey.VolumeDown,
        VkVolumeMute => MediaKey.Mute,
        VkPause or VkF15 when MediaKeys.UseAlternateBrightnessKeys => MediaKey.BrightnessUp,
        VkScrollLock or VkF14 when MediaKeys.UseAlternateBrightnessKeys => MediaKey.BrightnessDown,
        _ => null,
    };

    /// <summary>Detects shortcuts that another application registered as a global hotkey.</summary>
    private void CheckConflicts()
    {
        var conflicts = new List<ShortcutName>();
        foreach (var (name, shortcut) in _shortcuts.Where(s => ShortcutManager.IsEnabled(s.Key)))
        {
            if (IsTakenByAnotherApp(_window.Handle, shortcut))
            {
                conflicts.Add(name);
            }
        }

        if (conflicts.Count > 0)
        {
            Core.Log.Info($"Shortcut conflicts: {string.Join(", ", conflicts)}");
            ConflictsDetected?.Invoke(this, conflicts);
        }
    }

    public static bool IsTakenByAnotherApp(IntPtr window, Shortcut shortcut)
    {
        const int probeId = 0xB000;
        var modifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (shortcut.Modifiers.HasFlag(KeyModifiers.Control))
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_ALT;
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Win))
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_WIN;
        }

        var hwnd = new HWND(window);
        if (!PInvoke.RegisterHotKey(hwnd, probeId, modifiers, (uint)shortcut.VirtualKey))
        {
            return true;
        }

        PInvoke.UnregisterHotKey(hwnd, probeId);
        return false;
    }
}
