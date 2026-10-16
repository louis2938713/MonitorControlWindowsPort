using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;

namespace MonitorControl.Core.Tests;

internal sealed class FakeDdc : IDdcChannel
{
    public List<(byte Code, ushort Value)> Writes { get; } = [];

    public Dictionary<byte, (ushort Current, ushort Max)> Values { get; } = [];

    public bool Write(byte code, ushort value)
    {
        lock (Writes)
        {
            Writes.Add((code, value));
        }

        return true;
    }

    public (ushort Current, ushort Max)? Read(byte code) => Values.TryGetValue(code, out var v) ? v : null;

    public ushort? LastWrite(byte code)
    {
        lock (Writes)
        {
            return Writes.LastOrDefault(w => w.Code == code) is { Code: not 0 } w ? w.Value : null;
        }
    }
}

internal sealed class FakeInternalBrightness(float value) : IInternalBrightness
{
    public float Value { get; set; } = value;

    public float? Get() => Value;

    public void Set(float value) => Value = value;
}

internal sealed class FakeGamma : IGammaController
{
    public GammaTable Current { get; private set; } = GammaTable.Identity();

    public GammaTable? Read() => Current;

    public bool Write(GammaTable table)
    {
        Current = table;
        return true;
    }
}

internal sealed class FakePlatform : IDisplayPlatform
{
    public List<DisplayInfo> Displays { get; } = [];

    public Dictionary<uint, FakeDdc> Ddc { get; } = [];

    public Dictionary<uint, FakeInternalBrightness> Internal { get; } = [];

    public Dictionary<uint, FakeGamma> Gamma { get; } = [];

    public uint? UnderCursor { get; set; }

    public IReadOnlyList<DisplayInfo> EnumerateDisplays() => Displays;

    public IDdcChannel? OpenDdc(uint displayId, bool probe) => Ddc.GetValueOrDefault(displayId);

    public IInternalBrightness? OpenInternalBrightness(uint displayId) => Internal.GetValueOrDefault(displayId);

    public IGammaController? OpenGamma(uint displayId)
    {
        if (!Gamma.TryGetValue(displayId, out var gamma))
        {
            gamma = new FakeGamma();
            Gamma[displayId] = gamma;
        }

        return gamma;
    }

    public uint? DisplayUnderCursor() => UnderCursor;

    public uint? DisplayWithFocus() => UnderCursor;

    public bool ToggleMirror() => false;

    public static DisplayInfo Info(uint id, string name, bool builtIn = false, bool isVirtual = false) =>
        new(id, name, name, 0x10AC, 0x4123, null, builtIn, isVirtual, false, false, 0, $"path{id}", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040));
}

internal sealed class FakeShades : IShadeHost
{
    public Dictionary<uint, float> Alphas { get; } = [];

    public bool Update(uint displayId)
    {
        Alphas.TryAdd(displayId, 0);
        return true;
    }

    public float? GetAlpha(uint displayId) => Alphas.TryGetValue(displayId, out var a) ? a : null;

    public bool SetAlpha(uint displayId, float alpha)
    {
        Alphas[displayId] = alpha;
        return true;
    }

    public bool Destroy(uint displayId) => Alphas.Remove(displayId);

    public bool DestroyAll()
    {
        var any = Alphas.Count > 0;
        Alphas.Clear();
        return any;
    }
}

internal sealed class FakeOsd : IOsd
{
    public List<(uint Id, Command Command, float Value)> Shown { get; } = [];

    public void Show(uint displayId, Command command, float value, float maxValue = 1, bool roundChiclet = false, bool locked = false) =>
        Shown.Add((displayId, command, value));

    public void ShowVolumeDisabled(uint displayId)
    {
    }

    public void ShowMuteDisabled(uint displayId)
    {
    }

    public void PopEmpty(uint displayId, Command command)
    {
    }
}

/// <summary>Collects posted work; tests run it explicitly.</summary>
internal sealed class ManualDispatcher : IUiDispatcher
{
    private readonly List<Action> _pending = [];

    public void Post(Action action) => _pending.Add(action);

    public void PostDelayed(TimeSpan delay, Action action) => _pending.Add(action);

    /// <summary>Runs pending work (and work posted by it) up to a limit.</summary>
    public void RunPending(int maxRounds = 1)
    {
        for (var round = 0; round < maxRounds && _pending.Count > 0; round++)
        {
            var batch = _pending.ToList();
            _pending.Clear();
            foreach (var action in batch)
            {
                action();
            }
        }
    }
}

internal sealed class FakeAlerts : IAlerts
{
    public bool AskDisableGammaAfterInterference() => false;

    public void ShowIncompatiblePreviousVersion()
    {
    }

    public void ShowSafeMode()
    {
    }
}

/// <summary>A display manager wired with fakes.</summary>
internal sealed class TestRig : IDisposable
{
    public TestRig()
    {
        Prefs = new Prefs(new SettingsStoreBase());
        Manager = new DisplayManager(Prefs, State, Platform, Shades, Osd, Ui, new FakeAlerts());
        Controller = new AppController(Prefs, State, Manager, Ui, new FakeAlerts(), 1);
    }

    public Prefs Prefs { get; }

    public AppState State { get; } = new();

    public FakePlatform Platform { get; } = new();

    public FakeShades Shades { get; } = new();

    public FakeOsd Osd { get; } = new();

    public ManualDispatcher Ui { get; } = new();

    public DisplayManager Manager { get; }

    public AppController Controller { get; }

    public FakeDdc AddExternal(uint id, string name)
    {
        Platform.Displays.Add(FakePlatform.Info(id, name));
        var ddc = new FakeDdc();
        Platform.Ddc[id] = ddc;
        return ddc;
    }

    public FakeInternalBrightness AddInternal(uint id, float brightness)
    {
        Platform.Displays.Add(FakePlatform.Info(id, "Built-in Display", builtIn: true));
        var control = new FakeInternalBrightness(brightness);
        Platform.Internal[id] = control;
        return control;
    }

    /// <summary>Waits until queued DDC writes are done.</summary>
    public void FlushDdc() => Manager.DdcWorker.Run(() => 0);

    public void Dispose() => Manager.Dispose();
}
