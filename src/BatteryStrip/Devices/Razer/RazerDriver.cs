using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.Polled;

namespace BatteryStrip.Devices.Razer;

/// <summary>
/// Razer wireless mice, keyboards and their dongles (USB vendor 1532). Asks each product for its battery level and
/// charging state with the feature-report protocol in <see cref="RazerProtocol"/>. The protocol facts are from OpenRazer's
/// protocol documentation (github.com/openrazer/openrazer), collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor). Read commands only.
/// </summary>
internal sealed class RazerDriver : PolledHidDriver
{
    /// <summary>Pause before the first reply is read, and between the polls of a device that says it is busy.</summary>
    private static readonly TimeSpan FirstReplyDelay = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan BusyPollDelay = TimeSpan.FromMilliseconds(40);

    /// <summary>The transaction id that answered last, per product id. It is tried first next time.</summary>
    private readonly ConcurrentDictionary<ushort, byte> _answeringIds = new();

    public override string Owner => "razer";

    public override bool Wants(PolledTarget target) =>
        target.VendorId == RazerProtocol.VendorId && target.Collections.Any(IsProtocolCollection);

    public override async Task<IReadOnlyList<PolledReading>?> ReadAsync(PolledTarget target, CancellationToken cancellation)
    {
        foreach (HidCollection collection in target.Collections.Where(IsProtocolCollection))
        {
            cancellation.ThrowIfCancellationRequested();
            using HidFeaturePort? port = HidFeaturePort.Open(collection);
            if (port == null) continue;

            DeviceReport? report = await ReadReportAsync(port, collection.FeatureLength, target.ProductId, cancellation).ConfigureAwait(false);
            if (report != null) return new[] { ReadingOf(target, report) };
        }
        return null;
    }

    /// <summary>
    /// Asks for the battery with each transaction id in turn, the one that answered last first. Returns the charge, an
    /// asleep device when some id reported it offline, or null when no id got an answer.
    /// </summary>
    private async Task<DeviceReport?> ReadReportAsync(HidFeaturePort port, int featureLength, ushort productId, CancellationToken cancellation)
    {
        bool offline = false;
        foreach (byte transactionId in TransactionIdsFor(productId))
        {
            RazerReply? battery = await CommandAsync(port, featureLength, transactionId, RazerProtocol.BatteryClass, RazerProtocol.BatteryLevelId, cancellation).ConfigureAwait(false);
            if (battery is { Status: RazerStatus.Ok } answer)
            {
                _answeringIds[productId] = transactionId;
                RazerReply? charging = await CommandAsync(port, featureLength, transactionId, RazerProtocol.BatteryClass, RazerProtocol.ChargingId, cancellation).ConfigureAwait(false);
                bool isCharging = charging is { Status: RazerStatus.Ok } state && RazerProtocol.IsCharging(state);
                return new DeviceReport.Charge(RazerProtocol.BatteryPercent(answer), isCharging);
            }

            // "Offline" can also mean the transaction id is wrong, so the remaining ids are still tried.
            offline |= battery is { Status: RazerStatus.Offline };
        }
        return offline ? new DeviceReport.Asleep() : null;
    }

    private IEnumerable<byte> TransactionIdsFor(ushort productId)
    {
        if (!_answeringIds.TryGetValue(productId, out byte known)) return RazerProtocol.TransactionIds;
        return new[] { known }.Concat(RazerProtocol.TransactionIds.Where(id => id != known));
    }

    /// <summary>
    /// Sends one read command and waits for its reply, asking again while the device says it is busy. Null when the
    /// transfer is refused or no usable reply comes back.
    /// </summary>
    private static async Task<RazerReply?> CommandAsync(HidFeaturePort port, int featureLength, byte transactionId, byte commandClass, byte commandId, CancellationToken cancellation)
    {
        try
        {
            port.Set(Hidapi.Pad(RazerProtocol.BuildRequest(transactionId, commandClass, commandId), featureLength));
            for (int poll = 0; poll < RazerProtocol.ReplyPolls; poll++)
            {
                await Task.Delay(poll == 0 ? FirstReplyDelay : BusyPollDelay, cancellation).ConfigureAwait(false);
                RazerReply? reply = RazerProtocol.ParseReply(port.Get(0, featureLength), commandClass, commandId);
                if (reply is not { Status: RazerStatus.Busy }) return reply;
            }
            return null;
        }
        catch (Win32Exception)
        {
            // A refused transfer is no answer for this transaction id; the next one is tried.
            return null;
        }
    }

    private static bool IsProtocolCollection(HidCollection collection) => collection.FeatureLength == RazerProtocol.FeatureLength;

    /// <summary>
    /// The reading of a product. The name is the product string without receiver words; a product without one gets the
    /// default name. Two connections of one mouse (its dongle and its cable) share the name, so they share the key.
    /// </summary>
    private static PolledReading ReadingOf(PolledTarget target, DeviceReport report)
    {
        string product = RazerProtocol.CleanName(target.Product);
        DeviceKind kind = DeviceGuess.FromCollections(target.AllPresent, target.VendorId, target.ProductId);
        if (kind == DeviceKind.Other) kind = DeviceGuess.FromName(product);
        string name = product.Length > 0 ? product : Text.Current.BrandDevice("Razer", kind);
        bool viaCable = target.Product.IndexOf("Wired", StringComparison.OrdinalIgnoreCase) >= 0;
        return new PolledReading(DeviceGuess.Slug(name), name, kind, report, viaCable);
    }
}
