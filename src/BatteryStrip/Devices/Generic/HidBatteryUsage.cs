using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.Polled;
using Microsoft.Win32.SafeHandles;

namespace BatteryStrip.Devices.Generic;

/// <summary>
/// Any HID product that declares a standard battery usage: Generic Device Controls "Battery Strength" (page 0x06,
/// usage 0x20) or Battery System "Absolute State Of Charge" (page 0x85, usage 0x65), as the USB HID Usage Tables name
/// them. Which report carries the value was collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor); the code here is written from the usage tables.
/// Only read requests are sent. Products of the vendors in <see cref="SkippedVendors"/> are never read here.
/// </summary>
internal sealed class GenericHidDriver : PolledHidDriver
{
    /// <summary>
    /// Vendors other drivers own, or whose devices a read disturbs. Logitech's G435 receiver, for one, drops its audio
    /// when it is touched.
    /// </summary>
    private static readonly HashSet<ushort> SkippedVendors = new()
    {
        0x046D, 0x25A7, 0x054C, 0x1532, 0x1038, 0x1B1C, 0x03F0, 0x0951, 0x258A, 0x3554, 0x373B, 0x3770, 0x045E,
    };

    /// <summary>The battery usages as (usage page, usage), from the USB HID Usage Tables.</summary>
    private static readonly (ushort Page, ushort Usage)[] BatteryUsages = { (0x06, 0x20), (0x85, 0x65) };

    private static readonly object FieldsGate = new();

    /// <summary>Battery fields of each collection, by path. An empty list means the collection declares no battery.</summary>
    private static readonly Dictionary<string, IReadOnlyList<BatteryField>> FieldsByPath = new(StringComparer.OrdinalIgnoreCase);

    public override string Owner => "hid";

    /// <summary>
    /// Whether the collection declares a battery usage. Passive: the collection is opened without access rights, its
    /// report descriptor is read and it is closed again. The answer is cached per path, so later refreshes stay cheap.
    /// </summary>
    internal static bool DeclaresBattery(HidCollection collection) => FieldsOf(collection).Count > 0;

    /// <summary>
    /// Maps a raw value onto 0..100 through its logical range. A range that cannot be used (maximum not above minimum)
    /// takes the value as a percentage when it lies in 0..100; any other value gives null.
    /// </summary>
    internal static int? ScalePercent(int value, int logicalMin, int logicalMax)
    {
        if (logicalMax > logicalMin)
            return BatteryMath.Clamp100((int)((value - (long)logicalMin) * 100 / ((long)logicalMax - logicalMin)));
        return value >= 0 && value <= 100 ? (int?)value : null;
    }

    public override bool Wants(PolledTarget target) =>
        !SkippedVendors.Contains(target.VendorId) && target.Collections.Any(DeclaresBattery);

    public override Task<IReadOnlyList<PolledReading>?> ReadAsync(PolledTarget target, CancellationToken cancellation) =>
        Task.Run(() => ReadNow(target), cancellation);

    /// <summary>One blocking read. The first battery value that answers gives the reading; none gives null.</summary>
    private static IReadOnlyList<PolledReading>? ReadNow(PolledTarget target)
    {
        foreach (HidCollection collection in target.Collections)
        {
            foreach (BatteryField field in FieldsOf(collection))
            {
                if (ReadPercent(collection.Path, field) is not int percent) continue;

                string name = target.Product.Length > 0 ? target.Product : Text.Current.Device;
                DeviceKind kind = DeviceGuess.FromCollections(target.AllPresent, target.VendorId, target.ProductId);
                if (kind == DeviceKind.Other) kind = DeviceGuess.FromName(name);
                string key = $"{target.VendorId:X4}:{target.ProductId:X4}";

                // The usage tables give no charging state for these usages.
                return new[] { new PolledReading(key, name, kind, new DeviceReport.Charge(percent, IsCharging: false)) };
            }
        }
        return null;
    }

    /// <summary>The battery fields of a collection, found once per path. Top-level pages 0x84 and 0x85 (power supplies) are skipped.</summary>
    private static IReadOnlyList<BatteryField> FieldsOf(HidCollection collection)
    {
        if (collection.UsagePage is 0x84 or 0x85) return Array.Empty<BatteryField>();

        lock (FieldsGate)
        {
            if (FieldsByPath.TryGetValue(collection.Path, out IReadOnlyList<BatteryField>? known)) return known;
        }

        (bool probed, IReadOnlyList<BatteryField> fields) = Probe(collection);
        // A collection Windows would not open is asked again on the next refresh rather than remembered as empty.
        if (!probed) return fields;

        lock (FieldsGate) FieldsByPath[collection.Path] = fields;
        return fields;
    }

    /// <summary>Opens the collection without access rights and reads its report descriptor. Probed is false when Windows refuses to open it.</summary>
    private static (bool Probed, IReadOnlyList<BatteryField> Fields) Probe(HidCollection collection)
    {
        using SafeFileHandle handle = HidNative.CreateFile(collection.Path, 0, HidNative.ShareReadWrite, IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return (false, Array.Empty<BatteryField>());
        if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return (false, Array.Empty<BatteryField>());

        try
        {
            return (true, FindFields(collection, preparsed));
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>The battery fields among the feature and input value caps of a collection, feature reports first.</summary>
    private static IReadOnlyList<BatteryField> FindFields(HidCollection collection, IntPtr preparsed)
    {
        if (HidNative.HidP_GetCaps(preparsed, out HidNative.HIDP_CAPS caps) != HidNative.HidpStatusSuccess)
            return Array.Empty<BatteryField>();

        // Rest[] starts after the five fixed fields: Rest[19] is NumberInputValueCaps, Rest[25] is NumberFeatureValueCaps.
        var fields = new List<BatteryField>();
        fields.AddRange(ValueFields(preparsed, HidNative.HidpFeature, caps.Rest[25], collection.FeatureLength));
        fields.AddRange(ValueFields(preparsed, HidNative.HidpInput, caps.Rest[19], collection.InputLength));
        return fields;
    }

    /// <summary>Walks the HIDP_VALUE_CAPS entries of one report type and keeps those that carry a battery usage.</summary>
    private static List<BatteryField> ValueFields(IntPtr preparsed, int reportType, ushort count, int reportLength)
    {
        var fields = new List<BatteryField>();
        if (count == 0 || reportLength <= 0) return fields;

        ushort filled = count;
        byte[] entries = new byte[count * HidNative.ValueCapsSize];
        if (HidNative.HidP_GetValueCaps(reportType, entries, ref filled, preparsed) != HidNative.HidpStatusSuccess) return fields;

        for (int i = 0; i < filled; i++)
        {
            // Byte offsets inside one HIDP_VALUE_CAPS entry (72 bytes): UsagePage 0, ReportID 2, LinkCollection 6,
            // IsRange 12, LogicalMin 40, LogicalMax 44, UsageMin (and the single Usage) 56, UsageMax 58 for ranges.
            int at = i * HidNative.ValueCapsSize;
            ushort page = BitConverter.ToUInt16(entries, at);
            ushort usageMin = BitConverter.ToUInt16(entries, at + 56);
            ushort usageMax = entries[at + 12] != 0 ? BitConverter.ToUInt16(entries, at + 58) : usageMin;

            foreach ((ushort batteryPage, ushort batteryUsage) in BatteryUsages)
            {
                if (page != batteryPage || batteryUsage < usageMin || batteryUsage > usageMax) continue;
                fields.Add(new BatteryField(
                    reportType,
                    entries[at + 2],
                    batteryPage,
                    batteryUsage,
                    BitConverter.ToUInt16(entries, at + 6),
                    BitConverter.ToInt32(entries, at + 40),
                    BitConverter.ToInt32(entries, at + 44),
                    reportLength));
            }
        }
        return fields;
    }

    /// <summary>Reads one battery value: a feature report, or an input report requested with a GET, then scaled to percent.</summary>
    private static int? ReadPercent(string path, BatteryField field)
    {
        using SafeFileHandle handle = HidNative.CreateFile(path, 0, HidNative.ShareReadWrite, IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;

        try
        {
            var report = new byte[field.ReportLength];
            report[0] = field.ReportId;
            bool answered = field.ReportType == HidNative.HidpFeature
                ? HidNative.HidD_GetFeature(handle, report, report.Length)
                : HidNative.HidD_GetInputReport(handle, report, report.Length);
            if (!answered) return null;

            if (HidNative.HidP_GetUsageValue(field.ReportType, field.UsagePage, field.LinkCollection, field.Usage, out uint raw,
                    preparsed, report, report.Length) != HidNative.HidpStatusSuccess)
                return null;
            return ScalePercent(unchecked((int)raw), field.LogicalMin, field.LogicalMax);
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>Where one battery value sits: report type, report id, usage, its logical range and the length of the report.</summary>
    private sealed record BatteryField(int ReportType, byte ReportId, ushort UsagePage, ushort Usage, ushort LinkCollection,
        int LogicalMin, int LogicalMax, int ReportLength);
}
