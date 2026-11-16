namespace BatteryStrip.Devices.Kysona;

/// <summary>Online status (command 03): whether the mouse is linked to its dongle right now.</summary>
/// <param name="Connected">Byte 6 of the reply is not zero: the mouse answers over the dongle's link.</param>
internal readonly record struct KysonaStatus(bool Connected);

/// <summary>Battery (command 04).</summary>
/// <param name="Percent">Byte 6 of the reply, 0 to 100, as the mouse reports it.</param>
/// <param name="IsCharging">Byte 7 of the reply is not zero: the mouse is on its charging cable.</param>
/// <param name="Millivolts">Bytes 8 and 9 of the reply, big-endian battery voltage. Read but not shown: the mouse's own percent is used.</param>
internal readonly record struct KysonaBattery(int Percent, bool IsCharging, int Millivolts);

/// <summary>
/// Wire format of Kysona and Pulsar mice (USB vendor 3554), ATK and VXE dongles (vendor 373B) and the Hitscan Hyperlight
/// receiver (vendor 3770, product 0200). One protocol for all of them: a 17-byte request and 17-byte replies with report
/// id 0x08. On some vendor collections the input report is longer; its first 17 bytes are the reply.
/// <code>
/// byte 0       report id, 0x08
/// byte 1       command: 0x03 online status, 0x04 battery
/// bytes 2-15   zero in a request
/// byte 16      checksum: 0x55 minus the sum of bytes 0-15, modulo 256
/// </code>
/// Replies: command 03, byte 6 = 1 while the mouse is connected to the dongle. Command 04, byte 6 = percent,
/// byte 7 = charging, bytes 8-9 = millivolts. A reply counts only with the report id and command of the request
/// and a matching checksum.
/// Sources: the Linux hid-kysona driver by Lode Willems; the protocol facts were collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor). Only the two read commands are implemented here.
/// </summary>
internal static class KysonaProtocol
{
    /// <summary>Length of a request, and of the part of a reply that carries the answer.</summary>
    public const int FrameLength = 17;

    /// <summary>Byte 0 of every request and reply.</summary>
    public const byte ReportId = 0x08;

    /// <summary>Online status: byte 6 of the reply is 1 while the mouse is linked to the dongle.</summary>
    public const byte CommandOnlineStatus = 0x03;

    /// <summary>Battery: percent, charging flag and millivolts in the reply.</summary>
    public const byte CommandBattery = 0x04;

    private const int ChecksumTarget = 0x55;

    /// <summary>The 17-byte request for a command. Nothing else is ever sent to the device.</summary>
    public static byte[] BuildRequest(byte command)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = command;
        frame[FrameLength - 1] = Checksum(frame);
        return frame;
    }

    /// <summary>
    /// True when the report answers <paramref name="command"/>: it starts with the report id and the command, has at least
    /// 17 bytes and its checksum matches. A longer report (from a vendor collection) is read from its first 17 bytes.
    /// </summary>
    public static bool IsReplyTo(byte[] reply, byte command) =>
        reply.Length >= FrameLength
        && reply[0] == ReportId
        && reply[1] == command
        && reply[FrameLength - 1] == Checksum(reply);

    /// <summary>Reads an online-status reply. Returns false for any other report or a damaged one.</summary>
    public static bool TryParseStatus(byte[] reply, out KysonaStatus status)
    {
        status = default;
        if (!IsReplyTo(reply, CommandOnlineStatus)) return false;
        status = new KysonaStatus(Connected: reply[6] != 0);
        return true;
    }

    /// <summary>Reads a battery reply. Returns false for any other report, a damaged one or a percent above 100.</summary>
    public static bool TryParseBattery(byte[] reply, out KysonaBattery battery)
    {
        battery = default;
        if (!IsReplyTo(reply, CommandBattery)) return false;
        if (reply[6] > 100) return false;
        battery = new KysonaBattery(Percent: reply[6], IsCharging: reply[7] != 0, Millivolts: (reply[8] << 8) | reply[9]);
        return true;
    }

    /// <summary>
    /// Products this protocol talks to: all of Kysona (3554), the ATK and VXE dongles (373B) except the ATK headset
    /// products 11CA and 11CB, which another driver handles, and the Hitscan Hyperlight receiver 3770:0200. Other
    /// Hitscan products speak the Glorious protocol.
    /// </summary>
    public static bool Handles(ushort vendorId, ushort productId) => vendorId switch
    {
        0x3554 => true,
        0x373B => productId is not (0x11CB or 0x11CA),
        0x3770 => productId == 0x0200,
        _ => false,
    };

    /// <summary>Checksum of bytes 0-15: 0x55 minus their sum, modulo 256.</summary>
    private static byte Checksum(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < FrameLength - 1; i++) sum += frame[i];
        return unchecked((byte)(ChecksumTarget - sum));
    }
}
