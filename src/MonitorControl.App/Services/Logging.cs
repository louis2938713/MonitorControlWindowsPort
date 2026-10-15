using System.IO;
using Serilog;

namespace MonitorControl.App.Services;

/// <summary>Local rolling file logs in %LOCALAPPDATA%\MonitorControl\logs. Never logs keystrokes.</summary>
internal static class Logging
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorControl", "logs");

    public static void Initialize()
    {
        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .WriteTo.File(
                Path.Combine(LogDirectory, "monitorcontrol-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();
    }
}
