using System;
using BatteryStrip.App;

namespace BatteryStrip.Core;

/// <summary>The one-line status under a device name in the flyout and in tray tooltips.</summary>
internal static class StatusText
{
    public static string For(DeviceSnapshot device, DateTime utcNow) => For(device, utcNow, Text.Current);

    public static string For(DeviceSnapshot device, DateTime utcNow, Strings text)
    {
        if (device.Percent == null) return text.NoCharge;

        TimeSpan age = device.UpdatedUtc is DateTime updated ? utcNow - updated : TimeSpan.MaxValue;
        if (device.IsAsleep) return text.DataFrom(device.Note ?? text.Asleep, text.Age(age));
        if (device.IsCharging) return text.Charging;
        return device.IsEstimate ? text.Updated(age) + text.Approximate : text.Updated(age);
    }
}
