namespace BatteryStrip.Devices.PlayStation;

/// <summary>Charge of a PlayStation controller as read from one input report.</summary>
internal readonly record struct PadBattery(int Percent, bool IsCharging);

/// <summary>
/// Battery fields of Sony controllers. They send their state in every input report, battery included,
/// so nothing has to be sent to read it. Offsets follow the public report layouts (psdevwiki, Linux hid-playstation).
/// </summary>
internal static class PlayStationProtocol
{
    public const ushort SonyVendorId = 0x054C;

    public static bool IsDualSense(ushort productId) => productId is 0x0CE6 or 0x0DF2;

    public static bool IsDualShock4(ushort productId) => productId is 0x05C4 or 0x09CC or 0x0BA0;

    /// <summary>Over Bluetooth both controllers start with a short report without battery data; reading this
    /// feature report (calibration data) switches them to the full report, as games and Steam do.</summary>
    public static byte FullReportFeatureId(ushort productId) => IsDualSense(productId) ? (byte)0x05 : (byte)0x02;

    /// <summary>Reads the battery from a full input report; null for short or unknown reports.</summary>
    public static PadBattery? Parse(ushort productId, byte[] report)
    {
        if (report.Length == 0) return null;
        if (IsDualSense(productId))
        {
            if (report[0] == 0x01 && report.Length >= 64) return DualSense(report[53]);
            if (report[0] == 0x31 && report.Length >= 78) return DualSense(report[54]);
        }
        else if (IsDualShock4(productId))
        {
            if (report[0] == 0x01 && report.Length >= 64) return DualShock4(report[30]);
            if (report[0] == 0x11 && report.Length >= 78) return DualShock4(report[32]);
        }
        return null;
    }

    /// <summary>Low nibble: level 0–10; high nibble: 0 discharging, 1 charging, 2 full, others errors.</summary>
    private static PadBattery? DualSense(byte status)
    {
        int level = status & 0x0F, charging = status >> 4;
        int percent = level * 10 + 5 > 100 ? 100 : level * 10 + 5;
        return charging switch
        {
            0 => new PadBattery(percent, false),
            1 => new PadBattery(percent, true),
            2 => new PadBattery(100, false),
            _ => null,
        };
    }

    /// <summary>Low nibble: level 0–10 (11 = full on cable); bit 4: cable connected.</summary>
    private static PadBattery? DualShock4(byte status)
    {
        int level = status & 0x0F;
        bool cable = (status & 0x10) != 0;
        if (!cable) return new PadBattery(level < 10 ? level * 10 + 5 : 100, false);
        return level switch
        {
            < 10 => new PadBattery(level * 10 + 5, true),
            10 => new PadBattery(100, true),
            11 => new PadBattery(100, false),
            _ => null,
        };
    }
}
