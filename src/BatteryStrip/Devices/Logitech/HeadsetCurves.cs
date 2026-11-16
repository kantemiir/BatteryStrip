using System.Collections.Generic;
using BatteryStrip.Devices.Polled;

namespace BatteryStrip.Devices.Logitech;

/// <summary>
/// Voltage-to-percent curves of Logitech headsets that report only their battery voltage (HID++ feature 0x1F20).
/// A headset's cell and electronics sag differently from a mouse's, so a generic Li-Po curve reads them too low.
/// The calibration points were measured by the HeadsetControl project (github.com/Sapd/HeadsetControl) and collected
/// per product id by Wireless Battery (github.com/krisgfx9/Wireless-Device-Battery-Monitor). Facts only, no code copied.
/// </summary>
internal static class HeadsetCurves
{
    private static readonly (int Millivolts, int Percent)[] G533 =
        [(4200, 100), (3850, 50), (3790, 30), (3750, 20), (3680, 5), (3330, 0)];

    private static readonly (int Millivolts, int Percent)[] G535 =
        [(4175, 100), (3817, 50), (3766, 30), (3730, 20), (3664, 5), (3310, 0)];

    private static readonly (int Millivolts, int Percent)[] G633 =
        [(4100, 100), (3950, 80), (3850, 60), (3750, 40), (3650, 20), (3500, 10), (3300, 5), (3150, 0)];

    private static readonly (int Millivolts, int Percent)[] GPro =
        [(4150, 100), (3830, 50), (3780, 30), (3740, 20), (3670, 5), (3320, 0)];

    private static readonly Dictionary<ushort, (int Millivolts, int Percent)[]> ByProduct = new()
    {
        [0x0A66] = G533,
        [0x0AC4] = G535,
        // G633, G635, G733, G933, G935 and their variants.
        [0x0A5C] = G633, [0x0A89] = G633, [0x0A5B] = G633, [0x0A87] = G633, [0x0AB5] = G633, [0x0AFE] = G633,
        // G PRO and G PRO X wireless.
        [0x0AA7] = GPro, [0x0AAA] = GPro, [0x0ABA] = GPro, [0x0AFB] = GPro,
    };

    /// <summary>Percent for the headset's own curve, or the generic Li-Po curve for any other product.</summary>
    public static int Percent(ushort productId, int millivolts) =>
        ByProduct.TryGetValue(productId, out var curve) ? BatteryMath.Interpolate(millivolts, curve) : LiPoBattery.Percent(millivolts);

    public static bool IsKnown(ushort productId) => ByProduct.ContainsKey(productId);
}
