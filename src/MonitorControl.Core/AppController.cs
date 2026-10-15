using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;

namespace MonitorControl.Core;

/// <summary>UI-side reactions the core logic needs to trigger.</summary>
public interface IAppShell
{
    /// <summary>Rebuilds the flyout and updates the notification area icon visibility.</summary>
    void UpdateMenus();

    /// <summary>Re-evaluates which keys are listened for (shortcuts and media keys).</summary>
    void UpdateKeyboard();

    /// <summary>Reloads the Displays settings page if it is open.</summary>
    void ReloadDisplaySettings();

    /// <summary>Shows the notification area icon unconditionally (used on reset and quit).</summary>
    void ForceStatusItemVisible();

    /// <summary>Applies Windows-specific first launch defaults (default shortcuts…).</summary>
    void ApplyFirstLaunchDefaults();
}

/// <summary>
/// Application lifecycle: configuration, display reconfiguration, sleep/wake handling, startup actions and
/// the brightness sync job. Ported from AppDelegate.swift.
/// </summary>
public sealed class AppController(Prefs prefs, AppState state, DisplayManager displayManager, IUiDispatcher ui, IAlerts alerts, int buildNumber)
{
    private bool _jobRunning;
    private int _startupActionWriteCounter;

    public IAppShell? Shell { get; set; }

    public DisplayManager DisplayManager { get; } = displayManager;

    public Prefs Prefs { get; } = prefs;

    public AppState State { get; } = state;

    /// <summary>True when this is the first launch (the onboarding window should be shown).</summary>
    public bool IsFirstLaunch { get; private set; }

    public void Start(bool safeMode)
    {
        State.SafeMode = safeMode;
        if (safeMode)
        {
            alerts.ShowSafeMode();
        }

        SetPrefsBuildNumber();
        IsFirstLaunch = !Prefs.Bool(PrefKey.AppAlreadyLaunched);
        SetDefaultPrefs();
        Configure(firstRun: true);
    }

    /// <summary>Called before quitting: puts every display back to its original state.</summary>
    public void Shutdown()
    {
        Log.Info("Goodbye!");
        DisplayManager.RestoreAllForExit();
        Shell?.ForceStatusItemVisible();
        Prefs.Store.Flush();
    }

    private void SetPrefsBuildNumber()
    {
        var previousBuildNumber = Prefs.Int(PrefKey.BuildNumber);
        if (State.SafeMode || (previousBuildNumber < AppInfo.MinPreviousBuildNumber && previousBuildNumber > 0) || previousBuildNumber > buildNumber)
        {
            if (!State.SafeMode)
            {
                alerts.ShowIncompatiblePreviousVersion();
            }

            var language = Prefs.String(PrefKey.Language);
            Prefs.Store.Clear();
            Prefs.Set(PrefKey.Language, language);
        }

        Prefs.Set(PrefKey.BuildNumber, buildNumber);
    }

    public void SetDefaultPrefs()
    {
        if (!Prefs.Bool(PrefKey.AppAlreadyLaunched))
        {
            // Only settings that are not false, 0 or "" by default are set here. Assumes a wiped store.
            Prefs.Set(PrefKey.AppAlreadyLaunched, true);
            Prefs.Set(PrefKey.EnableAutomaticUpdateChecks, true);
            // Windows: brightness keys are not standard, so both media keys and the default shortcuts are on.
            Prefs.Set(PrefKey.KeyboardBrightness, KeyboardBrightness.Both);
            Prefs.Set(PrefKey.KeyboardVolume, KeyboardVolume.Both);
            Shell?.ApplyFirstLaunchDefaults();
        }
    }

    public void DisplayReconfigured()
    {
        DisplayManager.ResetSwBrightnessForAllDisplays(noPrefSave: true);
        foreach (var display in DisplayManager.Displays)
        {
            display.RestoreDefaultGamma();
        }

        State.ReconfigureId += 1;
        Shell?.UpdateKeyboard();
        Log.Info($"Bumping reconfigureID to {State.ReconfigureId}");
        DisplayManager.DestroyAllShades();
        if (State.SleepId == 0)
        {
            var dispatchedReconfigureId = State.ReconfigureId;
            Log.Info($"Display to be reconfigured with reconfigureID {dispatchedReconfigureId}");
            ui.PostDelayed(TimeSpan.FromSeconds(1), () => Configure(dispatchedReconfigureId));
        }
    }

    public void Configure(int dispatchedReconfigureId = 0, bool firstRun = false)
    {
        if (State.SleepId != 0 || dispatchedReconfigureId != State.ReconfigureId)
        {
            return;
        }

        Log.Info($"Request for configuration with reconfigureID {dispatchedReconfigureId}");
        State.ReconfigureId = 0;
        DisplayManager.GammaInterferenceCounter = 0;
        DisplayManager.ConfigureDisplays();
        DisplayManager.AddDisplayCounterSuffixes();
        if (firstRun && Prefs.StartupAction != StartupAction.Write)
        {
            DisplayManager.ResetSwBrightnessForAllDisplays(prefsOnly: true);
        }

        DisplayManager.SetupOtherDisplays(firstRun);
        UpdateMenusAndKeys();
        if ((!firstRun || Prefs.StartupAction == StartupAction.Write) && !Prefs.Bool(PrefKey.DisableCombinedBrightness))
        {
            DisplayManager.RestoreSwBrightnessForAllDisplays(async: !Prefs.Bool(PrefKey.DisableSmoothBrightness));
        }

        Shell?.ReloadDisplaySettings();
        Job(start: true);
    }

    public void UpdateMenusAndKeys()
    {
        Shell?.UpdateMenus();
        Shell?.UpdateKeyboard();
    }

    public void Sleep()
    {
        State.SleepId += 1;
        Log.Info($"Sleeping with sleep {State.SleepId}");
        Shell?.UpdateKeyboard();
    }

    public void Wake()
    {
        if (State.SleepId == 0)
        {
            return;
        }

        Log.Info($"Waking up from sleep {State.SleepId}");
        var dispatchedSleepId = State.SleepId;
        ui.PostDelayed(TimeSpan.FromSeconds(3), () => SoberNow(dispatchedSleepId)); // Some displays take time to recover...
    }

    private void SoberNow(int dispatchedSleepId)
    {
        if (State.SleepId != dispatchedSleepId)
        {
            return;
        }

        Log.Info($"Sober from sleep {State.SleepId}");
        State.SleepId = 0;
        if (State.ReconfigureId != 0)
        {
            var dispatchedReconfigureId = State.ReconfigureId;
            Log.Info($"Displays need reconfig after sober with reconfigureID {dispatchedReconfigureId}");
            Configure(dispatchedReconfigureId);
        }
        else
        {
            Job(start: true);
        }

        StartupActionWriteRepeatAfterSober();
        Shell?.UpdateKeyboard();
    }

    private void StartupActionWriteRepeatAfterSober(int dispatchedCounter = 0)
    {
        var counter = dispatchedCounter == 0 ? 10 : dispatchedCounter;
        _startupActionWriteCounter = dispatchedCounter == 0 ? counter : _startupActionWriteCounter;
        if (Prefs.StartupAction != StartupAction.Write || _startupActionWriteCounter != counter)
        {
            return;
        }

        Log.Info($"Sober write action repeat for DDC - {counter}");
        DisplayManager.RestoreOtherDisplays();
        _startupActionWriteCounter = counter - 1;
        if (counter > 1)
        {
            ui.PostDelayed(TimeSpan.FromSeconds(1), () => StartupActionWriteRepeatAfterSober(counter - 1));
        }
    }

    /// <summary>Periodic job: picks up external brightness changes of built-in displays and syncs them.</summary>
    private void Job(bool start = false)
    {
        if (_jobRunning && start)
        {
            return;
        }

        if (!State.IsActive)
        {
            _jobRunning = false;
            Log.Info("MonitorControl job died because of sleep or reconfiguration.");
            return;
        }

        if (!_jobRunning)
        {
            Log.Info("MonitorControl job started.");
            _jobRunning = true;
        }

        var refreshedSomething = false;
        foreach (var display in DisplayManager.Displays.ToList())
        {
            var delta = display.RefreshBrightness();
            if (delta == 0)
            {
                continue;
            }

            refreshedSomething = true;
            if (Prefs.Bool(PrefKey.EnableBrightnessSync))
            {
                foreach (var targetDisplay in DisplayManager.Displays.Where(d => d != display))
                {
                    var newValue = Math.Clamp(targetDisplay.GetBrightness() + delta, 0, 1);
                    targetDisplay.SetBrightness(newValue);
                    if (targetDisplay.SliderHandler.TryGetValue(Command.Brightness, out var slider))
                    {
                        slider.SetValue(newValue, targetDisplay.Id);
                    }
                }
            }
        }

        ui.PostDelayed(TimeSpan.FromSeconds(refreshedSomething ? 0.1 : 1.0), () => Job());
    }

    public void SettingsReset()
    {
        Log.Info("Resetting all settings.");
        if (!Prefs.Bool(PrefKey.DisableCombinedBrightness))
        {
            DisplayManager.ResetSwBrightnessForAllDisplays(async: false);
        }

        var language = Prefs.String(PrefKey.Language);
        Prefs.Store.Clear();
        Prefs.Set(PrefKey.Language, language);
        Prefs.Set(PrefKey.BuildNumber, buildNumber);
        Shell?.ForceStatusItemVisible();
        SetDefaultPrefs();
        Shell?.UpdateKeyboard();
        Configure(firstRun: true);
    }
}
