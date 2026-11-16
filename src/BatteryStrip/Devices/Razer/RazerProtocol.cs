using System;
using BatteryStrip.Devices.Polled;

namespace BatteryStrip.Devices.Razer;

/// <summary>Status byte of a Razer reply.</summary>
internal enum RazerStatus : byte
{
    /// <summary>The device is still working on the request; the reply is asked for again.</summary>
    Busy = 0x01,

    /// <summary>The reply is valid and its arguments can be read.</summary>
    Ok = 0x02,

    /// <summary>The device refused the command.</summary>
    Failed = 0x03,

    /// <summary>
    /// No answer in time: the device is switched off or asleep. A wrong transaction id can produce the same
    /// reply, so the other transaction ids are still tried.
    /// </summary>
    Offline = 0x04,

    /// <summary>The device does not know the command.</summary>
    NotSupported = 0x05,
}

/// <summary>A reply to one of our requests: its status and the 80 argument bytes.</summary>
internal sealed record RazerReply(RazerStatus Status, byte[] Arguments);

/// <summary>
/// Wire format of Razer wireless mice, keyboards and dongles (USB vendor 1532). The protocol facts (report layout,
/// command classes and ids, transaction ids, status codes) are from OpenRazer's protocol documentation
/// (github.com/openrazer/openrazer), collected from Wireless Battery (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// <para>
/// A request is a 91-byte feature report: byte 0 is the report id, always 0, and the 90-byte report follows it.
/// The offsets in the list below are offsets within the 90-byte report, so in the feature buffer each one is one higher.
/// <code>
/// 0      status: 0 in a request; in a reply one of RazerStatus
/// 1      transaction id: models expect different values, see TransactionIds
/// 2-3    remaining packets (0 for a single-packet command)
/// 4      protocol type (0)
/// 5      data size (2: the battery commands return two argument bytes)
/// 6      command class (0x07: power and battery)
/// 7      command id
/// 8-87   arguments, 80 bytes; in a reply, argument 1 (the second byte) is the battery value
/// 88     checksum: XOR of bytes 2 to 87
/// 89     reserved (0)
/// </code>
/// Only two read commands are sent: the battery level (class 0x07, id 0x80, argument 1 = 0..255) and the charging
/// state (class 0x07, id 0x84, argument 1 = 1 while charging). Nothing that changes a device setting is sent.
/// </para>
/// </summary>
internal static class RazerProtocol
{
    public const ushort VendorId = 0x1532;

    /// <summary>Length of the feature report: report id 0 plus the 90-byte report.</summary>
    public const int FeatureLength = 91;

    /// <summary>Transaction ids in the order they are tried. Models differ; the one that answered last is tried first next time.</summary>
    public static readonly byte[] TransactionIds = { 0x1F, 0x3F, 0xFF, 0x08, 0x9F };

    /// <summary>How many times a busy device is asked for its reply before the transaction id is given up.</summary>
    public const int ReplyPolls = 8;

    public const byte BatteryClass = 0x07;
    public const byte BatteryLevelId = 0x80;
    public const byte ChargingId = 0x84;

    private const int ReportLength = 90;
    private const int StatusAt = 0;
    private const int TransactionAt = 1;
    private const int DataSizeAt = 5;
    private const int ClassAt = 6;
    private const int IdAt = 7;
    private const int ArgumentsAt = 8;
    private const int ArgumentsLength = 80;
    private const int ChecksumAt = 88;
    private const int BatteryArgument = 1;
    private const byte DataSize = 0x02;

    /// <summary>The 91-byte feature report that asks for one command with the given transaction id.</summary>
    public static byte[] BuildRequest(byte transactionId, byte commandClass, byte commandId)
    {
        var report = new byte[ReportLength];
        report[TransactionAt] = transactionId;
        report[DataSizeAt] = DataSize;
        report[ClassAt] = commandClass;
        report[IdAt] = commandId;
        report[ChecksumAt] = Checksum(report);

        var feature = new byte[FeatureLength];
        Array.Copy(report, 0, feature, 1, ReportLength);
        return feature;
    }

    /// <summary>
    /// Reads the reply to the command <paramref name="commandClass"/>/<paramref name="commandId"/> from a feature
    /// report as the device returns it (91 bytes, report id first). Null for a reply to another command, a status this
    /// protocol does not know, or a buffer that is too short.
    /// </summary>
    public static RazerReply? ParseReply(byte[] feature, byte commandClass, byte commandId)
    {
        if (feature.Length < FeatureLength) return null;

        var report = new byte[ReportLength];
        Array.Copy(feature, 1, report, 0, ReportLength);
        if (report[ClassAt] != commandClass || report[IdAt] != commandId) return null;

        byte status = report[StatusAt];
        if (status < (byte)RazerStatus.Busy || status > (byte)RazerStatus.NotSupported) return null;

        var arguments = new byte[ArgumentsLength];
        Array.Copy(report, ArgumentsAt, arguments, 0, ArgumentsLength);
        return new RazerReply((RazerStatus)status, arguments);
    }

    /// <summary>The battery byte, 0..255, as a percent rounded to the nearest whole number.</summary>
    public static int BatteryPercent(RazerReply reply) => (reply.Arguments[BatteryArgument] * 100 + 127) / 255;

    /// <summary>True when the charging reply says the device is on its charger.</summary>
    public static bool IsCharging(RazerReply reply) => reply.Arguments[BatteryArgument] == 1;

    /// <summary>The product name without the words Windows adds for a receiver or a cable ("Dongle", "(Wired)").</summary>
    public static string CleanName(string product) =>
        DeviceGuess.TrimSuffixes(product, " HyperSpeed Dongle", " Wireless Receiver", " Receiver", " Dongle", " (Wired)", " (Wireless)");

    /// <summary>XOR of report bytes 2 to 87. The transaction id is not covered.</summary>
    private static byte Checksum(byte[] report)
    {
        byte checksum = 0;
        for (int i = 2; i < ChecksumAt; i++) checksum ^= report[i];
        return checksum;
    }
}
