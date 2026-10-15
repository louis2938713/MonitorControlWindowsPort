using MonitorControl.Core.Mathematics;

namespace MonitorControl.Core.Tests;

public class StepMathTests
{
    [Theory]
    [InlineData(0.5f, true, false, 0.5625f)]
    [InlineData(0.5f, false, false, 0.4375f)]
    [InlineData(0.52f, true, false, 0.5625f)]
    [InlineData(0.52f, false, false, 0.5f)]
    [InlineData(0.51f, false, false, 0.4375f)]
    [InlineData(0.5f, true, true, 0.51f)]
    [InlineData(0.5f, false, true, 0.49f)]
    [InlineData(1f, true, false, 1f)]
    [InlineData(0f, false, false, 0f)]
    [InlineData(0.995f, true, true, 1f)]
    public void CalcNewValue_StepsOneChicletOrOnePercent(float current, bool up, bool small, float expected) =>
        Assert.Equal(expected, StepMath.CalcNewValue(current, up, small), 4);

    [Fact]
    public void CalcNewValue_HalfScale() =>
        Assert.Equal(17f / 32f, StepMath.CalcNewValue(0.5f, true, false, half: true), 4);

    [Theory]
    [InlineData(0.5f, true, false, 0.5625f)]
    [InlineData(0.5f, false, false, 0.4375f)]
    [InlineData(0.5f, true, true, 0.515625f)]
    [InlineData(0.98f, true, false, 1f)]
    [InlineData(0.02f, false, false, 0f)]
    public void CalcNewBrightness_UsesSixteenthOrSixtyFourthSteps(float current, bool up, bool small, float expected) =>
        Assert.Equal(expected, StepMath.CalcNewBrightness(current, up, small), 4);

    [Theory]
    [InlineData(0.48f, 0.5f)]
    [InlineData(0.40f, 0.40f)]
    [InlineData(0.03f, 0f)]
    [InlineData(0.77f, 0.75f)]
    [InlineData(0.98f, 1f)]
    public void Snap_ToQuarterMarks(float value, float expected) =>
        Assert.Equal(expected, StepMath.Snap(value), 4);

    [Fact]
    public void Chiclets_Convert()
    {
        Assert.Equal(8f, StepMath.Chiclet(0.5f, 1));
        Assert.Equal(16f, StepMath.Chiclet(0.5f, 1, half: true));
        Assert.Equal(0.25f, StepMath.Value(4, 1));
        Assert.Equal(0.25f, StepMath.DistanceFromNearestChiclet(8.25f), 4);
    }
}
