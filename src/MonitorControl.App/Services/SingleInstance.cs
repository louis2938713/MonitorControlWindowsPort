namespace MonitorControl.App.Services;

/// <summary>
/// One instance per user session. Launching the app again asks the running instance to open its settings
/// (like reopening the app on macOS), which is also the way back when the notification area icon is hidden.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\MonitorControl.SingleInstance";
    private const string EventName = @"Local\MonitorControl.OpenSettings";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _event;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, EventWaitHandle openSettings)
    {
        _mutex = mutex;
        _event = openSettings;
    }

    /// <summary>Returns null when another instance is already running (it has been asked to show its settings).</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var openSettings = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        if (createdNew)
        {
            return new SingleInstance(mutex, openSettings);
        }

        openSettings.Set();
        openSettings.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="onActivated"/> (on a background thread) when another launch is attempted.</summary>
    public void Listen(Action onActivated)
    {
        var thread = new Thread(() =>
        {
            var handles = new[] { _event, _stop.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                onActivated();
            }
        })
        {
            IsBackground = true,
            Name = "Single instance listener",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _event.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _stop.Dispose();
    }
}
