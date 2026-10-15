namespace MonitorControl.Core.Settings;

/// <summary>
/// Flat key/value settings store with UserDefaults-like semantics: reading a missing key returns
/// false / 0 / "" and numbers, booleans and strings convert into each other when read.
/// Implementations must be thread-safe (the DDC thread writes settings too).
/// </summary>
public interface ISettingsStore
{
    event EventHandler<string>? Changed;

    bool Contains(string key);

    void Remove(string key);

    bool GetBool(string key);

    int GetInt(string key);

    float GetFloat(string key);

    string GetString(string key);

    void Set(string key, bool value);

    void Set(string key, int value);

    void Set(string key, float value);

    void Set(string key, string value);

    /// <summary>Removes every setting.</summary>
    void Clear();

    /// <summary>Writes pending changes to storage immediately.</summary>
    void Flush();
}
