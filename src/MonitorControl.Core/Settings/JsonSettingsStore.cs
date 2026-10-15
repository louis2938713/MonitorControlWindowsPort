using System.Text.Json;

namespace MonitorControl.Core.Settings;

/// <summary>
/// Settings persisted as a flat JSON object. Writes are debounced and atomic (temp file + replace).
/// A corrupted file is kept as a backup and the app starts with default settings.
/// </summary>
public sealed class JsonSettingsStore : SettingsStoreBase, IDisposable
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Timer _saveTimer;
    private readonly object _ioLock = new();
    private bool _dirty;

    public JsonSettingsStore(string path)
    {
        _path = path;
        _saveTimer = new Timer(_ => Flush());
        LoadFromDisk();
    }

    public override void Flush()
    {
        lock (_ioLock)
        {
            lock (SyncRoot)
            {
                if (!_dirty)
                {
                    return;
                }

                _dirty = false;
            }

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temp = _path + ".tmp";
                var sorted = new SortedDictionary<string, object>(Snapshot(), StringComparer.Ordinal);
                File.WriteAllText(temp, JsonSerializer.Serialize(sorted, WriteOptions));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Unable to save settings", ex);
            }
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }

    protected override void OnChanged(string key)
    {
        lock (SyncRoot)
        {
            _dirty = true;
        }

        _saveTimer.Change(TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
        base.OnChanged(key);
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var values = new List<KeyValuePair<string, object>>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                object? value = property.Value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                    _ => null,
                };
                if (value is not null)
                {
                    values.Add(new(property.Name, value));
                }
            }

            Load(values);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            Log.Error("Settings file is corrupted, starting with defaults", ex);
            try
            {
                File.Copy(_path, _path + ".corrupt.bak", overwrite: true);
            }
            catch (IOException)
            {
                // Best effort only.
            }
        }
    }
}
