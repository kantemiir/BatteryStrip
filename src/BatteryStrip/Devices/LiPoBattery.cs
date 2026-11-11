using System;

namespace BatteryStrip.Devices;

/// <summary>
/// Charge estimate from the voltage of a single lithium-polymer cell, for devices that report only volts.
/// Generic discharge curve, from portyporty/g435-battery-percentage (MIT). While charging, the voltage reads high.
/// </summary>
internal static class LiPoBattery
{
    private static readonly (int Millivolts, int Percent)[] Curve =
    [
        (4200, 100), (4150, 95), (4100, 90), (4050, 85), (4000, 80), (3950, 75), (3900, 70), (3850, 62),
        (3800, 55), (3750, 45), (3700, 35), (3650, 25), (3600, 15), (3550, 10), (3500, 5), (3400, 0),
    ];

    public static int Percent(int millivolts)
    {
        if (millivolts >= Curve[0].Millivolts) return 100;
        for (int i = 0; i + 1 < Curve.Length; i++)
        {
            var (highMv, highPercent) = Curve[i];
            var (lowMv, lowPercent) = Curve[i + 1];
            if (millivolts < lowMv) continue;
            double share = (double)(millivolts - lowMv) / (highMv - lowMv);
            return (int)Math.Round(lowPercent + share * (highPercent - lowPercent), MidpointRounding.AwayFromZero);
        }
        return 0;
    }
}
