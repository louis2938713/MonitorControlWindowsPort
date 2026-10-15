namespace MonitorControl.Core.Mathematics;


/// <summary>OSD chiclet math and keyboard step calculations (ported from OSDUtils.swift and the display models).</summary>
public static class StepMath
{
    /// <summary>Number of boxes in the OSD bar.</summary>
    public const float ChicletCount = 16;

    public static float Chiclet(float value, float maxValue, bool half = false) =>
        value * ChicletCount * (half ? 2 : 1) / maxValue;

    public static float Value(float chiclet, float maxValue, bool half = false) =>
        chiclet * maxValue / (ChicletCount * (half ? 2 : 1));

    public static float DistanceFromNearestChiclet(float chiclet) => Math.Abs(MathF.Truncate(chiclet) - chiclet);

    /// <summary>Next value for a keyboard step on DDC controls: one OSD chiclet, or 1% for small increments.</summary>
    public static float CalcNewValue(float currentValue, bool isUp, bool isSmallIncrement, bool half = false)
    {
        float nextValue;
        if (isSmallIncrement)
        {
            nextValue = currentValue + (isUp ? 0.01f : -0.01f);
        }
        else
        {
            var chiclet = Chiclet(currentValue, 1, half);
            var distance = DistanceFromNearestChiclet(chiclet);
            var nextFilledChiclet = isUp ? MathF.Ceiling(chiclet) : MathF.Floor(chiclet);
            const float distanceThreshold = 0.25f; // 25% of the distance between the edges of an OSD box
            if (distance == 0)
            {
                nextFilledChiclet += isUp ? 1 : -1;
            }
            else if (!isUp && distance < distanceThreshold)
            {
                nextFilledChiclet -= 1;
            }
            else if (isUp && distance > 1 - distanceThreshold)
            {
                nextFilledChiclet += 1;
            }

            nextValue = Value(nextFilledChiclet, 1, half);
        }

        return Math.Max(0, Math.Min(1, nextValue));
    }

    /// <summary>Next value for a keyboard step on software/built-in brightness (1/16 or 1/64 steps).</summary>
    public static float CalcNewBrightness(float currentBrightness, bool isUp, bool isSmallIncrement)
    {
        var step = (isUp ? 1f : -1f) / (isSmallIncrement ? 64.0f : 16.0f);
        var delta = step / 4;
        return Math.Min(Math.Max(0, MathF.Ceiling((currentBrightness + delta) / step) * step), 1);
    }

    /// <summary>Slider snapping to 0/25/50/75/100% when within 3%.</summary>
    public static float Snap(float value)
    {
        var intPercent = (int)(value * 100);
        const int snapInterval = 25;
        const int snapThreshold = 3;
        var closest = (intPercent + (snapInterval / 2)) / snapInterval * snapInterval;
        return Math.Abs(closest - intPercent) <= snapThreshold ? closest / 100f : value;
    }
}
