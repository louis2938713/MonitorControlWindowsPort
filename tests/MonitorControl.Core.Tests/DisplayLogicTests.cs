using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;

namespace MonitorControl.Core.Tests;

public sealed class DisplayLogicTests : IDisposable
{
    private readonly TestRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private ExternalDisplay ConfigureExternal(out FakeDdc ddc)
    {
        ddc = _rig.AddExternal(1, "DELL U2720Q");
        _rig.Controller.Start(safeMode: false);
        _rig.FlushDdc();
        return _rig.Manager.GetExternalDisplays().Single();
    }

    [Fact]
    public void Configure_CreatesInternalAndExternalDisplays()
    {
        _rig.AddExternal(1, "DELL U2720Q");
        _rig.AddInternal(2, 0.4f);

        _rig.Controller.Start(safeMode: false);

        Assert.Single(_rig.Manager.GetExternalDisplays());
        Assert.Single(_rig.Manager.GetInternalDisplays());
        Assert.False(_rig.Manager.GetExternalDisplays()[0].IsSw());
    }

    [Fact]
    public void DisplaysWithTheSameName_GetCounterSuffixes()
    {
        _rig.AddExternal(1, "LG");
        _rig.AddExternal(2, "LG");

        _rig.Controller.Start(safeMode: false);

        Assert.Equal(["LG (1)", "LG (2)"], _rig.Manager.Displays.Select(d => d.Name));
    }

    [Fact]
    public void CombinedBrightness_AboveSwitchingPoint_UsesDdcOnly()
    {
        var display = ConfigureExternal(out var ddc);

        display.SetDirectBrightness(0.75f);
        _rig.FlushDdc();

        Assert.Equal((ushort)50, ddc.LastWrite((byte)Command.Brightness));
        Assert.Equal(1f, display.ReadPrefAsFloat(PrefKey.SwBrightness));
    }

    [Fact]
    public void CombinedBrightness_BelowSwitchingPoint_DimsInSoftware()
    {
        var display = ConfigureExternal(out var ddc);

        display.SetDirectBrightness(0.25f);
        _rig.FlushDdc();

        Assert.Equal((ushort)0, ddc.LastWrite((byte)Command.Brightness));
        Assert.Equal(0.5f, display.ReadPrefAsFloat(PrefKey.SwBrightness), 3);
        // Software factor keeps the 15% floor: 0.5 * 0.85 + 0.15.
        Assert.Equal(0.575f, _rig.Platform.Gamma[1].Current.Peak, 3);
    }

    [Fact]
    public void WithoutCombinedBrightness_DdcCoversTheFullRange()
    {
        _rig.Prefs.Set(PrefKey.DisableCombinedBrightness, true);
        var display = ConfigureExternal(out var ddc);

        display.SetDirectBrightness(0.25f);
        _rig.FlushDdc();

        Assert.Equal((ushort)25, ddc.LastWrite((byte)Command.Brightness));
    }

    [Fact]
    public void StepVolume_MovesOneChicletAndShowsOsd()
    {
        var display = ConfigureExternal(out var ddc);

        display.StepVolume(isUp: true, isSmallIncrement: false);
        _rig.FlushDdc();

        // Default volume is 12.5% (2 chiclets) → 3 chiclets = 18.75% → DDC 19.
        Assert.Equal((ushort)19, ddc.LastWrite((byte)Command.AudioSpeakerVolume));
        Assert.Contains(_rig.Osd.Shown, s => s.Command == Command.AudioSpeakerVolume && Math.Abs(s.Value - 0.1875f) < 0.001f);
    }

    [Fact]
    public void ToggleMute_UsesMuteCommandWhenEnabled()
    {
        var display = ConfigureExternal(out var ddc);
        display.SavePref(true, PrefKey.EnableMuteUnmute);

        display.ToggleMute();
        _rig.FlushDdc();

        Assert.Equal((ushort)1, ddc.LastWrite((byte)Command.AudioMuteScreenBlank));
        Assert.Equal(1, display.ReadPrefAsInt(command: Command.AudioMuteScreenBlank));

        display.ToggleMute();
        _rig.FlushDdc();
        Assert.Equal((ushort)2, ddc.LastWrite((byte)Command.AudioMuteScreenBlank));
    }

    [Fact]
    public void RemappedCodes_AreWrittenInsteadOfTheStandardCode()
    {
        var display = ConfigureExternal(out var ddc);
        display.SavePref("6b, 10", PrefKey.RemapDdc, Command.Contrast);

        display.StepContrast(isUp: true, isSmallIncrement: true);
        _rig.FlushDdc();

        Assert.Contains(ddc.Writes, w => w.Code == 0x6B);
        Assert.Contains(ddc.Writes, w => w.Code == 0x10);
        Assert.DoesNotContain(ddc.Writes, w => w.Code == (byte)Command.Contrast);
    }

    [Fact]
    public void UnchangedValues_AreNotWrittenTwice()
    {
        var display = ConfigureExternal(out var ddc);

        display.WriteDdcValues(Command.Contrast, 40);
        display.WriteDdcValues(Command.Contrast, 40);
        _rig.FlushDdc();

        Assert.Single(ddc.Writes, w => w.Code == (byte)Command.Contrast);
        Assert.True(display.ReadPrefAsBool(PrefKey.IsTouched, Command.Contrast));
    }

    [Fact]
    public void NothingIsWritten_WhileAsleep()
    {
        var display = ConfigureExternal(out var ddc);
        _rig.Controller.Sleep();

        display.WriteDdcValues(Command.Contrast, 40);
        _rig.FlushDdc();

        Assert.DoesNotContain(ddc.Writes, w => w.Code == (byte)Command.Contrast);
    }

    [Fact]
    public void DisplayWithoutDdc_IsSoftwareOnlyAndUsesGamma()
    {
        _rig.Platform.Displays.Add(FakePlatform.Info(3, "TV"));
        _rig.Controller.Start(safeMode: false);
        var display = _rig.Manager.GetExternalDisplays().Single();

        Assert.True(display.IsSwOnly());
        display.SetDirectBrightness(0.5f);

        Assert.Equal(0.575f, _rig.Platform.Gamma[3].Current.Peak, 3);
    }

    [Fact]
    public void VirtualDisplay_IsDimmedWithAShade()
    {
        _rig.Platform.Displays.Add(FakePlatform.Info(4, "Miracast", isVirtual: true));
        _rig.Controller.Start(safeMode: false);
        var display = _rig.Manager.GetExternalDisplays().Single();

        display.SetDirectBrightness(0.5f);

        Assert.Equal(1 - 0.575f, _rig.Shades.Alphas[4], 3);
    }

    [Fact]
    public void BuiltInBrightnessChanges_AreSyncedToOtherDisplays()
    {
        _rig.Prefs.Set(PrefKey.EnableBrightnessSync, true);
        _rig.Prefs.Set(PrefKey.DisableSmoothBrightness, true);
        var builtIn = _rig.AddInternal(2, 0.5f);
        _rig.AddExternal(1, "DELL");
        _rig.Controller.Start(safeMode: false);
        var external = _rig.Manager.GetExternalDisplays().Single();
        external.SetBrightness(0.5f);
        var before = external.GetBrightness();

        builtIn.Value = 0.8f; // e.g. changed with the laptop Fn keys
        _rig.Ui.RunPending();

        Assert.True(external.GetBrightness() > before);
    }

    [Fact]
    public void Shutdown_RestoresSoftwareBrightness()
    {
        var display = ConfigureExternal(out _);
        display.SetDirectBrightness(0.2f);

        _rig.Controller.Shutdown();

        Assert.Equal(1f, _rig.Platform.Gamma[1].Current.Peak, 3);
    }
}
