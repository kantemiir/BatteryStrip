namespace BatteryStrip.Core;

/// <summary>
/// Decides when to warn about a low battery: once when the charge falls to 20 %, once more at 10 %.
/// A threshold can fire again only after the device was charged well above it.
/// </summary>
internal static class LowBatteryAlarm
{
    /// <summary>How far above a threshold the charge must rise before that threshold can fire again.</summary>
    public const int RearmMargin = 5;

    /// <summary>Lowest threshold that already produced a warning; 0 when none is pending re-arm.</summary>
    /// <param name="warnedAt">Value kept from the previous call: 0, 20 or 10.</param>
    /// <param name="threshold">Threshold to warn about now, or 0 for no warning.</param>
    /// <returns>New value of <paramref name="warnedAt"/>.</returns>
    public static int Step(int warnedAt, int percent, bool isCharging, out int threshold)
    {
        threshold = 0;

        // A device on the charger needs no warning, and the next discharge starts fresh.
        if (isCharging) return 0;

        if (warnedAt == ChargeLevels.CriticalThreshold && percent >= ChargeLevels.CriticalThreshold + RearmMargin)
            warnedAt = ChargeLevels.LowThreshold;
        if (warnedAt == ChargeLevels.LowThreshold && percent >= ChargeLevels.LowThreshold + RearmMargin)
            warnedAt = 0;

        int crossed = percent <= ChargeLevels.CriticalThreshold ? ChargeLevels.CriticalThreshold
            : percent <= ChargeLevels.LowThreshold ? ChargeLevels.LowThreshold
            : 0;

        // Thresholds count down, so "already warned at 20" is a larger number than "crossed 10".
        if (crossed != 0 && (warnedAt == 0 || crossed < warnedAt))
        {
            threshold = crossed;
            return crossed;
        }
        return warnedAt;
    }
}
