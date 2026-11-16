using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.Polled;

namespace BatteryStrip.Devices.Kysona;

/// <summary>
/// Kysona, Pulsar, ATK and VXE wireless mice and dongles, and the Hitscan Hyperlight receiver, read with the 17-byte
/// report 0x08 protocol of <see cref="KysonaProtocol"/>. A mouse seen over its cable and over its dongle shows as one
/// device, keyed by the name its product string gives.
/// Sources: the Linux hid-kysona driver by Lode Willems; the protocol facts were collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// </summary>
internal sealed class KysonaDriver : PolledHidDriver
{
    private const ushort AtkVendorId = 0x373B;
    private const ushort HitscanVendorId = 0x3770;

    /// <summary>How long a reply is waited for, per question.</summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Words a dongle appends to the mouse's name. A word that ends another comes first.</summary>
    private static readonly string[] DongleWords = { " Wireless Receiver", " Dongle", " Receiver", " 2.4G" };

    public override string Owner => "kysona";

    public override bool Wants(PolledTarget target) =>
        KysonaProtocol.Handles(target.VendorId, target.ProductId) && Candidates(target).Any();

    public override async Task<IReadOnlyList<PolledReading>?> ReadAsync(PolledTarget target, CancellationToken cancellation)
    {
        foreach (HidCollection collection in Candidates(target))
        {
            DeviceReport? report;
            try
            {
                report = await ReadCollectionAsync(collection, cancellation).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException)
            {
                Log.Info($"{Owner}: {collection.Path} failed ({exception.Message}).");
                continue;
            }

            if (report is not null) return new[] { ToReading(target, report) };
        }
        return null;
    }

    /// <summary>
    /// Asks one collection for the online status and, while the mouse is linked, for its battery. Null when nothing
    /// answers in this protocol. A mouse that is not linked, or whose battery question goes unanswered, is
    /// <see cref="DeviceReport.Asleep"/>; a battery reply out of range is <see cref="DeviceReport.Unsupported"/>.
    /// </summary>
    private static async Task<DeviceReport?> ReadCollectionAsync(HidCollection collection, CancellationToken cancellation)
    {
        using HidExchange? exchange = HidExchange.Open(collection);
        if (exchange is null) return null;

        byte[]? statusReply = await exchange.AskAsync(
            KysonaProtocol.BuildRequest(KysonaProtocol.CommandOnlineStatus),
            reply => KysonaProtocol.IsReplyTo(reply, KysonaProtocol.CommandOnlineStatus),
            ReplyTimeout,
            cancellation).ConfigureAwait(false);

        // Null when the status question went unanswered; then the battery question decides whether the collection is ours.
        bool? linked = null;
        if (statusReply is not null && KysonaProtocol.TryParseStatus(statusReply, out KysonaStatus status)) linked = status.Connected;
        if (linked == false) return new DeviceReport.Asleep();

        byte[]? batteryReply = await exchange.AskAsync(
            KysonaProtocol.BuildRequest(KysonaProtocol.CommandBattery),
            reply => KysonaProtocol.IsReplyTo(reply, KysonaProtocol.CommandBattery),
            ReplyTimeout,
            cancellation).ConfigureAwait(false);

        if (batteryReply is null && linked is null) return null;
        if (batteryReply is null) return new DeviceReport.Asleep(); // linked, but the battery question went unanswered this time
        if (KysonaProtocol.TryParseBattery(batteryReply, out KysonaBattery battery))
            return new DeviceReport.Charge(battery.Percent, battery.IsCharging);

        return new DeviceReport.Unsupported();
    }

    /// <summary>
    /// The reading for one product. The name is the product string without the dongle words, so a mouse on its cable and
    /// on its dongle share one key. A product with no usable name is keyed by its product id instead, so two such
    /// products do not merge.
    /// </summary>
    private static PolledReading ToReading(PolledTarget target, DeviceReport report)
    {
        string name = target.VendorId == HitscanVendorId ? "Hitscan Hyperlight" : CleanName(target.Product);
        string slug = DeviceGuess.Slug(name);
        string key = slug.Length > 0 ? slug : $"{target.VendorId:X4}:{target.ProductId:X4}";
        if (name.Length == 0) name = Text.Current.BrandDevice(target.VendorId == AtkVendorId ? "ATK" : "Pulsar", DeviceKind.Mouse);

        DeviceKind kind = DeviceGuess.FromCollections(target.AllPresent, target.VendorId, target.ProductId);
        return new PolledReading(key, name, kind == DeviceKind.Other ? DeviceKind.Mouse : kind, report);
    }

    /// <summary>The product string with the dongle words taken off the end, repeated until none is left.</summary>
    private static string CleanName(string product)
    {
        string name = product.Trim();
        bool removed = true;
        while (removed)
        {
            removed = false;
            foreach (string word in DongleWords)
            {
                if (name.Length <= word.Length || !name.EndsWith(word, StringComparison.OrdinalIgnoreCase)) continue;
                name = name.Substring(0, name.Length - word.Length).TrimEnd();
                removed = true;
            }
        }
        return name;
    }

    /// <summary>
    /// The collections that may carry the 17-byte reports: a dedicated 17/17 collection, or a vendor collection with at
    /// least 17 bytes each way (ATK and VXE receivers mix report 08 with longer reports). Tried in listing order.
    /// </summary>
    private static IEnumerable<HidCollection> Candidates(PolledTarget target) => target.Collections.Where(c =>
        (c.OutputLength == KysonaProtocol.FrameLength && c.InputLength == KysonaProtocol.FrameLength)
        || (c.IsVendorDefined && c.OutputLength >= KysonaProtocol.FrameLength && c.InputLength >= KysonaProtocol.FrameLength));
}
