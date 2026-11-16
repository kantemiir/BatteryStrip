using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BatteryStrip.Devices.Atk;

/// <summary>
/// Frames of the serial protocol of the ATK N9 Pro headset, which talks over a USB virtual COM port at 9600 8N1.
/// The command bytes and the reply layout were collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor), which took them from ATK's own configuration software
/// ("n9-pro" model). Every frame is 64 bytes. A request is [2][command][sub][zeros]; a reply is
/// [length][command][sub][data...], and the value the reply carries is its byte 4. The headset also sends frames on its
/// own, so a reply is recognised by its command and sub-command, never by its position.
/// </summary>
internal static class AtkSerialProtocol
{
    public const int BaudRate = 9600;

    public const int FrameLength = 64;

    /// <summary>USB vendor id of the ATK headset family.</summary>
    public const ushort VendorId = 0x373B;

    /// <summary>Product id of the 2.4 GHz dongle.</summary>
    public const ushort DongleProductId = 0x11CB;

    /// <summary>Product id of the headset on its USB cable.</summary>
    public const ushort CableProductId = 0x11CA;

    /// <summary>Asks whether the headset is connected to the dongle or the cable.</summary>
    public const byte StatusCommand = 0x00, StatusSub = 0x04;

    /// <summary>Asks for the battery: percent and the charging flag.</summary>
    public const byte BatteryCommand = 0x00, BatterySub = 0x03;

    /// <summary>Index of the value byte in a reply: the second byte after the length, command and sub-command.</summary>
    private const int ValueIndex = 4;

    /// <summary>A request frame: length 2, the command and the sub-command, then zeros up to 64 bytes.</summary>
    public static byte[] Request(byte command, byte sub)
    {
        var frame = new byte[FrameLength];
        frame[0] = 2;
        frame[1] = command;
        frame[2] = sub;
        return frame;
    }

    /// <summary>True for a frame that answers the given command and sub-command.</summary>
    public static bool IsReplyTo(byte[] frame, byte command, byte sub) =>
        frame.Length == FrameLength && frame[1] == command && frame[2] == sub;

    /// <summary>
    /// Takes whole frames off the front of <paramref name="received"/> until one answers (command, sub). Frames before it
    /// are unsolicited and dropped; a frame still incomplete stays in the list for the next bytes. Null when no answer is complete.
    /// </summary>
    public static byte[]? TakeReply(List<byte> received, byte command, byte sub)
    {
        while (received.Count >= FrameLength)
        {
            byte[] frame = received.GetRange(0, FrameLength).ToArray();
            received.RemoveRange(0, FrameLength);
            if (IsReplyTo(frame, command, sub)) return frame;
        }
        return null;
    }

    /// <summary>The status reply: the value byte is 1 when the headset is connected to the dongle or the cable.</summary>
    public static bool IsConnected(byte[] status) => status[ValueIndex] == 1;

    /// <summary>The battery reply: the percent in bits 0 to 6 (at most 100) and the charging flag in bit 7.</summary>
    public static (int Percent, bool Charging) ParseBattery(byte[] battery)
    {
        int raw = battery[ValueIndex];
        return (Math.Min(raw & 0x7F, 100), (raw & 0x80) != 0);
    }

    /// <summary>
    /// Product id of the headset from a device instance id such as "USB\VID_373B&amp;PID_11CB&amp;MI_02\...". Null for any other device.
    /// </summary>
    public static ushort? ProductIdOf(string instanceId)
    {
        if (instanceId.IndexOf("VID_373B&PID_11CB", StringComparison.OrdinalIgnoreCase) >= 0) return DongleProductId;
        if (instanceId.IndexOf("VID_373B&PID_11CA", StringComparison.OrdinalIgnoreCase) >= 0) return CableProductId;
        return null;
    }

    /// <summary>
    /// The COM port named at the end of a device's friendly name: "USB Serial Device (COM5)" gives "COM5". Null when the
    /// name does not end with a COM port in brackets.
    /// </summary>
    public static string? PortNameOf(string? friendlyName)
    {
        string name = (friendlyName ?? "").Trim();
        if (!name.EndsWith(")", StringComparison.Ordinal)) return null;

        int open = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;

        string digits = name.Substring(open + 4, name.Length - 1 - (open + 4));
        if (digits.Length == 0 || digits.Length > 3 || !digits.All(c => c >= '0' && c <= '9')) return null;

        int number = int.Parse(digits, CultureInfo.InvariantCulture);
        return number == 0 ? null : "COM" + number.ToString(CultureInfo.InvariantCulture);
    }
}
