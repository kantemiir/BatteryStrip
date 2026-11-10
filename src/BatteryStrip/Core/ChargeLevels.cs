using System;

namespace BatteryStrip.Core;

/// <summary>Maps a percent to a colour band, with hysteresis so the colour does not flicker at a boundary.</summary>
internal static class ChargeLevels
{
    public const int LowThreshold = 20;
    public const int CriticalThreshold = 10;

    /// <summary>How far above a threshold the charge must rise before the colour improves.</summary>
    public const int Hysteresis = 2;

    /// <summary>Band of a percent taken on its own.</summary>
    public static ChargeLevel Of(int percent) =>
        percent <= CriticalThreshold ? ChargeLevel.Critical
        : percent <= LowThreshold ? ChargeLevel.Low
        : ChargeLevel.Normal;

    /// <summary>
    /// Band after a new reading. A falling charge changes band exactly at the threshold;
    /// a rising one has to clear the threshold by <see cref="Hysteresis"/> points.
    /// </summary>
    public static ChargeLevel Next(ChargeLevel current, int percent)
    {
        ChargeLevel strict = Of(percent);
        if (strict >= current) return strict;

        ChargeLevel cautious = Of(percent - Hysteresis);
        return (ChargeLevel)Math.Min((int)current, (int)cautious);
    }
}
