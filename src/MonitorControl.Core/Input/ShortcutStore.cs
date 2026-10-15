using MonitorControl.Core.Settings;

namespace MonitorControl.Core.Input;

/// <summary>Custom shortcuts persisted in the settings ("KeyboardShortcuts_&lt;name&gt;").</summary>
public sealed class ShortcutStore(ISettingsStore store)
{
    private const string Prefix = "KeyboardShortcuts_";

    public event EventHandler? Changed;

    public Shortcut? Get(ShortcutName name) => Shortcut.Parse(store.GetString(Key(name)));

    public void Set(ShortcutName name, Shortcut? shortcut)
    {
        store.Set(Key(name), shortcut?.Serialize() ?? string.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies the default shortcuts (first launch).</summary>
    public void ApplyDefaults()
    {
        foreach (var (name, shortcut) in Shortcut.Defaults)
        {
            store.Set(Key(name), shortcut.Serialize());
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Other names currently using the same combination.</summary>
    public IEnumerable<ShortcutName> NamesUsing(Shortcut shortcut, ShortcutName except) =>
        Enum.GetValues<ShortcutName>().Where(n => n != except && Get(n) == shortcut);

    private static string Key(ShortcutName name) => Prefix + name;
}
