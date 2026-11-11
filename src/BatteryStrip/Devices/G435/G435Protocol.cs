using System;

namespace BatteryStrip.Devices.G435;

/// <summary>
/// Wire format of the Logitech G435 receiver 046D:0ACB. The headset has no official battery interface;
/// this is the service channel its firmware updater uses, as documented by the open-source project
/// deddroid/logitech-g435-battery (MIT) and verified on a real receiver (see research/devices.md).
///
/// Every message is a 64-byte feature report 0x06 on the vendor collection 0xFFC0:
/// <code>
/// 06 01 03                 enter service mode (the audio link pauses while it is active)
/// 06 04 03 len  "PA" ...   send a command; len counts the bytes after it
/// 06 08 03                 leave service mode
/// </code>
/// A command is <c>50 41 category sequence length payload</c>. The receiver acknowledges each report
/// with input report <c>06 5A</c>; command replies are then fetched with a feature read of report 0x06,
/// where <c>FF</c> in byte 1 means "nothing yet".
/// </summary>
internal static class G435Protocol
{
    public const byte ReportId = 0x06;
    public const int FeatureLength = 64;

    public const byte AckOk = 0x5A;
    public const byte AckAlreadyInServiceMode = 0xFE;
    public const byte NoReplyYet = 0xFF;

    private const byte CategoryFirmware = 0x06;
    private const byte CategoryWireless = 0x0E;
    private const byte GetRemoteMode = 0xE0;
    private const byte SetRemoteMode = 0xE1;
    private const byte GetConnectStatus = 0xE3;

    /// <summary>In this mode commands are answered by the headset, not by the receiver itself.</summary>
    public const int RemoteModeHeadset = 1;

    /// <summary>Voltages outside this range are not a Li-Po cell; the receiver returns such garbage in the wrong mode.</summary>
    private const int MinMillivolts = 2500, MaxMillivolts = 4600;

    public static byte[] EnterServiceMode() => Frame(0x01, 0x03);

    public static byte[] ExitServiceMode() => Frame(0x08, 0x03);

    public static byte[] QueryConnectStatus(byte sequence) => Command(CategoryWireless, sequence, 1, GetConnectStatus);

    public static byte[] QueryRemoteMode(byte sequence) => Command(CategoryWireless, sequence, 1, GetRemoteMode);

    public static byte[] ChangeRemoteMode(byte sequence, int mode) => Command(CategoryWireless, sequence, 2, SetRemoteMode, (byte)mode);

    public static byte[] QueryBatteryVoltage(byte sequence) => Command(CategoryFirmware, sequence, 1, 0x00, 0x31);

    /// <summary>Sequence numbers run 1..127 and wrap.</summary>
    public static byte NextSequence(byte sequence) => (byte)(sequence % 127 + 1);

    /// <summary>1 when the headset is switched on and linked to the receiver, 0 when it is not.</summary>
    public static int? ParseConnectStatus(byte[] reply) => ParseWirelessValue(reply, GetConnectStatus);

    public static int? ParseRemoteMode(byte[] reply) => ParseWirelessValue(reply, GetRemoteMode);

    /// <summary>Battery voltage from a reply shaped <c>07 00 06 seq|80 00 lo hi</c>.</summary>
    public static int? ParseBatteryMillivolts(byte[] reply)
    {
        for (int i = 0; i + 7 <= reply.Length; i++)
        {
            bool isBatteryReply = reply[i] == 0x07 && reply[i + 1] == 0x00 && reply[i + 2] == CategoryFirmware
                && (reply[i + 3] & 0x80) != 0 && reply[i + 4] == 0x00;
            if (!isBatteryReply) continue;

            int millivolts = reply[i + 5] | (reply[i + 6] << 8);
            if (millivolts is >= MinMillivolts and <= MaxMillivolts) return millivolts;
        }
        return null;
    }

    /// <summary>Charge estimate from the cell voltage; while the headset charges the voltage reads high.</summary>
    public static int MillivoltsToPercent(int millivolts) => LiPoBattery.Percent(millivolts);

    private static int? ParseWirelessValue(byte[] reply, byte command)
    {
        for (int i = 0; i + 4 <= reply.Length; i++)
        {
            if (reply[i] == 0x02 && reply[i + 1] == 0x00 && reply[i + 2] == command) return reply[i + 3];
        }
        return null;
    }

    private static byte[] Command(byte category, byte sequence, byte length, params byte[] payload)
    {
        var body = new byte[6 + payload.Length];
        body[0] = (byte)(5 + payload.Length);
        body[1] = 0x50;
        body[2] = 0x41;
        body[3] = category;
        body[4] = sequence;
        body[5] = length;
        Array.Copy(payload, 0, body, 6, payload.Length);
        return Frame(0x04, 0x03, body);
    }

    private static byte[] Frame(byte op0, byte op1, params byte[] body)
    {
        var report = new byte[FeatureLength];
        report[0] = ReportId;
        report[1] = op0;
        report[2] = op1;
        Array.Copy(body, 0, report, 3, body.Length);
        return report;
    }
}

/// <summary>
/// Steadies the percent shown for the headset. Voltage readings jitter by a few millivolts, so the
/// value only moves down, or up by a clear step (the headset was charged).
/// </summary>
internal sealed class EstimateSmoother
{
    private const int RiseStep = 3;
    private int? _shown;

    public int Apply(int percent)
    {
        if (_shown is not int shown || percent < shown || percent >= shown + RiseStep) _shown = percent;
        return _shown.Value;
    }

    public void Reset() => _shown = null;
}
