using System.Globalization;

namespace MonitorControl.Core.Settings;

/// <summary>In-memory store with UserDefaults-like conversions. Values are bool, long, double or string.</summary>
public class SettingsStoreBase : ISettingsStore
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    protected object SyncRoot { get; } = new();

    public event EventHandler<string>? Changed;

    public bool Contains(string key)
    {
        lock (SyncRoot)
        {
            return _values.ContainsKey(key);
        }
    }

    public void Remove(string key)
    {
        bool removed;
        lock (SyncRoot)
        {
            removed = _values.Remove(key);
        }

        if (removed)
        {
            OnChanged(key);
        }
    }

    public bool GetBool(string key) => TryGet(key) switch
    {
        bool b => b,
        long l => l != 0,
        double d => d != 0,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n != 0),
        _ => false,
    };

    public int GetInt(string key) => TryGet(key) switch
    {
        bool b => b ? 1 : 0,
        long l => (int)Math.Clamp(l, int.MinValue, int.MaxValue),
        double d => double.IsFinite(d) ? (int)Math.Clamp(Math.Truncate(d), int.MinValue, int.MaxValue) : 0,
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
            ? (int)Math.Clamp(Math.Truncate(v), int.MinValue, int.MaxValue)
            : 0,
        _ => 0,
    };

    public float GetFloat(string key) => TryGet(key) switch
    {
        bool b => b ? 1 : 0,
        long l => l,
        double d => (float)d,
        string s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0,
        _ => 0,
    };

    public string GetString(string key) => TryGet(key) switch
    {
        string s => s,
        bool b => b ? "1" : "0",
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    public void Set(string key, bool value) => SetValue(key, value);

    public void Set(string key, int value) => SetValue(key, (long)value);

    public void Set(string key, float value) => SetValue(key, (double)value);

    public void Set(string key, string value) => SetValue(key, value);

    public void Clear()
    {
        lock (SyncRoot)
        {
            _values.Clear();
        }

        OnChanged(string.Empty);
    }

    public virtual void Flush()
    {
    }

    /// <summary>Copy of all values (for persistence).</summary>
    protected Dictionary<string, object> Snapshot()
    {
        lock (SyncRoot)
        {
            return new Dictionary<string, object>(_values, StringComparer.Ordinal);
        }
    }

    /// <summary>Replaces all values without raising change notifications (for loading).</summary>
    protected void Load(IEnumerable<KeyValuePair<string, object>> values)
    {
        lock (SyncRoot)
        {
            _values.Clear();
            foreach (var (key, value) in values)
            {
                _values[key] = value;
            }
        }
    }

    protected virtual void OnChanged(string key) => Changed?.Invoke(this, key);

    private object? TryGet(string key)
    {
        lock (SyncRoot)
        {
            return _values.GetValueOrDefault(key);
        }
    }

    private void SetValue(string key, object value)
    {
        lock (SyncRoot)
        {
            if (_values.TryGetValue(key, out var existing) && existing.Equals(value))
            {
                return;
            }

            _values[key] = value;
        }

        OnChanged(key);
    }
}
