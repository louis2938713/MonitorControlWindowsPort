using MonitorControl.Core.Mathematics;

namespace MonitorControl.Core.Tests;

public class DdcMathTests
{
    [Theory]
    [InlineData(0f, 0, 100, 5, false, 0)]
    [InlineData(0.5f, 0, 100, 5, false, 50)]
    [InlineData(1f, 0, 100, 5, false, 100)]
    [InlineData(0.25f, 0, 100, 5, true, 75)]
    [InlineData(0.5f, 0, 100, 9, false, 27)]
    [InlineData(0.5f, 0, 100, 1, false, 66)]
    [InlineData(0f, 20, 80, 5, false, 20)]
    [InlineData(1f, 20, 80, 5, false, 80)]
    [InlineData(0.5f, 0, 0, 5, false, 0)]
    [InlineData(2f, 0, 100, 5, false, 100)]
    [InlineData(-1f, 0, 100, 5, false, 0)]
    public void ValueToDdc_MapsLikeTheMacApp(float value, int min, int max, int curve, bool invert, int expected) =>
        Assert.Equal(expected, DdcMath.ValueToDdc(value, min, max, curve, invert));

    [Fact]
    public void ValueToDdc_VolumeNeverRoundsToZero()
    {
        Assert.Equal(1, DdcMath.ValueToDdc(0.001f, 0, 100, 5, false, neverZero: true));
        Assert.Equal(0, DdcMath.ValueToDdc(0f, 0, 100, 5, false, neverZero: true));
    }

    [Theory]
    [InlineData(50, 0, 100, 5, false, 0.5f)]
    [InlineData(0, 0, 100, 5, false, 0f)]
    [InlineData(100, 0, 100, 5, false, 1f)]
    [InlineData(25, 0, 100, 5, true, 0.75f)]
    [InlineData(10, 20, 80, 5, false, 0f)]
    [InlineData(200, 0, 100, 5, false, 1f)]
    [InlineData(50, 0, 0, 5, false, 0f)]
    public void DdcToValue_RevertsMapping(int ddc, int min, int max, int curve, bool invert, float expected) =>
        Assert.Equal(expected, DdcMath.DdcToValue((ushort)ddc, min, max, curve, invert), 3);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Conversions_RoundTrip(int curve)
    {
        // Below 10% strong curves lose precision on a 0..100 DDC scale (same as the macOS app).
        for (var i = 10; i <= 100; i++)
        {
            var value = i / 100f;
            var back = DdcMath.DdcToValue(DdcMath.ValueToDdc(value, 0, 100, curve, false), 0, 100, curve, false);
            Assert.InRange(back, value - 0.02f, value + 0.02f);
        }
    }

    [Theory]
    [InlineData(-8, 0f)]
    [InlineData(0, 0.5f)]
    [InlineData(8, 1f)]
    public void CombinedSwitchingValue(int setting, float expected) =>
        Assert.Equal(expected, DdcMath.CombinedSwitchingValue(setting));

    [Fact]
    public void SwBrightnessTransform_KeepsFloorUnlessZeroAllowed()
    {
        Assert.Equal(0.15f, DdcMath.SwBrightnessTransform(0, allowZero: false), 4);
        Assert.Equal(1f, DdcMath.SwBrightnessTransform(1, allowZero: false), 4);
        Assert.Equal(0f, DdcMath.SwBrightnessTransform(0, allowZero: true), 4);
        Assert.Equal(0f, DdcMath.SwBrightnessTransform(0.15f, allowZero: false, reverse: true), 4);
        Assert.Equal(0.5f, DdcMath.SwBrightnessTransform(DdcMath.SwBrightnessTransform(0.5f, false), false, reverse: true), 4);
    }

    [Fact]
    public void RemapCodes_AreParsedAndNormalized()
    {
        Assert.Equal(new byte[] { 0x10, 0x6B }, DdcMath.ParseRemapCodes("10, 6B,zz,0,"));
        Assert.Empty(DdcMath.ParseRemapCodes(null));
        Assert.Equal("10, 6b", DdcMath.NormalizeRemapCodes(" 10 ,6B"));
        Assert.Equal(string.Empty, DdcMath.NormalizeRemapCodes("xyz"));
    }
}
