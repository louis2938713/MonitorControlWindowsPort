using MonitorControl.App.Services;
using MonitorControl.Core;
using MonitorControl.Core.Displays;
using MonitorControl.Core.Input;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;

namespace MonitorControl.App.UI.Settings;

/// <summary>Services used by the settings pages.</summary>
internal sealed record SettingsContext(
    Prefs Prefs,
    AppController Controller,
    DisplayManager DisplayManager,
    ShortcutStore Shortcuts,
    KeyboardService Keyboard,
    IAudioService Audio,
    Action CheckForUpdates,
    Action Quit);
