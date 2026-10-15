using System.Runtime.InteropServices;
using MonitorControl.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MonitorControl.App.Platform;

/// <summary>A key event seen by the low-level keyboard hook.</summary>
internal readonly record struct KeyEvent(int VirtualKey, bool IsDown, bool IsRepeat, KeyModifiers Modifiers);

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL). No special permission is needed on Windows (unlike macOS
/// Accessibility). The handler must return quickly: it only decides whether to swallow the key and posts the work.
/// Keystrokes are never logged.
/// </summary>
internal sealed unsafe class KeyboardHook : IDisposable
{
    /// <summary>Unassigned virtual key used to keep Windows from opening Start when Win+key is swallowed.</summary>
    private const ushort MaskKey = 0xE8;
    private const uint InjectedFlag = 0x10;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private readonly HOOKPROC _proc;
    private readonly HashSet<int> _down = [];
    private readonly UnhookWindowsHookExSafeHandle _hook;

    public KeyboardHook(Func<KeyEvent, bool> handler)
    {
        Handler = handler;
        _proc = HookProc;
        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _proc, PInvoke.GetModuleHandle((string?)null), 0);
        if (_hook.IsInvalid)
        {
            Core.Log.Error($"Unable to install the keyboard hook ({Marshal.GetLastPInvokeError()})");
        }
    }

    public Func<KeyEvent, bool> Handler { get; }

    public static bool IsModifier(int vk) => vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    /// <summary>Current modifier state (physical keys).</summary>
    public static KeyModifiers CurrentModifiers()
    {
        static bool Down(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;
        var modifiers = KeyModifiers.None;
        if (Down(0x11))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (Down(0x12))
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (Down(0x10))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (Down(0x5B) || Down(0x5C))
        {
            modifiers |= KeyModifiers.Win;
        }

        return modifiers;
    }

    public void Dispose() => _hook.Dispose();

    private LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            var data = (KBDLLHOOKSTRUCT*)lParam.Value;
            var vk = (int)data->vkCode;
            var injected = ((uint)data->flags & InjectedFlag) != 0;
            var message = (uint)wParam.Value;
            var isDown = message is WmKeyDown or WmSysKeyDown;
            if (!(injected && vk == MaskKey) && !IsModifier(vk))
            {
                var isRepeat = isDown && !_down.Add(vk);
                if (!isDown)
                {
                    _down.Remove(vk);
                }

                var modifiers = CurrentModifiers();
                bool swallow;
                try
                {
                    swallow = Handler(new KeyEvent(vk, isDown, isRepeat, modifiers));
                }
#pragma warning disable CA1031 // Never let an exception escape a system hook
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    Core.Log.Error("Keyboard handler failed", ex);
                    swallow = false;
                }

                if (swallow)
                {
                    if (isDown && (modifiers & (KeyModifiers.Win | KeyModifiers.Alt)) != 0)
                    {
                        MaskModifierRelease();
                    }

                    return new LRESULT(1);
                }
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    /// <summary>
    /// When a Win+key or Alt+key combination is swallowed, Windows only sees Win/Alt pressed and released alone and would
    /// open Start or the menu bar. A dummy key in between prevents that.
    /// </summary>
    private static void MaskModifierRelease()
    {
        Span<INPUT> inputs = stackalloc INPUT[2];
        inputs[0].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[0].Anonymous.ki.wVk = (VIRTUAL_KEY)MaskKey;
        inputs[1].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[1].Anonymous.ki.wVk = (VIRTUAL_KEY)MaskKey;
        inputs[1].Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        _ = PInvoke.SendInput(inputs, sizeof(INPUT));
    }
}
