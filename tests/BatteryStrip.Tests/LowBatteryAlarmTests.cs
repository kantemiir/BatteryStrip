using System.Collections.Generic;
using BatteryStrip.Core;
using Xunit;

namespace BatteryStrip.Tests;

public class LowBatteryAlarmTests
{
    /// <summary>Feeds readings through the alarm and returns the thresholds it fired, in order.</summary>
    private static List<int> Warnings(params (int Percent, bool Charging)[] readings)
    {
        var fired = new List<int>();
        int state = 0;
        foreach (var (percent, charging) in readings)
        {
            state = LowBatteryAlarm.Step(state, percent, charging, out int threshold);
            if (threshold != 0) fired.Add(threshold);
        }
        return fired;
    }

    [Fact]
    public void Warns_once_at_20_and_once_at_10()
    {
        List<int> fired = Warnings((25, false), (20, false), (19, false), (15, false), (10, false), (9, false), (5, false));
        Assert.Equal(new[] { 20, 10 }, fired);
    }

    [Fact]
    public void A_drop_past_both_thresholds_gives_one_warning()
    {
        Assert.Equal(new[] { 10 }, Warnings((40, false), (8, false), (7, false)));
    }

    [Fact]
    public void Bouncing_on_a_threshold_does_not_repeat_the_warning()
    {
        Assert.Equal(new[] { 20 }, Warnings((20, false), (21, false), (20, false), (22, false), (19, false)));
    }

    [Fact]
    public void Warns_again_after_the_device_was_charged()
    {
        List<int> fired = Warnings((20, false), (30, true), (90, true), (90, false), (20, false));
        Assert.Equal(new[] { 20, 20 }, fired);
    }

    [Fact]
    public void Warns_again_after_the_charge_rose_well_above_the_threshold()
    {
        Assert.Equal(new[] { 20, 20 }, Warnings((20, false), (25, false), (20, false)));
        Assert.Equal(new[] { 10, 10 }, Warnings((10, false), (15, false), (10, false)));
    }

    [Fact]
    public void Stays_silent_while_charging()
    {
        Assert.Empty(Warnings((15, true), (8, true)));
    }
}
