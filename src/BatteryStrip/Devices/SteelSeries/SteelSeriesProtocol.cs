using System;
using System.Linq;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.Polled;

namespace BatteryStrip.Devices.SteelSeries;

/// <summary>What one SteelSeries status reply says about the device.</summary>
/// <param name="Online">False when the device is switched off or out of range; the receiver still answers.</param>
/// <param name="Percent">Null when the reply carries no level.</param>
internal readonly record struct SteelSeriesState(bool Online, int? Percent, bool IsCharging);

/// <summary>The HID collection of a product that answers the battery query.</summary>
internal enum SteelSeriesRoute
{
    /// <summary>Vendor page 0xFF43, usage 0x0202 (Arctis 1 and Arctis 7 headsets).</summary>
    PageFF43,

    /// <summary>Vendor page 0xFFC0, usage 0x0001 (Nova family headsets).</summary>
    PageFFC0,

    /// <summary>USB interface 0, with output reports.</summary>
    Interface0,

    /// <summary>USB interface 2, with output reports.</summary>
    Interface2,

    /// <summary>USB interface 3, with output reports.</summary>
    Interface3,

    /// <summary>USB interface 4, with output reports.</summary>
    Interface4,
}

/// <summary>Reply layout of a model. Several models share one layout.</summary>
internal enum SteelSeriesFamily
{
    Arctis1,
    Arctis7,
    Arctis9,
    ArctisProWireless,
    NovaProWireless,

    /// <summary>Status block 00 B0: Arctis 7+.</summary>
    Arctis7Plus,

    /// <summary>Status block 00 B0: Nova 3P Wireless and Nova 5.</summary>
    Nova3P,

    /// <summary>Status block 00 B0: GameBuds, reports each bud that is out of the case; the lower level counts.</summary>
    GameBuds,

    /// <summary>Status block 00 B0: Nova 7 family, level in four steps.</summary>
    Nova7Steps,

    /// <summary>Status block 00 B0: Nova 7 family, level already in percent.</summary>
    Nova7Percent,

    /// <summary>Mouse: request 92 when on the cable, D2 when wireless.</summary>
    Aerox,

    /// <summary>Mouse: request 00 AA 01.</summary>
    Rival,

    /// <summary>Mouse: request 92, answered with a reply that starts 92 00.</summary>
    RivalPro,
}

/// <summary>One product: its name, kind, reply layout and the collection that answers.</summary>
/// <param name="ProductId">USB product id as Windows lists it.</param>
/// <param name="KeyProductId">Product id that names the device on the strip. For the cable id of a mouse this is the
/// wireless id, so a reading over the cable and one over the receiver are the same device.</param>
/// <param name="Wired">The product id is the cable connection of a mouse; its readings count as coming over the cable.</param>
internal sealed record SteelSeriesModel(ushort ProductId, ushort KeyProductId, string Name, DeviceKind Kind, SteelSeriesFamily Family, SteelSeriesRoute Route, bool Wired);

/// <summary>
/// Wire format of SteelSeries headsets and mice (USB vendor 1038). Headset requests follow HeadsetControl
/// (github.com/Sapd/HeadsetControl); mouse requests follow rivalcfg (github.com/flozz/rivalcfg), the Rival Pro
/// from rivalcfg's pull request 294. The facts were collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// <para>
/// Replies are read in their hidapi form (see <see cref="Hidapi"/>): byte 0 is the first byte after the report id.
/// Only the battery queries of those sources are sent; nothing here changes a setting of the device.
/// </para>
/// </summary>
internal static class SteelSeriesProtocol
{
    /// <summary>USB vendor id of SteelSeries.</summary>
    public const ushort VendorId = 0x1038;

    private static readonly SteelSeriesModel[] Models =
    {
        Headset(0x12B3, "SteelSeries Arctis 1 Wireless", SteelSeriesFamily.Arctis1, SteelSeriesRoute.PageFF43),
        Headset(0x12B6, "SteelSeries Arctis 1 Wireless", SteelSeriesFamily.Arctis1, SteelSeriesRoute.PageFF43),
        Headset(0x12D7, "SteelSeries Arctis 1 Wireless", SteelSeriesFamily.Arctis1, SteelSeriesRoute.PageFF43),
        Headset(0x1260, "SteelSeries Arctis 7", SteelSeriesFamily.Arctis7, SteelSeriesRoute.PageFF43),
        Headset(0x12AD, "SteelSeries Arctis 7", SteelSeriesFamily.Arctis7, SteelSeriesRoute.PageFF43),
        Headset(0x1252, "SteelSeries Arctis Pro", SteelSeriesFamily.Arctis7, SteelSeriesRoute.PageFF43),
        Headset(0x12C2, "SteelSeries Arctis 9", SteelSeriesFamily.Arctis9, SteelSeriesRoute.Interface0),
        Headset(0x1290, "SteelSeries Arctis Pro Wireless", SteelSeriesFamily.ArctisProWireless, SteelSeriesRoute.Interface0),
        Headset(0x12E0, "SteelSeries Arctis Nova Pro Wireless", SteelSeriesFamily.NovaProWireless, SteelSeriesRoute.Interface4),

        Headset(0x220E, "SteelSeries Arctis 7+", SteelSeriesFamily.Arctis7Plus, SteelSeriesRoute.PageFFC0),
        Headset(0x2212, "SteelSeries Arctis 7+", SteelSeriesFamily.Arctis7Plus, SteelSeriesRoute.PageFFC0),
        Headset(0x2216, "SteelSeries Arctis 7+", SteelSeriesFamily.Arctis7Plus, SteelSeriesRoute.PageFFC0),
        Headset(0x2236, "SteelSeries Arctis 7+", SteelSeriesFamily.Arctis7Plus, SteelSeriesRoute.PageFFC0),
        Headset(0x2269, "SteelSeries Arctis Nova 3P Wireless", SteelSeriesFamily.Nova3P, SteelSeriesRoute.PageFFC0),
        Headset(0x226D, "SteelSeries Arctis Nova 3P Wireless", SteelSeriesFamily.Nova3P, SteelSeriesRoute.PageFFC0),
        Headset(0x2232, "SteelSeries Arctis Nova 5", SteelSeriesFamily.Nova3P, SteelSeriesRoute.PageFFC0),
        Headset(0x2253, "SteelSeries Arctis Nova 5", SteelSeriesFamily.Nova3P, SteelSeriesRoute.PageFFC0),
        Headset(0x230A, "SteelSeries Arctis GameBuds", SteelSeriesFamily.GameBuds, SteelSeriesRoute.PageFFC0),
        Headset(0x2202, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Steps, SteelSeriesRoute.PageFFC0),
        Headset(0x2206, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Steps, SteelSeriesRoute.PageFFC0),
        Headset(0x220A, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Steps, SteelSeriesRoute.PageFFC0),
        Headset(0x223A, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Steps, SteelSeriesRoute.PageFFC0),
        Headset(0x227A, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Steps, SteelSeriesRoute.PageFFC0),
        Headset(0x22A4, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Steps, SteelSeriesRoute.PageFFC0),
        Headset(0x22A1, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Percent, SteelSeriesRoute.PageFFC0),
        Headset(0x227E, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Percent, SteelSeriesRoute.PageFFC0),
        Headset(0x2258, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Percent, SteelSeriesRoute.PageFFC0),
        Headset(0x229E, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Percent, SteelSeriesRoute.PageFFC0),
        Headset(0x22AD, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Percent, SteelSeriesRoute.PageFFC0),
        Headset(0x22A9, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Percent, SteelSeriesRoute.PageFFC0),
        Headset(0x22A5, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Percent, SteelSeriesRoute.PageFFC0),
        Headset(0x22A7, "SteelSeries Arctis Nova 7", SteelSeriesFamily.Nova7Percent, SteelSeriesRoute.PageFFC0),

        // Aerox and Prime: each wireless id is paired with the cable id listed right after it.
        Mouse(0x1838, 0x1838, "SteelSeries Aerox 3 Wireless", SteelSeriesFamily.Aerox, SteelSeriesRoute.Interface3, wired: false),
        Mouse(0x183A, 0x1838, "SteelSeries Aerox 3 Wireless", SteelSeriesFamily.Aerox, SteelSeriesRoute.Interface3, wired: true),
        Mouse(0x1852, 0x1852, "SteelSeries Aerox 5 Wireless", SteelSeriesFamily.Aerox, SteelSeriesRoute.Interface3, wired: false),
        Mouse(0x1854, 0x1852, "SteelSeries Aerox 5 Wireless", SteelSeriesFamily.Aerox, SteelSeriesRoute.Interface3, wired: true),
        Mouse(0x1858, 0x1858, "SteelSeries Aerox 9 Wireless", SteelSeriesFamily.Aerox, SteelSeriesRoute.Interface3, wired: false),
        Mouse(0x185A, 0x1858, "SteelSeries Aerox 9 Wireless", SteelSeriesFamily.Aerox, SteelSeriesRoute.Interface3, wired: true),
        Mouse(0x1840, 0x1840, "SteelSeries Prime Wireless", SteelSeriesFamily.Aerox, SteelSeriesRoute.Interface3, wired: false),
        Mouse(0x1842, 0x1840, "SteelSeries Prime Wireless", SteelSeriesFamily.Aerox, SteelSeriesRoute.Interface3, wired: true),

        Mouse(0x1830, 0x1830, "SteelSeries Rival 3 Wireless", SteelSeriesFamily.Rival, SteelSeriesRoute.Interface3, wired: false),
        Mouse(0x1872, 0x1872, "SteelSeries Rival 3 Wireless Gen 2", SteelSeriesFamily.Rival, SteelSeriesRoute.Interface3, wired: false),
        Mouse(0x172B, 0x172B, "SteelSeries Rival 650 Wireless", SteelSeriesFamily.Rival, SteelSeriesRoute.Interface0, wired: true),
        Mouse(0x1880, 0x1880, "SteelSeries Rival Pro", SteelSeriesFamily.RivalPro, SteelSeriesRoute.Interface2, wired: false),
    };

    /// <summary>The model of a product id, or null when the product is not handled here.</summary>
    public static SteelSeriesModel? Find(ushort productId) => Models.FirstOrDefault(model => model.ProductId == productId);

    /// <summary>Whether a collection of a product is the one that answers the route's battery query.</summary>
    public static bool Accepts(SteelSeriesRoute route, HidCollection collection) => route switch
    {
        SteelSeriesRoute.PageFF43 => collection.UsagePage == 0xFF43 && collection.Usage == 0x0202,
        SteelSeriesRoute.PageFFC0 => collection.UsagePage == 0xFFC0 && collection.Usage == 0x0001,
        SteelSeriesRoute.Interface0 => collection.Interface == 0 && collection.OutputLength > 0,
        SteelSeriesRoute.Interface2 => collection.Interface == 2 && collection.OutputLength > 0,
        SteelSeriesRoute.Interface3 => collection.Interface == 3 && collection.OutputLength > 0,
        SteelSeriesRoute.Interface4 => collection.Interface == 4 && collection.OutputLength > 0,
        _ => false,
    };

    /// <summary>The first request of a model: its status query, sent to the model's collection.</summary>
    public static byte[] BuildRequest(SteelSeriesModel model) => model.Family switch
    {
        SteelSeriesFamily.Arctis1 => new byte[] { 0x06, 0x12 },
        SteelSeriesFamily.Arctis7 => new byte[] { 0x06, 0x18 },
        SteelSeriesFamily.Arctis9 => new byte[] { 0x00, 0x20 },
        SteelSeriesFamily.ArctisProWireless => new byte[] { 0x41, 0xAA },
        SteelSeriesFamily.NovaProWireless => new byte[] { 0x06, 0xB0 },
        SteelSeriesFamily.Arctis7Plus or SteelSeriesFamily.Nova3P or SteelSeriesFamily.GameBuds
            or SteelSeriesFamily.Nova7Steps or SteelSeriesFamily.Nova7Percent => new byte[] { 0x00, 0xB0 },
        SteelSeriesFamily.Aerox => new byte[] { 0x00, model.Wired ? (byte)0x92 : (byte)0xD2 },
        SteelSeriesFamily.Rival => new byte[] { 0x00, 0xAA, 0x01 },
        SteelSeriesFamily.RivalPro => new byte[] { 0x00, 0x92 },
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };

    /// <summary>The level query the Arctis Pro Wireless needs in addition to its status query.</summary>
    public static byte[] BuildArctisProWirelessLevelRequest() => new byte[] { 0x40, 0xAA };

    /// <summary>How long a reply may take, in the family's protocol.</summary>
    public static TimeSpan TimeoutFor(SteelSeriesFamily family) => family switch
    {
        SteelSeriesFamily.Aerox or SteelSeriesFamily.Rival => TimeSpan.FromMilliseconds(300),
        SteelSeriesFamily.RivalPro => TimeSpan.FromMilliseconds(500),
        _ => TimeSpan.FromMilliseconds(1000),
    };

    /// <summary>
    /// Reads the status reply of a family. Null when the reply is too short or is not a reply of this family, so the
    /// caller can wait for another one.
    /// </summary>
    public static SteelSeriesState? Parse(SteelSeriesFamily family, byte[] reply) => family switch
    {
        SteelSeriesFamily.Arctis1 => ParseArctis1(reply),
        SteelSeriesFamily.Arctis7 => ParseArctis7(reply),
        SteelSeriesFamily.Arctis9 => ParseArctis9(reply),
        SteelSeriesFamily.ArctisProWireless => ParseArctisProWireless(reply),
        SteelSeriesFamily.NovaProWireless => ParseNovaProWireless(reply),
        SteelSeriesFamily.Arctis7Plus => ParseArctis7Plus(reply),
        SteelSeriesFamily.Nova3P => ParseNova3P(reply),
        SteelSeriesFamily.GameBuds => ParseGameBuds(reply),
        SteelSeriesFamily.Nova7Steps => ParseNova7Steps(reply),
        SteelSeriesFamily.Nova7Percent => ParseNova7Percent(reply),
        SteelSeriesFamily.Aerox => ParseAerox(reply),
        SteelSeriesFamily.Rival => ParseRival(reply),
        SteelSeriesFamily.RivalPro => ParseRivalPro(reply),
        _ => null,
    };

    /// <summary>Arctis 1: byte 2 is 01 when the headset is off, byte 3 is the level.</summary>
    public static SteelSeriesState? ParseArctis1(byte[] reply)
    {
        if (reply.Length < 4) return null;
        if (reply[2] == 0x01) return new SteelSeriesState(false, null, false);
        return new SteelSeriesState(true, BatteryMath.Clamp100(reply[3]), false);
    }

    /// <summary>Arctis 7 and Arctis Pro (2019): byte 2 is the level, 0 when the headset is off.</summary>
    public static SteelSeriesState? ParseArctis7(byte[] reply)
    {
        if (reply.Length < 3) return null;
        if (reply[2] == 0) return new SteelSeriesState(false, null, false);
        return new SteelSeriesState(true, BatteryMath.Clamp100(reply[2]), false);
    }

    /// <summary>Arctis 9: byte 3 is a raw level from 0x64 (empty) to 0x9A (full), byte 4 is 01 while charging. Always reported as on.</summary>
    public static SteelSeriesState? ParseArctis9(byte[] reply)
    {
        if (reply.Length < 5) return null;
        return new SteelSeriesState(true, BatteryMath.Map(reply[3], 0x64, 0x9A), reply[4] == 0x01);
    }

    /// <summary>Arctis Pro Wireless, status (41 AA): byte 0 is 02 when the headset is off.</summary>
    public static SteelSeriesState? ParseArctisProWireless(byte[] reply)
    {
        if (reply.Length < 1) return null;
        if (reply[0] == 0x02) return new SteelSeriesState(false, null, false);
        return new SteelSeriesState(true, null, false);
    }

    /// <summary>Arctis Pro Wireless, level (40 AA): byte 0 is a level from 0 to 4.</summary>
    public static int? ParseArctisProWirelessLevel(byte[] reply)
    {
        if (reply.Length < 1) return null;
        return BatteryMath.Map(reply[0], 0, 4);
    }

    /// <summary>Arctis Nova Pro Wireless: byte 15 is 01 when off and 02 while charging, byte 6 is a level from 0 to 8.</summary>
    public static SteelSeriesState? ParseNovaProWireless(byte[] reply)
    {
        if (reply.Length < 16) return null;
        if (reply[15] == 0x01) return new SteelSeriesState(false, null, false);
        return new SteelSeriesState(true, BatteryMath.Map(reply[6], 0, 8), reply[15] == 0x02);
    }

    /// <summary>Arctis 7+: byte 1 is 01 when off, byte 2 is a level from 0 to 4, byte 3 is 01 while charging.</summary>
    public static SteelSeriesState? ParseArctis7Plus(byte[] reply)
    {
        if (reply.Length < 7) return null;
        if (reply[1] == 0x01) return new SteelSeriesState(false, null, false);
        return new SteelSeriesState(true, BatteryMath.Map(reply[2], 0, 4), reply[3] == 0x01);
    }

    /// <summary>Nova 3P Wireless and Nova 5: byte 1 is 02 when off, byte 3 is the level, byte 4 is 01 while charging.</summary>
    public static SteelSeriesState? ParseNova3P(byte[] reply)
    {
        if (reply.Length < 7) return null;
        if (reply[1] == 0x02) return new SteelSeriesState(false, null, false);
        return new SteelSeriesState(true, BatteryMath.Clamp100(reply[3]), reply[4] == 0x01);
    }

    /// <summary>
    /// GameBuds: byte 3 or byte 4 is 03 for a bud out of the case, whose level is then byte 5 (left) or byte 6 (right).
    /// The lower of the buds out of the case is shown. Nothing out of the case means off.
    /// </summary>
    public static SteelSeriesState? ParseGameBuds(byte[] reply)
    {
        if (reply.Length < 7) return null;
        int lowest = -1;
        if (reply[3] == 0x03) lowest = reply[5];
        if (reply[4] == 0x03) lowest = lowest < 0 ? reply[6] : Math.Min(lowest, (int)reply[6]);
        if (lowest < 0) return new SteelSeriesState(false, null, false);
        return new SteelSeriesState(true, BatteryMath.Clamp100(lowest), false);
    }

    /// <summary>Nova 7 family with a level in four steps: byte 2 is the level from 0 to 4, byte 3 is 00 when off and 01 or 02 while charging.</summary>
    public static SteelSeriesState? ParseNova7Steps(byte[] reply) => ParseNova7(reply, stepped: true);

    /// <summary>Nova 7 family with a level in percent: byte 2 is the level from 0 to 100, byte 3 is 00 when off and 01 or 02 while charging.</summary>
    public static SteelSeriesState? ParseNova7Percent(byte[] reply) => ParseNova7(reply, stepped: false);

    /// <summary>
    /// Aerox and Prime, wired or wireless: byte 1 holds the level and the charging bit. The level is
    /// (raw - 1) * 5 percent; a raw value of 0 means the mouse is off.
    /// </summary>
    public static SteelSeriesState? ParseAerox(byte[] reply)
    {
        if (reply.Length < 2) return null;
        int raw = reply[1] & 0x7F;
        bool charging = (reply[1] & 0x80) != 0;
        if (raw == 0) return new SteelSeriesState(false, null, false);
        return new SteelSeriesState(true, BatteryMath.Clamp100((raw - 1) * 5), charging);
    }

    /// <summary>Rival 3 and Rival 650: byte 0 is the level in percent, byte 2 is non-zero while charging.</summary>
    public static SteelSeriesState? ParseRival(byte[] reply)
    {
        if (reply.Length < 3) return null;
        return new SteelSeriesState(true, BatteryMath.Clamp100(reply[0]), reply[2] != 0);
    }

    /// <summary>Rival Pro: a reply that starts 92 00 carries the mouse level in byte 2. A level above 100 is not accepted.</summary>
    public static SteelSeriesState? ParseRivalPro(byte[] reply)
    {
        if (reply.Length < 3 || reply[0] != 0x92 || reply[1] != 0x00 || reply[2] > 100) return null;
        return new SteelSeriesState(true, reply[2], false);
    }

    private static SteelSeriesState? ParseNova7(byte[] reply, bool stepped)
    {
        if (reply.Length < 7) return null;
        if (reply[3] == 0x00) return new SteelSeriesState(false, null, false);
        int level = stepped ? BatteryMath.Map(reply[2], 0, 4) : BatteryMath.Clamp100(reply[2]);
        return new SteelSeriesState(true, level, reply[3] == 0x01 || reply[3] == 0x02);
    }

    private static SteelSeriesModel Headset(ushort productId, string name, SteelSeriesFamily family, SteelSeriesRoute route) =>
        new(productId, productId, name, DeviceKind.Headset, family, route, Wired: false);

    private static SteelSeriesModel Mouse(ushort productId, ushort keyProductId, string name, SteelSeriesFamily family, SteelSeriesRoute route, bool wired) =>
        new(productId, keyProductId, name, DeviceKind.Mouse, family, route, wired);
}
