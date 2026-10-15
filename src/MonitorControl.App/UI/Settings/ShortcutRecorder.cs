using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MonitorControl.App.Localization;
using MonitorControl.App.Platform;
using MonitorControl.App.Services;
using MonitorControl.Core.Input;

namespace MonitorControl.App.UI.Settings;

/// <summary>
/// Records a custom shortcut: click, then press the combination. Esc/Tab cancel, Backspace/Delete clear.
/// Keys are captured by the global keyboard hook so combinations with Win work too. Port of RecorderCocoa.
/// </summary>
public sealed class ShortcutRecorder : Button
{
    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
        nameof(Shortcut), typeof(ShortcutName), typeof(ShortcutRecorder), new PropertyMetadata(ShortcutName.BrightnessUp, (d, _) => ((ShortcutRecorder)d).UpdateText()));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(ShortcutRecorder), new PropertyMetadata(string.Empty, (d, _) => ((ShortcutRecorder)d).UpdateText()));

    private const int VkBack = 0x08;
    private const int VkTab = 0x09;
    private const int VkEscape = 0x1B;
    private const int VkDelete = 0x2E;
    private bool _recording;

    public ShortcutRecorder()
    {
        SetResourceReference(StyleProperty, typeof(Button));
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        FlowDirection = FlowDirection.LeftToRight; // Keep modifiers before the key in RTL languages.
        Loaded += (_, _) =>
        {
            if (Store is { } store)
            {
                store.Changed += OnStoreChanged;
            }

            Loc.LanguageChanged += OnLanguageChanged;
            UpdateText();
        };
        Unloaded += (_, _) =>
        {
            StopRecording();
            if (Store is { } store)
            {
                store.Changed -= OnStoreChanged;
            }

            Loc.LanguageChanged -= OnLanguageChanged;
        };
        LostKeyboardFocus += (_, _) => StopRecording();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                StopRecording();
            }
        };
    }

    public ShortcutName Shortcut
    {
        get => (ShortcutName)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private KeyboardService? Keyboard => (DataContext as KeyboardViewModel)?.Context.Keyboard;

    private ShortcutStore? Store => (DataContext as KeyboardViewModel)?.Context.Shortcuts;

    protected override void OnClick()
    {
        base.OnClick();
        if (_recording)
        {
            StopRecording();
            return;
        }

        if (Keyboard is not { } keyboard)
        {
            return;
        }

        _recording = true;
        Focus();
        keyboard.StartRecording(OnKey);
        if (Window.GetWindow(this) is { } window)
        {
            window.Deactivated += OnWindowDeactivated;
        }

        UpdateText();
    }

    private void OnKey(KeyEvent e)
    {
        if (!_recording || Store is not { } store)
        {
            return;
        }

        if (e.Modifiers == KeyModifiers.None)
        {
            switch (e.VirtualKey)
            {
                case VkEscape or VkTab:
                    StopRecording();
                    return;
                case VkBack or VkDelete:
                    store.Set(Shortcut, null);
                    StopRecording();
                    return;
                default:
                    break;
            }
        }

        var shortcut = new Core.Input.Shortcut(e.VirtualKey, e.Modifiers);
        if (!shortcut.IsValid)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        StopRecording();
        var handle = Window.GetWindow(this) is { } window ? new WindowInteropHelper(window).Handle : IntPtr.Zero;
        if (store.Get(Shortcut) != shortcut && KeyboardService.IsTakenByAnotherApp(handle, shortcut))
        {
            AlertWindow.Show(Loc.T("Alert.ShortcutTaken.Title"), Loc.T("Alert.ShortcutTaken.Message"));
            return;
        }

        store.Set(Shortcut, shortcut);
    }

    private void StopRecording()
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        Keyboard?.StopRecording();
        if (Window.GetWindow(this) is { } window)
        {
            window.Deactivated -= OnWindowDeactivated;
        }

        UpdateText();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => StopRecording();

    private void OnStoreChanged(object? sender, EventArgs e) => UpdateText();

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateText();

    private void UpdateText()
    {
        var current = Store?.Get(Shortcut);
        Content = _recording ? Loc.T("Keyboard.PressShortcut")
            : current is { } shortcut ? ShortcutNames.Display(shortcut)
            : Placeholder;
        Opacity = !_recording && current is null ? 0.7 : 1;
        ToolTip = Placeholder;
        System.Windows.Automation.AutomationProperties.SetName(this, Placeholder);
    }
}
