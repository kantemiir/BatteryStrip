using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.Polled;

namespace BatteryStrip.Devices.Glorious;

/// <summary>
/// Glorious and Hitscan wireless mice, read with the feature-report protocol of <see cref="GloriousProtocol"/>.
/// A mouse seen over its USB cable and over its receiver shows as one device, keyed by its model name.
/// Sources: glorious-indicator, glorious-ctl and the Hitscan web configurator; the protocol facts were collected from
/// Wireless Battery (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// </summary>
internal sealed class GloriousDriver : PolledHidDriver
{
    /// <summary>Wait between the request and the answer. Hitscan mice answer more slowly.</summary>
    private static readonly TimeSpan GloriousReplyDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan HitscanReplyDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>Wait before the second request, sent when the mouse says it is waking up.</summary>
    private static readonly TimeSpan WakingUpRetry = TimeSpan.FromMilliseconds(400);

    public override string Owner => "glorious";

    public override bool Wants(PolledTarget target) =>
        GloriousProtocol.Handles(target.VendorId, target.ProductId)
        && target.Collections.Any(c => c.FeatureLength == GloriousProtocol.FeatureLength);

    public override async Task<IReadOnlyList<PolledReading>?> ReadAsync(PolledTarget target, CancellationToken cancellation)
    {
        TimeSpan replyDelay = target.VendorId == GloriousProtocol.HitscanVendorId ? HitscanReplyDelay : GloriousReplyDelay;
        foreach (HidCollection collection in target.Collections)
        {
            if (collection.FeatureLength != GloriousProtocol.FeatureLength) continue;
            GloriousStatus? status = await ReadCollectionAsync(collection, replyDelay, cancellation).ConfigureAwait(false);
            if (status is GloriousStatus answer) return new[] { ToReading(target, answer) };
        }
        return null;
    }

    /// <summary>
    /// Asks one collection for the status. Null when the collection does not answer in this protocol. A "waking up"
    /// answer is asked once more after a pause; the last answer counts.
    /// </summary>
    private static async Task<GloriousStatus?> ReadCollectionAsync(HidCollection collection, TimeSpan replyDelay, CancellationToken cancellation)
    {
        using HidFeaturePort? port = HidFeaturePort.Open(collection);
        if (port is null) return null;

        GloriousStatus? status = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            byte[] reply;
            try
            {
                port.Set(GloriousProtocol.BuildRequest());
                await Task.Delay(replyDelay, cancellation).ConfigureAwait(false);
                reply = port.Get(GloriousProtocol.ReportId, GloriousProtocol.FeatureLength);
            }
            catch (Win32Exception)
            {
                break; // the transfer failed: the answer from the first attempt, if there was one, stands
            }

            if (!GloriousProtocol.TryParseReply(reply, out GloriousStatus answer)) break;
            status = answer;
            if (answer.State != GloriousState.WakingUp) break;
            await Task.Delay(WakingUpRetry, cancellation).ConfigureAwait(false);
        }
        return status;
    }

    /// <summary>
    /// The reading for one product. A mouse that is asleep or still waking up shows as asleep. An awake mouse shows its
    /// percent, and a cable product always counts as charging. Unknown products are keyed by their product id, so two
    /// unknown mice do not merge into one device.
    /// </summary>
    private static PolledReading ToReading(PolledTarget target, GloriousStatus status)
    {
        GloriousProduct product = GloriousProtocol.Lookup(target.VendorId, target.ProductId);

        DeviceReport report;
        if (status.State != GloriousState.Awake) report = new DeviceReport.Asleep();
        else if (status.Percent is int percent) report = new DeviceReport.Charge(percent, product.IsCable || status.IsCharging);
        else report = new DeviceReport.Unsupported();

        string name = product.Model
            ?? (target.VendorId == GloriousProtocol.HitscanVendorId ? "Hitscan Hyperlight" : Text.Current.BrandDevice("Glorious", DeviceKind.Mouse));
        string key = product.Model is null
            ? $"{target.VendorId:X4}:{target.ProductId:X4}"
            : DeviceGuess.Slug(product.Model);
        return new PolledReading(key, name, DeviceKind.Mouse, report, ViaCable: product.IsCable);
    }
}
