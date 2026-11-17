using System;
using System.Collections.Generic;
using System.Linq;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.Logitech;

/// <summary>A frame received on the Centurion collection, its transport header removed.</summary>
/// <param name="Address">The device address of the addressed variant (report 0x50); null for report 0x51.</param>
/// <param name="Flags">Fragment flags: 0 for a frame that stands alone.</param>
/// <param name="Payload">Feature index, function and software id, then the parameters, as long as the frame says.</param>
internal sealed record CenturionFrame(byte ReportId, byte? Address, byte Flags, byte[] Payload);

/// <summary>Something the dongle's bridge passed on from the headset behind it.</summary>
internal abstract record CenturionBridgeEvent
{
    private CenturionBridgeEvent() { }

    /// <summary>The headset was switched on (<see cref="Linked"/>) or off.</summary>
    public sealed record Connection(bool Linked) : CenturionBridgeEvent;

    /// <summary>A message from the headset: the answer to a request, or a notification when the software id is 0.</summary>
    /// <param name="Error">The headset's error code; then <see cref="SubIndex"/> and <see cref="Tag"/> are those of the failed request.</param>
    public sealed record Message(byte SubIndex, byte Tag, byte[] Data, byte? Error) : CenturionBridgeEvent;
}

/// <summary>A battery reading from feature 0x0104.</summary>
internal readonly record struct CenturionBattery(int Percent, bool IsCharging);

/// <summary>
/// "Centurion", the transport of Logitech's newer LIGHTSPEED headsets (G522 LIGHTSPEED, PRO X 2 LIGHTSPEED). The
/// dongle has a vendor collection on usage page 0xFFA0 with 64-byte reports both ways:
/// <code>
/// 51          length  flags  feature-index  function&lt;&lt;4|software-id  parameters   (PRO X 2)
/// 50  address length  flags  feature-index  function&lt;&lt;4|software-id  parameters   (G522)
/// </code>
/// "length" counts the flags byte and everything after it that matters. Messages inside are HID++ 2.0 style: features
/// are found through the root feature (index 0). The dongle reaches the headset through its bridge feature 0x0003,
/// which wraps a second message: <c>bridge-index 1|sw  0 length  0 sub-index function&lt;&lt;4|sw  parameters</c>; the
/// bridge acknowledges and then answers with its event 1 (MessageEvent). Its event 0 says the headset came or went.
/// The battery is feature 0x0104: percent, percent again, state (0 discharging, 1 and 2 charging, 3 full).
/// Facts from the Solaar project (lib/logitech_receiver: base.py, centurion.py, device.py) and the Linux patch
/// "HID: logitech-headset: add a battery driver for the PRO X 2" (2026); the code here is our own.
/// </summary>
internal static class Centurion
{
    public const ushort UsagePage = 0xFFA0;
    public const int FrameLength = 64;

    /// <summary>Report 0x51 has no address byte (PRO X 2); report 0x50 carries the device address in byte 1 (G522).</summary>
    public const byte PlainReport = 0x51, AddressedReport = 0x50;

    /// <summary>
    /// Marks our requests, so answers meant for other programs are not taken for ours. Ids in use elsewhere:
    /// 0x07 OpenRGB, 0x0A LGSTrayEx, 0x0B Solaar, 0x0D G HUB, 0x0F Logitech firmware tools.
    /// </summary>
    public const byte SoftwareId = 0x0C;

    public const ushort FeatureRoot = 0x0000, FeatureSet = 0x0001, FeatureBridge = 0x0003, FeatureBatterySoc = 0x0104;

    /// <summary>The address Solaar found on a G522 dongle; tried before all the others.</summary>
    public const byte UsualAddress = 0x23;

    public const ushort G522ProductId = 0x0B18;

    /// <summary>The Centurion collection of a Logitech dongle or headset.</summary>
    public static bool IsCollection(HidCollection c) =>
        c.VendorId == Hidpp.LogitechVendorId && c.UsagePage == UsagePage && c.InputLength == FrameLength && c.OutputLength == FrameLength;

    /// <summary>
    /// The report id to talk with: the one the collection declares (0x51 first, as Solaar checks), otherwise 0x50 for the
    /// G522 and 0x51 for the rest.
    /// </summary>
    public static byte ChooseReport(IEnumerable<byte> declared, ushort productId)
    {
        var ids = declared.ToList();
        if (ids.Contains(PlainReport)) return PlainReport;
        if (ids.Contains(AddressedReport)) return AddressedReport;
        return productId == G522ProductId ? AddressedReport : PlainReport;
    }

    /// <summary>A whole 64-byte output report around <paramref name="payload"/>.</summary>
    /// <param name="address">Used by report 0x50 only.</param>
    public static byte[] Frame(byte reportId, byte address, byte[] payload)
    {
        var frame = new byte[FrameLength];
        int at = 0;
        frame[at++] = reportId;
        if (reportId == AddressedReport) frame[at++] = address;
        frame[at++] = (byte)(payload.Length + 1);
        frame[at++] = 0;
        Array.Copy(payload, 0, frame, at, Math.Min(payload.Length, FrameLength - at));
        return frame;
    }

    public static byte Tag(byte function) => (byte)((function << 4) | SoftwareId);

    /// <summary>A request to the device the frame is addressed to (the dongle, or a headset without a bridge).</summary>
    public static byte[] Request(byte featureIndex, byte function, params byte[] parameters) =>
        [featureIndex, Tag(function), .. parameters];

    /// <summary>A request to the headset behind the bridge at <paramref name="bridgeIndex"/> (bridge function 1, SendFragment).</summary>
    public static byte[] BridgeRequest(byte bridgeIndex, byte subIndex, byte subFunction, params byte[] parameters)
    {
        int length = 3 + parameters.Length;
        return [bridgeIndex, Tag(1), (byte)((length >> 8) & 0x0F), (byte)length, 0x00, subIndex, Tag(subFunction), .. parameters];
    }

    /// <summary>A received report, if it is a Centurion frame.</summary>
    public static CenturionFrame? Parse(byte[] report)
    {
        if (report.Length < 4) return null;
        byte reportId = report[0];
        byte? address = null;
        int at = 1;
        if (reportId == AddressedReport) address = report[at++];
        else if (reportId != PlainReport) return null;

        int length = report[at++];
        byte flags = report[at++];
        if (length < 1) return null;
        int count = Math.Min(length - 1, report.Length - at);
        var payload = new byte[count];
        Array.Copy(report, at, payload, 0, count);
        return new CenturionFrame(reportId, address, flags, payload);
    }

    /// <summary>The answer to a request sent with <see cref="Request"/>, if this payload is it.</summary>
    public static HidppReply? MatchReply(byte[] payload, byte featureIndex, byte function)
    {
        byte tag = Tag(function);
        if (payload.Length >= 4 && payload[0] == 0xFF && payload[1] == featureIndex && payload[2] == tag)
            return new HidppReply(Array.Empty<byte>(), payload[3]);
        if (payload.Length >= 2 && payload[0] == featureIndex && payload[1] == tag)
            return new HidppReply(payload.Skip(2).ToArray(), null);
        return null;
    }

    /// <summary>An event of the bridge at <paramref name="bridgeIndex"/>, if this payload is one (software id 0).</summary>
    public static CenturionBridgeEvent? ParseBridgeEvent(byte[] payload, byte bridgeIndex)
    {
        if (payload.Length < 4 || payload[0] != bridgeIndex) return null;
        int length = ((payload[2] & 0x0F) << 8) | payload[3];
        switch (payload[1])
        {
            case 0x00:
                // ConnectionStateChanged: the list of devices behind the bridge; empty when the headset went off.
                return new CenturionBridgeEvent.Connection(length > 0);

            case 0x10:
            {
                // MessageEvent: 0, sub-index, function|sw, data; an error is 0, FF, sub-index, function|sw, code.
                if (payload.Length < 7) return null;
                if (payload[5] == 0xFF)
                    return payload.Length >= 9 ? new CenturionBridgeEvent.Message(payload[6], payload[7], Array.Empty<byte>(), payload[8]) : null;
                int end = Math.Min(4 + length, payload.Length);
                byte[] data = end > 7 ? payload.Skip(7).Take(end - 7).ToArray() : Array.Empty<byte>();
                return new CenturionBridgeEvent.Message(payload[5], payload[6], data, null);
            }

            default:
                return null;
        }
    }

    /// <summary>The headset's answer to a request sent with <see cref="BridgeRequest"/>, if this message is it.</summary>
    public static HidppReply? MatchBridgeReply(CenturionBridgeEvent.Message message, byte subIndex, byte subFunction)
    {
        if (message.SubIndex != subIndex || message.Tag != Tag(subFunction)) return null;
        return new HidppReply(message.Data, message.Error);
    }

    /// <summary>True for a message the device sent by itself (software id 0) rather than as an answer.</summary>
    public static bool IsNotification(byte tag) => (tag & 0x0F) == 0;

    /// <summary>Battery from the data of feature 0x0104 (its getter or its event). Null when it is unusable.</summary>
    public static CenturionBattery? ParseBattery(byte[] data)
    {
        if (data.Length < 1) return null;
        byte state = data.Length >= 3 ? data[2] : (byte)0;
        if (state == 3) return new CenturionBattery(100, false);
        // 0 % is no reading: a headset that empty is off, and a false 0 would raise the low-battery alarm.
        if (data[0] is 0 or > 100 || state > 3) return null;
        return new CenturionBattery(data[0], state is 1 or 2);
    }

    /// <summary>The headset's name from the dongle's product string: "G522 LIGHTSPEED - Wireless Mode" → "Logitech G522 LIGHTSPEED".</summary>
    public static string? NameFromProduct(string product)
    {
        string name = product.Trim();
        int dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) name = name.Substring(0, dash).Trim();
        if (name.Length == 0) return null;
        return name.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) ? name : "Logitech " + name;
    }
}
