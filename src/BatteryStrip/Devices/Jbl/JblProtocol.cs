using System;

namespace BatteryStrip.Devices.Jbl;

/// <summary>
/// JBL portable speakers (Charge 5, Charge 6 and other models the JBL Portable app drives with its older
/// "AA" frames). The speaker answers over Bluetooth Low Energy on a vendor service; the request is the one the
/// app itself sends to show the battery. See research/devices.md for the captures.
/// </summary>
internal static class JblProtocol
{
    /// <summary>Bluetooth SIG company id of Harman, in the speaker's advertisement.</summary>
    public const ushort HarmanCompanyId = 0x0057;

    /// <summary>The vendor service ("excelpoint.com" in ASCII), its notify (answers) and write (requests) characteristics.</summary>
    public static readonly Guid Service = new("65786365-6c70-6f69-6e74-2e636f6d0000");
    public static readonly Guid Answers = new("65786365-6c70-6f69-6e74-2e636f6d0001");
    public static readonly Guid Requests = new("65786365-6c70-6f69-6e74-2e636f6d0002");

    private const byte Frame = 0xAA, DeviceInfo = 0x12, RequestToken = 0x13, Unsupported = 0xEE, BatteryToken = 0x44;

    /// <summary>"Device info, one token: battery" — AA 13 02 00 44. Nothing else is ever sent.</summary>
    public static byte[] BatteryRequest() => [Frame, RequestToken, 0x02, 0x00, BatteryToken];

    /// <summary>
    /// The Harman advertisement: product id (little-endian) at 0–1 and a CRC-16/ARC of the speaker's classic
    /// Bluetooth address at 6–7. The CRC ties the advertisement (sent from a changing random address) to the
    /// speaker paired with Windows. Null when the data is not in this layout.
    /// </summary>
    public static (ushort ProductId, ushort AddressCrc)? ParseAdvertisement(byte[] data)
    {
        if (data.Length < 8) return null;
        return ((ushort)(data[0] | data[1] << 8), (ushort)(data[6] | data[7] << 8));
    }

    /// <summary>CRC-16/ARC of a Bluetooth address, its six bytes taken most significant first ("78:66:F3:…").</summary>
    public static ushort AddressCrc(ulong address)
    {
        ushort crc = 0;
        for (int shift = 40; shift >= 0; shift -= 8)
        {
            crc ^= (byte)(address >> shift);
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>The answer to <see cref="BatteryRequest"/>: AA 12 03 00 44 &lt;value&gt;, bit 7 charging, bits 0–6 percent.</summary>
    public static (int Percent, bool IsCharging)? ParseBattery(byte[] frame)
    {
        if (frame.Length < 6 || frame[0] != Frame || frame[1] != DeviceInfo || frame[2] < 3 || frame[4] != BatteryToken) return null;
        int percent = frame[5] & 0x7F;
        return percent <= 100 ? (percent, (frame[5] & 0x80) != 0) : null;
    }

    /// <summary>"Unsupported command" (AA EE …): a model on another protocol generation.</summary>
    public static bool IsRejection(byte[] frame) => frame.Length >= 2 && frame[0] == Frame && frame[1] == Unsupported;
}
