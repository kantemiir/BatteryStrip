using System;
using System.Collections.Generic;
using System.Text;
using BatteryStrip.Core;

namespace BatteryStrip.Devices.Logitech;

/// <summary>Answer to one HID++ request: either its parameters or an error code.</summary>
internal readonly record struct HidppReply(byte[] Parameters, byte? Error)
{
    public bool IsError => Error != null;
}

/// <summary>A battery reading from any of the HID++ battery features.</summary>
/// <param name="Percent">Null when the device is off or the reading is unusable.</param>
internal readonly record struct HidppBattery(int? Percent, bool IsCharging, bool IsEstimate);

/// <summary>A notification the receiver or the device sent on its own.</summary>
internal abstract record HidppEvent(byte Device)
{
    /// <summary>A device paired to the receiver was switched on (<see cref="Linked"/>) or off.</summary>
    public sealed record Connection(byte Device, bool Linked) : HidppEvent(Device);

    /// <summary>A feature of the device reported something, for example its battery.</summary>
    public sealed record Feature(byte Device, byte FeatureIndex, byte Function, byte[] Parameters) : HidppEvent(Device);
}

/// <summary>
/// Logitech HID++ 2.0, the protocol of Logitech mice, keyboards and headsets on Unifying, Bolt and Lightspeed
/// receivers, by cable and on Bluetooth. Every message is a "long" report:
/// <code>
/// 11  device  feature-index  function&lt;&lt;4|software-id  16 parameter bytes
/// </code>
/// Features are looked up by id through the root feature (index 0). Only getters are sent from here:
/// root lookups, the device name and type, and the battery state. Public references: Logitech's HID++ 2.0
/// feature documents and the Solaar project.
/// </summary>
internal static class Hidpp
{
    public const ushort LogitechVendorId = 0x046D;
    public const byte ShortReport = 0x10, LongReport = 0x11;
    public const int LongLength = 20;

    /// <summary>Marks our requests, so answers to other programs (G HUB, Options+) are not taken for ours.</summary>
    public const byte SoftwareId = 0x0D;

    /// <summary>Device index of a device connected directly (cable, Bluetooth, or a headset's own receiver).</summary>
    public const byte DirectDevice = 0xFF;

    public const ushort FeatureRoot = 0x0000, FeatureDeviceName = 0x0005;
    public const ushort FeatureBatteryStatus = 0x1000, FeatureBatteryVoltage = 0x1001, FeatureUnifiedBattery = 0x1004, FeatureAdcMeasurement = 0x1F20;

    /// <summary>Battery features in order of preference.</summary>
    public static readonly ushort[] BatteryFeatures = [FeatureUnifiedBattery, FeatureBatteryStatus, FeatureBatteryVoltage, FeatureAdcMeasurement];

    /// <summary>Receivers that carry up to six devices (Unifying, Nano, Lightspeed, Bolt).</summary>
    public static bool IsReceiver(ushort productId) => productId is
        0xC52B or 0xC532 or 0xC52F or 0xC531 or 0xC534 or 0xC537 or
        0xC539 or 0xC53A or 0xC53D or 0xC53F or 0xC541 or 0xC545 or 0xC547 or 0xC54D or
        0xC548;

    public static byte[] Request(byte device, byte featureIndex, byte function, params byte[] parameters)
    {
        var report = new byte[LongLength];
        report[0] = LongReport;
        report[1] = device;
        report[2] = featureIndex;
        report[3] = (byte)((function << 4) | SoftwareId);
        Array.Copy(parameters, 0, report, 4, Math.Min(parameters.Length, LongLength - 4));
        return report;
    }

    /// <summary>The answer to a request, if this report is it.</summary>
    public static HidppReply? MatchReply(byte[] report, byte device, byte featureIndex, byte function)
    {
        if (report.Length < 7 || report[1] != device) return null;
        byte tag = (byte)((function << 4) | SoftwareId);

        // HID++ 2.0 error: 11 dev FF feature-index tag error; HID++ 1.0 (receiver) error: 10 dev 8F feature-index tag error.
        bool error20 = report[0] == LongReport && report[2] == 0xFF && report[3] == featureIndex && report[4] == tag;
        bool error10 = report[0] == ShortReport && report[2] == 0x8F && report[3] == featureIndex && report[4] == tag;
        if (error20 || error10) return new HidppReply(Array.Empty<byte>(), report[5]);

        if (report[0] != LongReport || report.Length < LongLength || report[2] != featureIndex || report[3] != tag) return null;
        var parameters = new byte[LongLength - 4];
        Array.Copy(report, 4, parameters, 0, parameters.Length);
        return new HidppReply(parameters, null);
    }

    /// <summary>A notification, if this report is one (software id 0 or a receiver connection message).</summary>
    public static HidppEvent? ParseEvent(byte[] report)
    {
        if (report.Length < 7) return null;
        if (report[0] == ShortReport && report[2] == 0x41) return new HidppEvent.Connection(report[1], Linked: (report[4] & 0x40) == 0);
        if (report[0] == LongReport && report.Length >= LongLength && report[2] != 0xFF && (report[3] & 0x0F) == 0)
        {
            var parameters = new byte[LongLength - 4];
            Array.Copy(report, 4, parameters, 0, parameters.Length);
            return new HidppEvent.Feature(report[1], report[2], (byte)(report[3] >> 4), parameters);
        }
        return null;
    }

    /// <summary>Function that reads the battery for each battery feature (0x1004: GetStatus; others: function 0).</summary>
    public static byte BatteryFunction(ushort feature) => feature == FeatureUnifiedBattery ? (byte)1 : (byte)0;

    /// <summary>
    /// Battery from the parameters of a battery getter or a battery event.
    /// <paramref name="percentSupported"/> applies to 0x1004: without it the device reports only coarse levels.
    /// </summary>
    public static HidppBattery? ParseBattery(ushort feature, byte[] p, bool percentSupported = true)
    {
        if (p.Length < 3) return null;
        switch (feature)
        {
            case FeatureUnifiedBattery:
            {
                // state of charge, level flags (1 critical, 2 low, 4 good, 8 full), charging (0 no, 1 yes, 2 slow, 3 complete, 4 error).
                byte charging = p[2];
                if (charging == 4) return null;
                bool isCharging = charging is 1 or 2;
                if (charging == 3) return new HidppBattery(100, false, false);
                if (percentSupported && p[0] is > 0 and <= 100) return new HidppBattery(p[0], isCharging, false);
                int? fromLevel = (p[1] & 0x08) != 0 ? 100 : (p[1] & 0x04) != 0 ? 60 : (p[1] & 0x02) != 0 ? 20 : (p[1] & 0x01) != 0 ? 5 : null;
                return fromLevel == null ? null : new HidppBattery(fromLevel, isCharging, true);
            }

            case FeatureBatteryStatus:
            {
                // level %, next level %, status (0 discharging, 1 recharging, 2 almost full, 3 full, 4 slow recharge, 5+ errors).
                byte status = p[2];
                if (status > 4) return null;
                if (status == 3) return new HidppBattery(100, false, false);
                if (p[0] is 0 or > 100) return null;
                return new HidppBattery(p[0], status is 1 or 2 or 4, false);
            }

            case FeatureBatteryVoltage:
            {
                // millivolts (big endian), flags: bit 7 charging, low bits 1 = charge complete.
                int millivolts = (p[0] << 8) | p[1];
                bool isCharging = (p[2] & 0x80) != 0;
                if (isCharging && (p[2] & 0x07) == 1) return new HidppBattery(100, false, false);
                return millivolts is >= 2500 and <= 4600 ? new HidppBattery(LiPoBattery.Percent(millivolts), isCharging, true) : null;
            }

            case FeatureAdcMeasurement:
            {
                // millivolts (big endian), flags: bit 0 device on, bit 1 charging. Headsets use this one.
                if ((p[2] & 0x01) == 0) return new HidppBattery(null, false, true);
                int millivolts = (p[0] << 8) | p[1];
                return millivolts is >= 2500 and <= 4600 ? new HidppBattery(LiPoBattery.Percent(millivolts), (p[2] & 0x02) != 0, true) : null;
            }

            default:
                return null;
        }
    }

    /// <summary>Device type from the DeviceName feature (function 2).</summary>
    public static DeviceKind KindOf(byte type) => type switch
    {
        0 or 2 => DeviceKind.Keyboard,
        3 or 4 or 5 => DeviceKind.Mouse,
        8 => DeviceKind.Headset,
        10 or 11 or 12 => DeviceKind.Gamepad,
        14 => DeviceKind.Speaker,
        _ => DeviceKind.Other,
    };

    /// <summary>A chunk of the device name; names are ASCII, padded with zeros.</summary>
    public static string NameChunk(byte[] parameters, int wanted)
    {
        int count = Math.Min(wanted, parameters.Length);
        var text = new StringBuilder(count);
        for (int i = 0; i < count && parameters[i] != 0; i++) text.Append((char)parameters[i]);
        return text.ToString();
    }

    public static string KindName(DeviceKind kind) => App.Text.Current.LogitechDevice(kind);

    /// <summary>Device indexes to look at behind a connection.</summary>
    public static IReadOnlyList<byte> CandidateDevices(ushort productId) =>
        IsReceiver(productId) ? [1, 2, 3, 4, 5, 6] : [DirectDevice];
}
