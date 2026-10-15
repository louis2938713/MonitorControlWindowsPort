using System.Windows.Threading;
using MonitorControl.Core.Platform;

namespace MonitorControl.App.Services;

/// <summary>Schedules work on the WPF UI thread (equivalent of DispatchQueue.main).</summary>
internal sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.Normal);

    public void PostDelayed(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }
}
