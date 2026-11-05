using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

public class HourglassModelTests
{
    [Fact]
    public void AtStartTopIsFullAndBottomIsEmpty()
    {
        var levels = HourglassModel.Compute(0);

        Assert.Equal(1, levels.TopFill);
        Assert.Equal(0, levels.BottomFill);
        Assert.Equal(1, levels.TopHeightFraction, precision: 9);
        Assert.Equal(0, levels.BottomHeightFraction, precision: 9);
        Assert.True(levels.StreamActive);
    }

    [Fact]
    public void AtEndTopIsEmptyBottomIsFullAndStreamStops()
    {
        var levels = HourglassModel.Compute(1);

        Assert.Equal(0, levels.TopFill);
        Assert.Equal(1, levels.BottomFill);
        Assert.Equal(0, levels.TopHeightFraction, precision: 9);
        Assert.Equal(1, levels.BottomHeightFraction, precision: 9);
        Assert.False(levels.StreamActive);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void SandVolumeIsLinearInProgress(double progress)
    {
        var levels = HourglassModel.Compute(progress);

        Assert.Equal(1 - progress, levels.TopFill, precision: 9);
        Assert.Equal(progress, levels.BottomFill, precision: 9);
        Assert.Equal(1, levels.TopFill + levels.BottomFill, precision: 9);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    public void HeightFractionsEncloseTheRequestedVolume(double fill)
    {
        const double n = HourglassModel.DefaultNeckRatio;
        var k = 1 - n;
        var total = (n + 1) / 2;

        var t = HourglassModel.TopHeightFraction(fill);
        var topArea = n * t + k * t * t / 2;
        Assert.Equal(fill * total, topArea, precision: 9);

        var s = HourglassModel.BottomHeightFraction(fill);
        var bottomArea = s - k * s * s / 2;
        Assert.Equal(fill * total, bottomArea, precision: 9);
    }

    [Fact]
    public void HeightFractionsAreMonotonic()
    {
        var previousTop = double.MaxValue;
        var previousBottom = -1.0;
        for (var i = 0; i <= 100; i++)
        {
            var levels = HourglassModel.Compute(i / 100.0);
            Assert.True(levels.TopHeightFraction <= previousTop);
            Assert.True(levels.BottomHeightFraction >= previousBottom);
            previousTop = levels.TopHeightFraction;
            previousBottom = levels.BottomHeightFraction;
        }
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(1.5, 1)]
    [InlineData(double.NaN, 0)]
    public void OutOfRangeProgressIsClamped(double progress, double expected)
    {
        var levels = HourglassModel.Compute(progress);

        Assert.Equal(expected, levels.BottomFill);
    }
}
