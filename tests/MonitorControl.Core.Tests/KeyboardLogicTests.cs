using MonitorControl.Core.Enums;
using MonitorControl.Core.Input;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;

namespace MonitorControl.Core.Tests;

internal sealed class FakeAudio : IAudioService
{
    public event EventHandler? DefaultOutputChanged;

    public string? DefaultOutputName { get; set; } = "DELL U2720Q (NVIDIA High Definition Audio)";

    public bool DefaultOutputIsDisplay { get; set; } = true;

    public void RaiseChanged() => DefaultOutputChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeKeyboardShell : IKeyboardShell
{
    public List<bool> OpenedSettings { get; } = [];

    public int Sounds { get; private set; }

    public void OpenSystemSettings(bool sound) => OpenedSettings.Add(sound);

    public void PlayVolumeChangedSound() => Sounds++;
}

public sealed class KeyboardLogicTests : IDisposable
{
    private readonly TestRig _rig = new();
    private readonly FakeAudio _audio = new();
    private readonly FakeKeyboardShell _shell = new();
    private readonly FakeDdc _ddc;
    private readonly MediaKeyHandler _media;
    private readonly ShortcutManager _shortcuts;

    public KeyboardLogicTests()
    {
        _ddc = _rig.AddExternal(1, "DELL U2720Q");
        _rig.Platform.UnderCursor = 1;
        _rig.Prefs.Set(PrefKey.DisableSmoothBrightness, true);
        _rig.Controller.Start(safeMode: false);
        _media = new MediaKeyHandler(_rig.Manager, _audio, _shell);
        _shortcuts = new ShortcutManager(_rig.Manager, _rig.Ui, _shell, () => (TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(30)));
    }

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void FirstLaunch_EnablesStandardKeysAndShortcuts()
    {
        Assert.Equal(KeyboardBrightness.Both, _rig.Prefs.KeyboardBrightness);
        Assert.Equal(KeyboardVolume.Both, _rig.Prefs.KeyboardVolume);
    }

    [Fact]
    public void VolumeKeys_AreTakenOnlyWhenSoundGoesToADisplay()
    {
        _media.UpdateKeys();
        Assert.Contains(MediaKey.VolumeUp, _media.ActiveKeys);
        Assert.Contains(MediaKey.BrightnessUp, _media.ActiveKeys);

        _audio.DefaultOutputIsDisplay = false;
        _media.UpdateKeys();
        Assert.DoesNotContain(MediaKey.VolumeUp, _media.ActiveKeys);
        Assert.DoesNotContain(MediaKey.Mute, _media.ActiveKeys);
    }

    [Fact]
    public void AudioNameMatching_TakesVolumeKeysForTheMatchingDisplay()
    {
        _rig.Prefs.Set(PrefKey.MultiKeyboardVolume, MultiKeyboardVolume.AudioDeviceNameMatching);
        _audio.DefaultOutputIsDisplay = false;

        _media.UpdateKeys();

        Assert.Contains(MediaKey.VolumeUp, _media.ActiveKeys);
        Assert.Single(_rig.Manager.AudioControlTargetDisplays);
    }

    [Fact]
    public void BrightnessKeys_AreLeftAloneWithOnlyBuiltInDisplays()
    {
        using var rig = new TestRig();
        rig.AddInternal(2, 0.5f);
        rig.Controller.Start(safeMode: false);
        var media = new MediaKeyHandler(rig.Manager, _audio, _shell);

        media.UpdateKeys();

        Assert.DoesNotContain(MediaKey.BrightnessUp, media.ActiveKeys);
    }

    [Fact]
    public void VolumeUpKey_StepsTheDisplayUnderTheMouse()
    {
        _media.Handle(MediaKey.VolumeUp, isPressed: true, isRepeat: false, KeyModifiers.None);
        _rig.FlushDdc();

        Assert.Equal((ushort)19, _ddc.LastWrite((byte)Command.AudioSpeakerVolume));
    }

    [Fact]
    public void ShiftAlt_GivesFineVolumeSteps()
    {
        _media.Handle(MediaKey.VolumeUp, isPressed: true, isRepeat: false, KeyModifiers.Shift | KeyModifiers.Alt);
        _rig.FlushDdc();

        // Default volume is DDC 12 (UInt16(100 * 0.125) truncates, as in the macOS app) → 12% + 1% = 13%.
        Assert.Equal((ushort)13, _ddc.LastWrite((byte)Command.AudioSpeakerVolume));
    }

    [Fact]
    public void AltAlone_OpensWindowsSettings()
    {
        _media.Handle(MediaKey.BrightnessUp, isPressed: true, isRepeat: false, KeyModifiers.Alt);
        _media.Handle(MediaKey.VolumeUp, isPressed: true, isRepeat: false, KeyModifiers.Alt);

        Assert.Equal([false, true], _shell.OpenedSettings);
        Assert.DoesNotContain(_ddc.Writes, w => w.Code == (byte)Command.AudioSpeakerVolume);
    }

    [Fact]
    public void CtrlAltWin_AdjustsContrast()
    {
        _media.Handle(MediaKey.BrightnessUp, isPressed: true, isRepeat: false, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win);
        _rig.FlushDdc();

        Assert.NotNull(_ddc.LastWrite((byte)Command.Contrast));
        Assert.Null(_ddc.LastWrite((byte)Command.Brightness));
    }

    [Fact]
    public void VolumeKeyRelease_PlaysFeedback()
    {
        _media.Handle(MediaKey.VolumeUp, isPressed: false, isRepeat: false, KeyModifiers.None);

        Assert.Equal(1, _shell.Sounds);
    }

    [Fact]
    public void DisabledDisplay_IsNotControlled()
    {
        _rig.Manager.Displays[0].SavePref(true, PrefKey.IsDisabled);

        _media.Handle(MediaKey.VolumeUp, isPressed: true, isRepeat: false, KeyModifiers.None);
        _rig.FlushDdc();

        Assert.Null(_ddc.LastWrite((byte)Command.AudioSpeakerVolume));
    }

    [Fact]
    public void BrightnessShortcut_StepsAndRepeatsWhileHeld()
    {
        var display = _rig.Manager.GetExternalDisplays()[0];
        display.SetBrightness(0.5f);

        _shortcuts.KeyDown(ShortcutName.BrightnessUp, fine: false);
        var afterFirst = display.GetBrightness();
        _rig.Ui.RunPending(); // repeat tick
        var afterRepeat = display.GetBrightness();
        _shortcuts.KeyUp(ShortcutName.BrightnessUp);
        _rig.Ui.RunPending(); // stops
        var afterRelease = display.GetBrightness();

        Assert.Equal(0.5625f, afterFirst, 3);
        Assert.Equal(0.625f, afterRepeat, 3);
        Assert.Equal(afterRepeat, afterRelease);
    }

    [Fact]
    public void ShortcutWithWin_UsesFineSteps()
    {
        var display = _rig.Manager.GetExternalDisplays()[0];
        display.SetBrightness(0.5f);

        _shortcuts.KeyDown(ShortcutName.BrightnessUp, fine: true);

        Assert.Equal(0.51f, display.GetBrightness(), 3);
    }

    [Fact]
    public void MuteShortcut_TogglesMute()
    {
        _shortcuts.KeyDown(ShortcutName.Mute, fine: false);

        Assert.Equal(1, _rig.Manager.Displays[0].ReadPrefAsInt(command: Command.AudioMuteScreenBlank));
    }

    [Fact]
    public void Shortcuts_AreIgnoredWhenCustomKeysAreDisabled()
    {
        _rig.Prefs.Set(PrefKey.KeyboardVolume, KeyboardVolume.Media);

        _shortcuts.KeyDown(ShortcutName.Mute, fine: false);

        Assert.NotEqual(1, _rig.Manager.Displays[0].ReadPrefAsInt(command: Command.AudioMuteScreenBlank));
    }

    [Fact]
    public void ShortcutStore_RoundTripsAndValidates()
    {
        var store = new ShortcutStore(new SettingsStoreBase());
        store.ApplyDefaults();

        Assert.Equal(Shortcut.Defaults[ShortcutName.VolumeUp], store.Get(ShortcutName.VolumeUp));
        store.Set(ShortcutName.VolumeUp, null);
        Assert.Null(store.Get(ShortcutName.VolumeUp));
        Assert.True(new Shortcut(0x70, KeyModifiers.None).IsValid); // F1
        Assert.False(new Shortcut(0x41, KeyModifiers.Shift).IsValid); // Shift+A
        Assert.True(new Shortcut(0x41, KeyModifiers.Control).IsValid);
        Assert.Null(Shortcut.Parse("garbage"));
    }
}
