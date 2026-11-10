using BatteryStrip.Core;
using Xunit;

namespace BatteryStrip.Tests;

public class ChargeLevelsTests
{
    [Theory]
    [InlineData(100, (int)ChargeLevel.Normal)]
    [InlineData(21, (int)ChargeLevel.Normal)]
    [InlineData(20, (int)ChargeLevel.Low)]
    [InlineData(11, (int)ChargeLevel.Low)]
    [InlineData(10, (int)ChargeLevel.Critical)]
    [InlineData(0, (int)ChargeLevel.Critical)]
    public void Band_of_a_single_value(int percent, int expected)
    {
        Assert.Equal((ChargeLevel)expected, ChargeLevels.Of(percent));
    }

    [Fact]
    public void Falling_charge_changes_band_exactly_at_the_threshold()
    {
        Assert.Equal(ChargeLevel.Normal, ChargeLevels.Next(ChargeLevel.Normal, 21));
        Assert.Equal(ChargeLevel.Low, ChargeLevels.Next(ChargeLevel.Normal, 20));
        Assert.Equal(ChargeLevel.Critical, ChargeLevels.Next(ChargeLevel.Low, 10));
        Assert.Equal(ChargeLevel.Critical, ChargeLevels.Next(ChargeLevel.Normal, 5));
    }

    [Fact]
    public void Colour_does_not_flicker_when_the_value_bounces_on_a_threshold()
    {
        ChargeLevel level = ChargeLevels.Next(ChargeLevel.Normal, 20);
        foreach (int percent in new[] { 21, 20, 21, 22, 21 })
        {
            level = ChargeLevels.Next(level, percent);
            Assert.Equal(ChargeLevel.Low, level);
        }
        Assert.Equal(ChargeLevel.Normal, ChargeLevels.Next(level, 23));
    }

    [Fact]
    public void Leaving_the_critical_band_needs_the_same_margin()
    {
        Assert.Equal(ChargeLevel.Critical, ChargeLevels.Next(ChargeLevel.Critical, 12));
        Assert.Equal(ChargeLevel.Low, ChargeLevels.Next(ChargeLevel.Critical, 13));
        Assert.Equal(ChargeLevel.Normal, ChargeLevels.Next(ChargeLevel.Critical, 80));
    }
}
