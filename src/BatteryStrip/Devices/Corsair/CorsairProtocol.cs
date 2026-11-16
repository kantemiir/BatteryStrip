using System;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.Corsair;

/// <summary>What a Void-family reply says about the headset.</summary>
internal enum VoidState
{
    /// <summary>Status 0: switched off or out of range.</summary>
    Offline,

    /// <summary>Status 1 or 2: on and reporting.</summary>
    Connected,

    /// <summary>Status 4 or 5: on the charging cable.</summary>
    Charging,

    /// <summary>Any other status: the headset answers, but the protocol does not say what the status means.</summary>
    Unknown,
}

/// <summary>A Void-family reply.</summary>
/// <param name="Percent">Battery level 0..100, with the microphone flag masked off.</param>
/// <param name="State">What the status byte says.</param>
internal readonly record struct VoidBattery(int Percent, VoidState State);

/// <summary>A Bragi GET reply: the status byte (0 = ok) and the value, a little-endian 32-bit number.</summary>
internal readonly record struct BragiReply(byte Status, uint Number);

/// <summary>
/// Wire format of Corsair wireless headsets, mice and keyboards (USB vendor 1B1C). Two families, both read-only:
/// <list type="bullet">
/// <item>
/// <term>Void family</term>
/// <description>
/// Void, Void Pro, Void Elite, HS70, HS80 and others. Request: bytes C9 64 written to the vendor output report.
/// Reply: byte 2 = battery level, bit 7 is the microphone flag; byte 4 = status (0 offline, 1-2 on, 4-5 charging).
/// Facts from HeadsetControl (github.com/Sapd/HeadsetControl), collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// </description>
/// </item>
/// <item>
/// <term>Bragi family</term>
/// <description>
/// Virtuoso, Void V2, HS80 MAX, Slipstream mice and keyboards. Request: 02 target 02 property (a numbered collection,
/// 64-byte output report) or 00 02 target 02 property (65-byte output report). Reply: byte 1 = source (00 for the device
/// itself, 01 for a device behind a receiver), byte 2 = 02, byte 3 = status (0 = ok), bytes 4-7 = value, little-endian.
/// Property 0F = battery, 0..1000 (tenths of a percent); property 10 = charge state (1 charging, 3 full).
/// Targets 09, 0A and 0B are devices paired to a receiver; 08 is the device itself. Facts from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// </description>
/// </item>
/// </list>
/// Only GET requests are sent. The software-mode switch and every SET are never sent.
/// </summary>
internal static class CorsairProtocol
{
    public const ushort VendorId = 0x1B1C;

    /// <summary>Target of the device itself (wired or Bluetooth), as opposed to the devices paired to a receiver.</summary>
    public const byte DeviceItself = 0x08;

    public const byte PropertyBattery = 0x0F;
    public const byte PropertyCharge = 0x10;

    /// <summary>Targets asked in this order: devices paired to a receiver, then the device itself.</summary>
    public static readonly byte[] BragiTargets = { 0x09, 0x0A, 0x0B, 0x08 };

    /// <summary>Void family products.</summary>
    public static readonly ushort[] VoidProductIds =
    {
        0x1B1C, 0x1B27, 0x0A14, 0x0A16, 0x0A17, 0x0A1D, 0x0A1A, 0x1B2A, 0x1B23,
        0x1B29, 0x0A55, 0x0A51, 0x0A52, 0x0A38, 0x0A4F, 0x0A2B, 0x0A75, 0x0A56,
    };

    /// <summary>Bragi products that are known by id. Other Corsair products are tried with the Bragi protocol too.</summary>
    public static readonly ushort[] BragiProductIds = { 0x2A08, 0x2A02, 0x0A97, 0x0A64, 0x0A62, 0x0A3E, 0x0A3D };

    public static bool IsVoid(ushort productId) => Array.IndexOf(VoidProductIds, productId) >= 0;

    /// <summary>The vendor collection a Void-family headset answers on: pages FFC5 or FF00, with an output report.</summary>
    public static bool IsVoidCollection(HidCollection collection) =>
        (collection.UsagePage == 0xFFC5 || collection.UsagePage == 0xFF00) && collection.OutputLength > 0;

    /// <summary>
    /// A collection a Bragi device answers on: 64 or 65 byte reports, on page FF42. A known Bragi product also
    /// answers on the other vendor pages and on interface 4.
    /// </summary>
    public static bool IsBragiCollection(HidCollection collection, ushort productId)
    {
        bool knownBragi = Array.IndexOf(BragiProductIds, productId) >= 0;
        bool sized = (collection.OutputLength is 64 or 65) && (collection.InputLength is 64 or 65);
        return sized && (collection.UsagePage == 0xFF42 || (knownBragi && (collection.UsagePage >= 0xFF00 || collection.Interface == 4)));
    }

    /// <summary>The Void battery request: bytes C9 and 64, written to the vendor output report (padded to its length).</summary>
    public static byte[] BuildVoidBatteryRequest() => new byte[] { 0xC9, 0x64 };

    /// <summary>Reads a Void-family reply. Null for a reply shorter than five bytes.</summary>
    public static VoidBattery? ParseVoidReply(byte[] payload)
    {
        if (payload.Length < 5) return null;

        VoidState state = payload[4] switch
        {
            0 => VoidState.Offline,
            1 or 2 => VoidState.Connected,
            4 or 5 => VoidState.Charging,
            _ => VoidState.Unknown,
        };
        return new VoidBattery(Math.Min(payload[2] & 0x7F, 100), state);
    }

    /// <summary>
    /// The Bragi GET for one property of one target. A numbered collection (64-byte output report) takes the request as
    /// it is; an unnumbered one (65-byte output report) gets a leading 00 report id.
    /// </summary>
    public static byte[] BuildBragiGet(byte targetId, byte property, bool numbered) =>
        numbered
            ? new byte[] { 0x02, targetId, 0x02, property }
            : new byte[] { 0x00, 0x02, targetId, 0x02, property };

    /// <summary>
    /// Reads a Bragi GET reply to <paramref name="targetId"/>. Null for a reply that is too short, comes from the other
    /// source (00 for the device itself, 01 for a device behind a receiver) or lacks the 02 marker: that is other traffic.
    /// A reply with a non-zero status is returned too; it says the target answered but has no value.
    /// </summary>
    public static BragiReply? ParseBragiReply(byte[] payload, byte targetId)
    {
        if (payload.Length < 8) return null;

        byte source = targetId == DeviceItself ? (byte)0x00 : (byte)0x01;
        if (payload[1] != source || payload[2] != 0x02) return null;

        uint number = (uint)payload[4] | (uint)payload[5] << 8 | (uint)payload[6] << 16 | (uint)payload[7] << 24;
        return new BragiReply(payload[3], number);
    }

    /// <summary>A Bragi battery value, 0..1000 (tenths of a percent), as a percent rounded to the nearest whole number.</summary>
    public static int BatteryPercent(uint value) => (int)Math.Min((value + 5) / 10, 100u);
}
