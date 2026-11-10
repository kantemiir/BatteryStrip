namespace BatteryStrip.Devices.Compx;

/// <summary>Battery state reported by a COMPX mouse.</summary>
/// <param name="IsAsleep">The receiver answered, but the mouse itself is asleep or switched off.</param>
internal readonly record struct CompxBattery(bool IsAsleep, int Percent, bool IsCharging);

/// <summary>
/// Wire format of COMPX wireless mice (USB vendor 25A7: Lamzu, Ajazz, Attack Shark and others).
/// A command is a 17-byte feature report 0x08; the answer arrives as input report 0x09:
/// <code>
/// byte 0      report id
/// byte 1      command
/// byte 2      status, 0 = ok
/// bytes 3-4   address
/// byte 5      data length
/// bytes 6-15  data
/// byte 16     checksum: all 17 bytes add up to 0x55
/// </code>
/// Only the battery command is implemented. Commands 0x07 and 0x08 access the mouse settings memory
/// and must never be sent from here.
/// </summary>
internal static class CompxProtocol
{
    public const int FrameLength = 17;
    public const byte RequestReportId = 0x08;
    public const byte ReplyReportId = 0x09;

    private const byte GetBatteryLevel = 0x04;
    private const byte ChecksumTarget = 0x55;

    /// <summary>The request the vendor software sends every 30 seconds.</summary>
    public static byte[] BuildBatteryRequest()
    {
        var frame = new byte[FrameLength];
        frame[0] = RequestReportId;
        frame[1] = GetBatteryLevel;
        frame[FrameLength - 1] = Checksum(frame);
        return frame;
    }

    /// <summary>Reads a battery reply. Returns false for any other report or a damaged frame.</summary>
    public static bool TryParseBatteryReply(byte[] report, out CompxBattery battery)
    {
        battery = default;
        if (report.Length != FrameLength) return false;
        if (report[0] != ReplyReportId || report[1] != GetBatteryLevel) return false;
        if (report[FrameLength - 1] != Checksum(report)) return false;
        if (report[2] != 0) return false;

        byte length = report[5], percent = report[6], charging = report[7];

        // With the mouse asleep the receiver answers for it with an empty payload.
        if (length == 0 && percent == 0 && charging == 0)
        {
            battery = new CompxBattery(IsAsleep: true, Percent: 0, IsCharging: false);
            return true;
        }

        if (percent > 100) return false;
        battery = new CompxBattery(IsAsleep: false, percent, charging != 0);
        return true;
    }

    private static byte Checksum(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < FrameLength - 1; i++) sum += frame[i];
        return unchecked((byte)(ChecksumTarget - sum));
    }
}

/// <summary>
/// Hides the top-up cycle of a full mouse left on the cable: it reports "100 %, not charging" and then
/// from time to time "95 %, charging". Once full, the mouse stays at 100 % until it really discharges.
/// </summary>
internal sealed class FullChargeLatch
{
    private const int TopUpFloor = 95;
    private bool _full;

    public CompxBattery Apply(CompxBattery battery)
    {
        if (battery.IsAsleep) return battery;

        if (battery.Percent == 100 && !battery.IsCharging) _full = true;
        else if (!battery.IsCharging || battery.Percent < TopUpFloor) _full = false;

        return _full ? battery with { Percent = 100, IsCharging = false } : battery;
    }
}
