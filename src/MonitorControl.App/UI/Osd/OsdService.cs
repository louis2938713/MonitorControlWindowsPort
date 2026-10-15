using MonitorControl.Core.Enums;
using MonitorControl.Core.Mathematics;
using MonitorControl.Core.Platform;

namespace MonitorControl.App.UI.Osd;

/// <summary>
/// Brightness/volume OSD (replacement for the private macOS OSDManager). Windows has no public API to show its own
/// OSD with arbitrary values, so MonitorControl draws one in the Windows 11 style on the controlled display.
/// </summary>
internal sealed class OsdService(Func<uint, PixelRect?> workAreaOf) : IOsd
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(1000);
    private readonly Dictionary<uint, OsdWindow> _windows = [];

    public void Show(uint displayId, Command command, float value, float maxValue = 1, bool roundChiclet = false, bool locked = false)
    {
        var glyph = command switch
        {
            Command.AudioSpeakerVolume => value > 0 ? Icons.Volume : Icons.Mute,
            Command.AudioMuteScreenBlank => Icons.Mute,
            Command.Contrast => Icons.Contrast,
            _ => Icons.Brightness,
        };
        double filled;
        bool chiclets;
        if (roundChiclet)
        {
            filled = Math.Round(StepMath.Chiclet(value, maxValue));
            chiclets = true;
        }
        else
        {
            filled = maxValue > 0 ? value / maxValue : 0;
            chiclets = false;
        }

        Present(displayId, glyph, filled, chiclets, dimmed: false, DefaultDuration);
    }

    public void ShowVolumeDisabled(uint displayId) => Present(displayId, Icons.Volume, null, false, dimmed: true, DefaultDuration);

    public void ShowMuteDisabled(uint displayId) => Present(displayId, Icons.Mute, null, false, dimmed: true, DefaultDuration);

    public void PopEmpty(uint displayId, Command command) =>
        Present(displayId, command == Command.AudioSpeakerVolume ? Icons.Volume : Icons.Brightness, null, false, dimmed: false, TimeSpan.FromMilliseconds(300));

    private void Present(uint displayId, string glyph, double? filled, bool chiclets, bool dimmed, TimeSpan duration)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Present(displayId, glyph, filled, chiclets, dimmed, duration));
            return;
        }

        if (workAreaOf(displayId) is not { } workArea)
        {
            return;
        }

        if (!_windows.TryGetValue(displayId, out var window))
        {
            window = new OsdWindow();
            _windows[displayId] = window;
        }

        window.FlowDirection = Localization.Loc.FlowDirection;
        window.Present(workArea, glyph, filled, chiclets, dimmed, duration);
    }
}
