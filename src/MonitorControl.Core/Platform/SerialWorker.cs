using System.Collections.Concurrent;

namespace MonitorControl.Core.Platform;

/// <summary>A dedicated background thread executing work items in order (equivalent of a serial DispatchQueue).</summary>
public sealed class SerialWorker : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;

    public SerialWorker(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
    }

    public bool IsCurrentThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    public void Enqueue(Action action)
    {
        if (!_work.IsAddingCompleted)
        {
            _work.Add(action);
        }
    }

    /// <summary>Runs a function on the queue and waits for its result.</summary>
    public T Run<T>(Func<T> function)
    {
        if (IsCurrentThread)
        {
            return function();
        }

        using var done = new ManualResetEventSlim();
        T result = default!;
        Exception? error = null;
        Enqueue(() =>
        {
            try
            {
                result = function();
            }
#pragma warning disable CA1031 // Rethrown on the calling thread
            catch (Exception ex)
#pragma warning restore CA1031
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        if (error is not null)
        {
            throw new InvalidOperationException("Queued work failed", error);
        }

        return result;
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(2));
        _work.Dispose();
    }

    private void Run()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // A failing work item must not kill the queue
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Log.Error($"Unhandled exception on queue {_thread.Name}", ex);
            }
        }
    }
}
