using MonitorControl.Core.Settings;

namespace MonitorControl.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mc-tests-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void MissingKeys_ReturnDefaults()
    {
        var store = new SettingsStoreBase();
        Assert.False(store.GetBool("x"));
        Assert.Equal(0, store.GetInt("x"));
        Assert.Equal(0f, store.GetFloat("x"));
        Assert.Equal(string.Empty, store.GetString("x"));
        Assert.False(store.Contains("x"));
    }

    [Fact]
    public void Values_ConvertLikeUserDefaults()
    {
        var store = new SettingsStoreBase();
        store.Set("i", 42);
        store.Set("f", 0.75f);
        store.Set("b", true);
        store.Set("s", "12");

        Assert.Equal("42", store.GetString("i"));
        Assert.True(store.GetBool("i"));
        Assert.Equal(42f, store.GetFloat("i"));
        Assert.Equal(0, store.GetInt("f"));
        Assert.Equal(0.75f, store.GetFloat("f"));
        Assert.Equal(1, store.GetInt("b"));
        Assert.Equal(12, store.GetInt("s"));
        Assert.True(store.GetBool("s"));
    }

    [Fact]
    public void Changed_IsRaisedOnlyOnActualChange()
    {
        var store = new SettingsStoreBase();
        var changes = new List<string>();
        store.Changed += (_, key) => changes.Add(key);
        store.Set("a", 1);
        store.Set("a", 1);
        store.Set("a", 2);
        store.Remove("a");
        store.Remove("a");
        Assert.Equal(["a", "a", "a"], changes);
    }

    [Fact]
    public void JsonStore_PersistsAndReloads()
    {
        using (var store = new JsonSettingsStore(SettingsPath))
        {
            store.Set("float", 0.3f);
            store.Set("int", -5);
            store.Set("bool", true);
            store.Set("string", "Dell U2720Q");
        }

        using var reloaded = new JsonSettingsStore(SettingsPath);
        Assert.Equal(0.3f, reloaded.GetFloat("float"));
        Assert.Equal(-5, reloaded.GetInt("int"));
        Assert.True(reloaded.GetBool("bool"));
        Assert.Equal("Dell U2720Q", reloaded.GetString("string"));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void JsonStore_CorruptedFile_IsBackedUpAndIgnored()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ not json");

        using var store = new JsonSettingsStore(SettingsPath);

        Assert.False(store.Contains("anything"));
        Assert.True(File.Exists(SettingsPath + ".corrupt.bak"));
    }

    [Fact]
    public void JsonStore_Clear_RemovesEverything()
    {
        using var store = new JsonSettingsStore(SettingsPath);
        store.Set("a", 1);
        store.Clear();
        store.Flush();

        using var reloaded = new JsonSettingsStore(SettingsPath);
        Assert.False(reloaded.Contains("a"));
    }
}
