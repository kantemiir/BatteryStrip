using System;
using System.Linq;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.Polled;

namespace BatteryStrip.Devices.HyperX;

/// <summary>What one HyperX battery read found.</summary>
/// <param name="Online">False when the headset is switched off; the receiver still answers.</param>
/// <param name="Percent">Null when no level is known in this round.</param>
/// <param name="IsEstimate">The percent is derived from the battery voltage, not reported by the headset.</param>
internal readonly record struct HyperXState(bool Online, int? Percent, bool IsCharging, bool IsEstimate = false);

/// <summary>The level and charge flag of one reply (Cloud Alpha 2).</summary>
internal readonly record struct HyperXCharge(int Percent, bool IsCharging);

/// <summary>The HID collection of a product that answers the battery queries.</summary>
internal enum HyperXRoute
{
    /// <summary>62-byte output and 62-byte input reports (Cloud III Wireless).</summary>
    Report62,

    /// <summary>Vendor page 0xFF90, usage 0x0303 (Cloud II Wireless, HP).</summary>
    PageFF90Usage0303,

    /// <summary>62-byte output reports (Cloud II Wireless, Kingston).</summary>
    Output62,

    /// <summary>Vendor page 0xFF00 or above, with output reports of at least 3 bytes (Cloud Alpha and Cloud Flight).</summary>
    VendorOutput,

    /// <summary>Vendor page 0xFF13, usage 0xFF00 (Cloud Alpha 2 Wireless).</summary>
    PageFF13Usage00,
}

/// <summary>Query layout of a model.</summary>
internal enum HyperXFamily
{
    Cloud3,
    Cloud2,
    Cloud2Kingston,
    CloudAlpha,
    CloudAlpha2,
    CloudFlight,
}

/// <summary>One product: its name, query layout and the collection that answers.</summary>
internal sealed record HyperXModel(ushort VendorId, ushort ProductId, string Name, HyperXFamily Family, HyperXRoute Route);

/// <summary>
/// Wire format of HyperX wireless headsets (HP vendor 03F0, Kingston vendor 0951). The Cloud III Wireless follows
/// HyperXWM (github.com/Medprj/HyperXWM); the other models follow HeadsetControl (github.com/Sapd/HeadsetControl).
/// The facts were collected from Wireless Battery (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// <para>
/// Every query is one output report; the reply is recognised by its leading bytes. Replies are read in their hidapi
/// form (see <see cref="Hidapi"/>): byte 0 is the first byte after the report id. Only battery queries are sent.
/// </para>
/// </summary>
internal static class HyperXProtocol
{
    /// <summary>Cloud Flight: a voltage above this, in millivolts, means the headset is on the charger.</summary>
    public const int ChargingAboveMillivolts = 0x100B;

    private static readonly HyperXModel[] Models =
    {
        new(0x03F0, 0x05B7, "HyperX Cloud III Wireless", HyperXFamily.Cloud3, HyperXRoute.Report62),
        new(0x03F0, 0x0C9D, "HyperX Cloud III Wireless", HyperXFamily.Cloud3, HyperXRoute.Report62),
        new(0x03F0, 0x0696, "HyperX Cloud II Wireless", HyperXFamily.Cloud2, HyperXRoute.PageFF90Usage0303),
        new(0x03F0, 0x018B, "HyperX Cloud II Wireless", HyperXFamily.Cloud2, HyperXRoute.PageFF90Usage0303),
        new(0x0951, 0x1718, "HyperX Cloud II Wireless", HyperXFamily.Cloud2Kingston, HyperXRoute.Output62),
        new(0x03F0, 0x098D, "HyperX Cloud Alpha Wireless", HyperXFamily.CloudAlpha, HyperXRoute.VendorOutput),
        new(0x03F0, 0x08BE, "HyperX Cloud Alpha 2 Wireless", HyperXFamily.CloudAlpha2, HyperXRoute.PageFF13Usage00),
        new(0x0951, 0x16C4, "HyperX Cloud Flight", HyperXFamily.CloudFlight, HyperXRoute.VendorOutput),
        new(0x0951, 0x1723, "HyperX Cloud Flight", HyperXFamily.CloudFlight, HyperXRoute.VendorOutput),
    };

    /// <summary>The model of a vendor and product id, or null when the product is not handled here.</summary>
    public static HyperXModel? Find(ushort vendorId, ushort productId) =>
        Models.FirstOrDefault(model => model.VendorId == vendorId && model.ProductId == productId);

    /// <summary>Whether a collection of a product is the one that answers the route's queries.</summary>
    public static bool Accepts(HyperXRoute route, HidCollection collection) => route switch
    {
        HyperXRoute.Report62 => collection.OutputLength == 62 && collection.InputLength == 62,
        HyperXRoute.PageFF90Usage0303 => collection.UsagePage == 0xFF90 && collection.Usage == 0x0303,
        HyperXRoute.Output62 => collection.OutputLength == 62,
        HyperXRoute.VendorOutput => collection.UsagePage >= 0xFF00 && collection.OutputLength >= 3,
        HyperXRoute.PageFF13Usage00 => collection.UsagePage == 0xFF13 && collection.Usage == 0xFF00,
        _ => false,
    };

    /// <summary>Cloud III query: report 66 followed by the query byte.</summary>
    public static byte[] Cloud3Request(byte query) => new byte[] { 0x66, query };

    /// <summary>Cloud II (HP) query: report 06 FF BB followed by the query byte.</summary>
    public static byte[] Cloud2Request(byte query) => new byte[] { 0x06, 0xFF, 0xBB, query };

    /// <summary>
    /// Cloud II (Kingston) query: a fixed 62-byte frame. The query byte sits at byte 15 and the rest is the frame the
    /// Kingston firmware expects.
    /// </summary>
    public static byte[] Cloud2KingstonRequest(byte query)
    {
        var frame = new byte[62];
        byte[] head = { 0x06, 0x00, 0x02, 0x00, 0x9A, 0x00, 0x00, 0x68, 0x4A, 0x8E, 0x0A, 0x00, 0x00, 0x00, 0xBB, query, 0x00 };
        Array.Copy(head, frame, head.Length);
        return frame;
    }

    /// <summary>Cloud Alpha query: report 21 BB followed by the query byte.</summary>
    public static byte[] CloudAlphaRequest(byte query) => new byte[] { 0x21, 0xBB, query };

    /// <summary>Cloud Alpha 2 query: a command byte and the sub-command that selects the value.</summary>
    public static byte[] CloudAlpha2Request(byte command, byte subcommand) => new byte[] { command, subcommand };

    /// <summary>Cloud Flight query: the battery voltage.</summary>
    public static byte[] CloudFlightRequest() => new byte[] { 0x21, 0xFF, 0x05 };

    /// <summary>Cloud III: byte 2 is non-zero while the headset is on.</summary>
    public static bool? Cloud3Connected(byte[] reply)
    {
        if (!Starts(reply, 5, 0x66, 0x82)) return null;
        return reply[2] != 0;
    }

    /// <summary>Cloud III: byte 2 is 1 while charging.</summary>
    public static bool? Cloud3Charging(byte[] reply)
    {
        if (!Starts(reply, 5, 0x66, 0x8A)) return null;
        return reply[2] == 1;
    }

    /// <summary>Cloud III: byte 4 is the level in percent; bytes 2 and 3 are the voltage and are not needed here.</summary>
    public static int? Cloud3Percent(byte[] reply)
    {
        if (!Starts(reply, 5, 0x66, 0x89) || reply[4] > 100) return null;
        return reply[4];
    }

    /// <summary>Cloud II (HP): byte 7 is the level in percent.</summary>
    public static int? Cloud2Percent(byte[] reply)
    {
        if (!Starts(reply, 8, 0x06, 0xFF, 0xBB, 0x02) || reply[7] > 100) return null;
        return reply[7];
    }

    /// <summary>Cloud II (HP): byte 4 is 1 while charging.</summary>
    public static bool? Cloud2Charging(byte[] reply)
    {
        if (!Starts(reply, 8, 0x06, 0xFF, 0xBB, 0x03)) return null;
        return reply[4] == 1;
    }

    /// <summary>Cloud II (Kingston): byte 7 is the level in percent.</summary>
    public static int? Cloud2KingstonPercent(byte[] reply)
    {
        if (!KingstonReply(reply, 0x02) || reply[7] > 100) return null;
        return reply[7];
    }

    /// <summary>Cloud II (Kingston): byte 4 is 1 while charging.</summary>
    public static bool? Cloud2KingstonCharging(byte[] reply)
    {
        if (!KingstonReply(reply, 0x03)) return null;
        return reply[4] == 1;
    }

    /// <summary>Cloud Alpha: byte 3 is 01 when the headset is off.</summary>
    public static bool? CloudAlphaConnected(byte[] reply)
    {
        if (!AlphaReply(reply, 0x03)) return null;
        return reply[3] != 0x01;
    }

    /// <summary>Cloud Alpha: byte 3 is 01 while charging.</summary>
    public static bool? CloudAlphaCharging(byte[] reply)
    {
        if (!AlphaReply(reply, 0x0C)) return null;
        return reply[3] == 0x01;
    }

    /// <summary>Cloud Alpha: byte 3 is the level in percent.</summary>
    public static int? CloudAlphaPercent(byte[] reply)
    {
        if (!AlphaReply(reply, 0x0B) || reply[3] > 100) return null;
        return reply[3];
    }

    /// <summary>Cloud Alpha 2: byte 2 is non-zero while the headset is on.</summary>
    public static bool? CloudAlpha2Connected(byte[] reply)
    {
        if (!Starts(reply, 8, 0x53, 0x01)) return null;
        return reply[2] != 0;
    }

    /// <summary>Cloud Alpha 2: byte 2 is the level in percent and byte 3 is 01 while charging.</summary>
    public static HyperXCharge? CloudAlpha2Level(byte[] reply)
    {
        if (!Starts(reply, 8, 0x51, 0x02) || reply[2] > 100) return null;
        return new HyperXCharge(reply[2], reply[3] == 0x01);
    }

    /// <summary>
    /// Cloud Flight: bytes 3 and 4 are the battery voltage in millivolts. The percent is estimated from the voltage;
    /// a voltage above <see cref="ChargingAboveMillivolts"/> means charging, with no percent.
    /// </summary>
    public static HyperXState? CloudFlightState(byte[] reply)
    {
        if (reply.Length < 5) return null;
        int millivolts = (reply[3] << 8) | reply[4];
        return new HyperXState(true, CloudFlightPercent(millivolts), millivolts > ChargingAboveMillivolts, true);
    }

    /// <summary>
    /// Percent from the Cloud Flight battery voltage, with the curve of the reference driver. Null while the headset
    /// is on the charger, where the voltage says nothing about the level.
    /// </summary>
    public static int? CloudFlightPercent(int millivolts)
    {
        if (millivolts > ChargingAboveMillivolts) return null;
        if (millivolts <= 3648) return BatteryMath.Clamp100((int)(0.00125 * millivolts));
        if (millivolts > 3975) return 100;

        double v = millivolts;
        double percent = 0.00000002547505 * Math.Pow(v, 4) - 0.0003900299 * Math.Pow(v, 3) + 2.238321 * v * v - 5706.256 * v + 5452299.0;
        return BatteryMath.Clamp100((int)Math.Round(percent, MidpointRounding.AwayFromZero));
    }

    /// <summary>True when the reply has at least <paramref name="length"/> bytes and starts with <paramref name="start"/>.</summary>
    private static bool Starts(byte[] reply, int length, params byte[] start)
    {
        if (reply.Length < length || reply.Length < start.Length) return false;
        for (int i = 0; i < start.Length; i++)
        {
            if (reply[i] != start[i]) return false;
        }
        return true;
    }

    /// <summary>Kingston reply: 0B in byte 0, 0xBB in byte 2 and the query echoed in byte 3. Byte 1 is not checked.</summary>
    private static bool KingstonReply(byte[] reply, byte query) =>
        reply.Length >= 8 && reply[0] == 0x0B && reply[2] == 0xBB && reply[3] == query;

    /// <summary>Cloud Alpha reply: 21 in byte 0 and the query echoed in byte 2. Byte 1 is not checked.</summary>
    private static bool AlphaReply(byte[] reply, byte query) =>
        reply.Length >= 4 && reply[0] == 0x21 && reply[2] == query;
}
