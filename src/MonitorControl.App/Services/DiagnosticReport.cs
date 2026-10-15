using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using MonitorControl.App.Platform;
using MonitorControl.Core.Enums;

namespace MonitorControl.App.Services;

/// <summary>
/// <c>MonitorControl.exe --diagnose</c>: lists displays, how they can be controlled and a few DDC reads.
/// Read-only: nothing is written to the displays. The report is saved next to the logs and opened.
/// </summary>
internal static class DiagnosticReport
{
    private static readonly Command[] ProbedCommands = [Command.Brightness, Command.Contrast, Command.AudioSpeakerVolume, Command.AudioMuteScreenBlank];

    public static int Run(bool open)
    {
        Logging.Initialize();
        var report = Build();
        Directory.CreateDirectory(Logging.LogDirectory);
        var path = Path.Combine(Logging.LogDirectory, "diagnose.txt");
        File.WriteAllText(path, report);
        Serilog.Log.Information("Diagnostic report written to {Path}", path);
        Serilog.Log.CloseAndFlush();
        if (open)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }

        return 0;
    }

    public static string Build()
    {
        var text = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        text.AppendLine(culture, $"MonitorControl diagnostic report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine(culture, $"Windows {Environment.OSVersion.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        text.AppendLine();

        using var platform = new WindowsDisplayPlatform();
        var displays = platform.EnumerateDisplays();
        text.AppendLine(culture, $"{displays.Count} active display(s)");
        foreach (var display in displays)
        {
            text.AppendLine();
            text.AppendLine(culture, $"== {display.Name} (id {display.Id})");
            text.AppendLine(culture, $"   EDID name: {display.RawName}, vendor 0x{display.VendorNumber ?? 0:X4}, model 0x{display.ModelNumber ?? 0:X4}, serial {display.SerialNumber?.ToString(culture) ?? "-"}");
            text.AppendLine(culture, $"   Built-in: {display.IsBuiltIn}, virtual: {display.IsVirtual}, dummy: {display.IsDummy}, HDR: {display.IsHdr}, mirror set: {display.IsInMirrorSet} (mirrors {display.MirrorOf})");
            text.AppendLine(culture, $"   Bounds: {display.Bounds}, work area: {display.WorkArea}");
            text.AppendLine(culture, $"   Device path: {display.DevicePath}");

            var internalBrightness = platform.OpenInternalBrightness(display.Id);
            if (internalBrightness is not null)
            {
                text.AppendLine(culture, $"   Built-in brightness (WMI): {internalBrightness.Get()?.ToString("P0", culture) ?? "unavailable"}");
            }

            var gamma = platform.OpenGamma(display.Id)?.Read();
            text.AppendLine(culture, $"   Gamma ramp: {(gamma is null ? "unavailable" : "peak " + gamma.Peak.ToString("0.000", culture))}");

            var ddc = platform.OpenDdc(display.Id, probe: true);
            if (ddc is null)
            {
                text.AppendLine("   DDC/CI: not available");
                continue;
            }

            text.AppendLine("   DDC/CI: available");
            foreach (var command in ProbedCommands)
            {
                var value = ddc.Read((byte)command);
                text.AppendLine(culture, $"     VCP 0x{(byte)command:X2} {command}: {(value is { } v ? $"{v.Current} / {v.Max}" : "no answer")}");
                Thread.Sleep(50);
            }
        }

        return text.ToString();
    }
}
