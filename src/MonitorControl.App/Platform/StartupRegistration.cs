using Microsoft.Win32;

namespace MonitorControl.App.Platform;

/// <summary>Start at login through the per-user Run key (replacement for the macOS login item helper app).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MonitorControl";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled && Environment.ProcessPath is { } path)
        {
            key.SetValue(ValueName, $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        Core.Log.Info($"Start at login {(enabled ? "enabled" : "disabled")}");
    }

    /// <summary>Keeps the registered path current (the executable moves when the app is updated).</summary>
    public static void Refresh()
    {
        if (IsEnabled)
        {
            Set(true);
        }
    }
}
