namespace MonitorControl.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Minimal logging facade for Core (the App plugs in its logger). Equivalent of os_log.</summary>
public static class Log
{
    public static Action<LogLevel, string, Exception?>? Sink { get; set; }

    public static void Debug(string message) => Sink?.Invoke(LogLevel.Debug, message, null);

    public static void Info(string message) => Sink?.Invoke(LogLevel.Info, message, null);

    public static void Warning(string message, Exception? exception = null) => Sink?.Invoke(LogLevel.Warning, message, exception);

    public static void Error(string message, Exception? exception = null) => Sink?.Invoke(LogLevel.Error, message, exception);
}
