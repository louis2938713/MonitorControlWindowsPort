namespace MonitorControl.Core.Mathematics;


/// <summary>Conversions between internal 0..1 values and raw DDC values (ported from OtherDisplay.swift).</summary>
public static class DdcMath
{
    /// <summary>Upper bound used when the display does not report a maximum (DDC_MAX_DETECT_LIMIT).</summary>
    public const int MaxDetectLimit = 100;

    public static float CurveMultiplier(int curveDdc) => curveDdc switch
    {
        1 => 0.6f,
        2 => 0.7f,
        3 => 0.8f,
        4 => 0.9f,
        6 => 1.3f,
        7 => 1.5f,
        8 => 1.7f,
        9 => 1.88f,
        _ => 1.0f,
    };

    /// <summary>Internal value (0..1) to DDC value, applying inversion, curve and min/max.</summary>
    /// <param name="neverZero">For volume: a non-zero value never maps to DDC 0 (muting breaks some displays).</param>
    public static ushort ValueToDdc(float value, int minDdc, int maxDdc, int curveDdc, bool invert, bool neverZero = false)
    {
        var from = value;
        if (invert)
        {
            value = 1 - value;
        }

        var curved = MathF.Pow(Math.Clamp(value, 0, 1), CurveMultiplier(curveDdc));
        var deNormalized = ((maxDdc - minDdc) * curved) + minDdc;
        var rounded = MathF.Round(deNormalized, MidpointRounding.AwayFromZero);
        var result = Math.Min(Math.Max(rounded, minDdc), maxDdc);
        var ddc = (ushort)Math.Clamp(result, 0, ushort.MaxValue);
        if (neverZero && from > 0)
        {
            ddc = Math.Max((ushort)1, ddc);
        }

        return ddc;
    }

    /// <summary>DDC value to internal value (0..1), reverting min/max, curve and inversion.</summary>
    public static float DdcToValue(ushort ddc, int minDdc, int maxDdc, int curveDdc, bool invert)
    {
        if (maxDdc <= minDdc)
        {
            return 0;
        }

        var normalized = (Math.Min(Math.Max((float)ddc, minDdc), maxDdc) - minDdc) / (maxDdc - minDdc);
        var value = MathF.Pow(normalized, 1.0f / CurveMultiplier(curveDdc));
        if (invert)
        {
            value = 1 - value;
        }

        return Math.Clamp(value, 0, 1);
    }

    /// <summary>Combined brightness switching point (0..1) from the -8..8 setting.</summary>
    public static float CombinedSwitchingValue(int switchingPointSetting) => (switchingPointSetting + 8) / 16f;

    /// <summary>Maps software brightness to the gamma/shade factor, keeping a 15% floor unless zero is allowed.</summary>
    public static float SwBrightnessTransform(float value, bool allowZero, bool reverse = false)
    {
        var lowThreshold = allowZero ? 0.0f : 0.15f;
        return reverse ? (value - lowThreshold) / (1 - lowThreshold) : (value * (1 - lowThreshold)) + lowThreshold;
    }

    /// <summary>
    /// Parses a comma separated list of hexadecimal VCP codes ("10, 6B"). Invalid and zero codes are skipped.
    /// </summary>
    public static IReadOnlyList<byte> ParseRemapCodes(string? text)
    {
        var codes = new List<byte>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return codes;
        }

        foreach (var part in text.Split(','))
        {
            var trimmed = part.Trim(' ');
            if (trimmed.Length > 0
                && byte.TryParse(trimmed, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var code)
                && code != 0)
            {
                codes.Add(code);
            }
        }

        return codes;
    }

    /// <summary>Normalizes a remap list for display: "10,6b" → "10, 6b".</summary>
    public static string NormalizeRemapCodes(string? text) =>
        string.Join(", ", ParseRemapCodes(text).Select(c => c.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
}
