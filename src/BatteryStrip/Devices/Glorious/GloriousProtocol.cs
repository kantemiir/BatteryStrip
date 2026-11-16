using System.Collections.Generic;

namespace BatteryStrip.Devices.Glorious;

/// <summary>What a mouse's status byte says: awake, waking up, or asleep.</summary>
internal enum GloriousState
{
    Awake,
    WakingUp,
    Asleep,
}

/// <summary>One answer to the status request.</summary>
/// <param name="State">Byte 1 of the reply: A1 awake, A0 waking up, any other value of the A family asleep.</param>
/// <param name="Percent">Byte 8, 1 to 100. Null when there is no percent to show: the mouse is not awake, or the byte is above 100. A reported 0 is shown as 1.</param>
/// <param name="IsCharging">Byte 7 is 1.</param>
internal readonly record struct GloriousStatus(GloriousState State, int? Percent, bool IsCharging);

/// <summary>What the product table says about one USB product.</summary>
/// <param name="Model">Model name; null when the product id is not in the table.</param>
/// <param name="IsCable">The product is the mouse's own USB cable, which means the mouse is on its charger.</param>
internal readonly record struct GloriousProduct(string? Model, bool IsCable);

/// <summary>
/// Wire format of Glorious (USB vendor 258A) and Hitscan (vendor 3770) wireless mice, except the Hitscan Hyperlight
/// receiver 3770:0200 that <see cref="BatteryStrip.Devices.Kysona.KysonaProtocol"/> handles. The request is a 65-byte feature report with
/// report id 0; the answer is read back as a feature report with report id 0 of the same length.
/// <code>
/// request   byte 0 report id 0x00; byte 3 = 0x02; byte 4 = 0x02; byte 6 = 0x83; all other bytes zero
/// reply     byte 1 status: A1 awake, A0 waking up, A4 asleep; byte 6 = 0x83; byte 7 = charging (1); byte 8 = percent
/// </code>
/// A reply counts only when byte 6 is 0x83 and the high nibble of byte 1 is A.
/// Sources: glorious-indicator (github.com/ikajdan/glorious-indicator), glorious-ctl (github.com/louis4craft/glorious-ctl)
/// and the Hitscan web configurator. The protocol facts were collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// </summary>
internal static class GloriousProtocol
{
    public const ushort GloriousVendorId = 0x258A;
    public const ushort HitscanVendorId = 0x3770;
    public const ushort HitscanReceiverProductId = 0x0200;

    /// <summary>Length of the feature report in both directions.</summary>
    public const int FeatureLength = 65;

    /// <summary>Report id of the feature report: 0, the device does not number its reports.</summary>
    public const byte ReportId = 0x00;

    /// <summary>Model and cable flag per product id, both for the cable and for the receiver of each mouse.</summary>
    private static readonly Dictionary<(ushort VendorId, ushort ProductId), (string Model, bool IsCable)> Products = new()
    {
        [(GloriousVendorId, 0x2022)] = ("Glorious Model O Wireless", false),
        [(GloriousVendorId, 0x2011)] = ("Glorious Model O Wireless", true),
        [(GloriousVendorId, 0x2023)] = ("Glorious Model D Wireless", false),
        [(GloriousVendorId, 0x2012)] = ("Glorious Model D Wireless", true),
        [(GloriousVendorId, 0x2024)] = ("Glorious Model O- Wireless", false),
        [(GloriousVendorId, 0x2013)] = ("Glorious Model O- Wireless", true),
        [(GloriousVendorId, 0x2025)] = ("Glorious Model D- Wireless", false),
        [(GloriousVendorId, 0x2014)] = ("Glorious Model D- Wireless", true),
        [(GloriousVendorId, 0x2033)] = ("Glorious Model O 2 Wireless", false),
        [(GloriousVendorId, 0x2034)] = ("Glorious Model D 2 PRO", false),
        [(GloriousVendorId, 0x2036)] = ("Glorious Model D 2 PRO", false),
        [(GloriousVendorId, 0x201A)] = ("Glorious Model D 2 PRO", true),
        [(HitscanVendorId, 0x0136)] = ("Hitscan Hyperlight X", true),
        [(HitscanVendorId, 0x0194)] = ("Hitscan Hyperlight N", true),
    };

    /// <summary>The feature report that asks for the status, as the vendor software sends it.</summary>
    public static byte[] BuildRequest()
    {
        var report = new byte[FeatureLength];
        report[0] = ReportId;
        report[3] = 0x02;
        report[4] = 0x02;
        report[6] = 0x83;
        return report;
    }

    /// <summary>
    /// Products this protocol talks to: only those in the table. Vendor id 258A belongs to the chip maker (Sinowealth) and
    /// is shared by many keyboards and mice of other brands, whose firmware may read the same bytes as something else.
    /// </summary>
    public static bool Handles(ushort vendorId, ushort productId) => Products.ContainsKey((vendorId, productId));

    /// <summary>
    /// Reads a status reply. Returns false when the reply is not an answer of this protocol: too short, a marker is
    /// missing, or the status is outside the A family.
    /// </summary>
    public static bool TryParseReply(byte[] reply, out GloriousStatus status)
    {
        status = default;
        if (reply.Length < 9) return false;
        if (reply[6] != 0x83 || (reply[1] & 0xF0) != 0xA0) return false;

        GloriousState state = reply[1] switch
        {
            0xA1 => GloriousState.Awake,
            0xA0 => GloriousState.WakingUp,
            _ => GloriousState.Asleep,
        };

        int? percent = null;
        if (state == GloriousState.Awake && reply[8] <= 100) percent = reply[8] == 0 ? 1 : reply[8];
        status = new GloriousStatus(state, percent, IsCharging: reply[7] == 0x01);
        return true;
    }

    /// <summary>What the product table says about a product id. Model is null for an id the table does not know.</summary>
    public static GloriousProduct Lookup(ushort vendorId, ushort productId) =>
        Products.TryGetValue((vendorId, productId), out var found)
            ? new GloriousProduct(found.Model, found.IsCable)
            : new GloriousProduct(null, false);
}
