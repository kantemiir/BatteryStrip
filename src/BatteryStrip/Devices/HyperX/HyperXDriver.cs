using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.Polled;

namespace BatteryStrip.Devices.HyperX;

/// <summary>
/// Reads HyperX wireless headsets (HP vendor 03F0, Kingston vendor 0951) through <see cref="HyperXProtocol"/>. For a
/// known product, the collections that the model names are tried in order; the first one that answers gives the device.
/// Protocol facts from HyperXWM and HeadsetControl, collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// </summary>
internal sealed class HyperXDriver : PolledHidDriver
{
    private static readonly TimeSpan ReplyWait = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan Cloud3ReplyWait = TimeSpan.FromMilliseconds(800);

    public override string Owner => "hyperx";

    public override bool Wants(PolledTarget target) => HyperXProtocol.Find(target.VendorId, target.ProductId) != null;

    public override async Task<IReadOnlyList<PolledReading>?> ReadAsync(PolledTarget target, CancellationToken cancellation)
    {
        HyperXModel? model = HyperXProtocol.Find(target.VendorId, target.ProductId);
        if (model == null) return null;

        foreach (HidCollection collection in target.Collections)
        {
            if (!HyperXProtocol.Accepts(model.Route, collection)) continue;
            using HidExchange? exchange = HidExchange.Open(collection);
            if (exchange == null) continue;

            HyperXState? state = await ReadStateAsync(model.Family, exchange, cancellation).ConfigureAwait(false);
            if (state is HyperXState answered) return ToReadings(model, answered);
        }
        return null;
    }

    /// <summary>
    /// Runs the queries of one family. Null when the first query gets no reply, which means this collection does not
    /// speak the protocol; a later query that fails leaves its value out.
    /// </summary>
    private static async Task<HyperXState?> ReadStateAsync(HyperXFamily family, HidExchange exchange, CancellationToken cancellation)
    {
        switch (family)
        {
            case HyperXFamily.Cloud3:
                return await ReadCloud3Async(exchange, cancellation).ConfigureAwait(false);
            case HyperXFamily.Cloud2:
                return await ReadCloud2Async(exchange, HyperXProtocol.Cloud2Request, HyperXProtocol.Cloud2Percent,
                    HyperXProtocol.Cloud2Charging, cancellation).ConfigureAwait(false);
            case HyperXFamily.Cloud2Kingston:
                return await ReadCloud2Async(exchange, HyperXProtocol.Cloud2KingstonRequest, HyperXProtocol.Cloud2KingstonPercent,
                    HyperXProtocol.Cloud2KingstonCharging, cancellation).ConfigureAwait(false);
            case HyperXFamily.CloudAlpha:
                return await ReadCloudAlphaAsync(exchange, cancellation).ConfigureAwait(false);
            case HyperXFamily.CloudAlpha2:
                return await ReadCloudAlpha2Async(exchange, cancellation).ConfigureAwait(false);
            case HyperXFamily.CloudFlight:
                return await AskAsync<HyperXState>(exchange, HyperXProtocol.CloudFlightRequest(), HyperXProtocol.CloudFlightState,
                    ReplyWait, cancellation).ConfigureAwait(false);
            default:
                return null;
        }
    }

    private static async Task<HyperXState?> ReadCloud3Async(HidExchange exchange, CancellationToken cancellation)
    {
        bool? connected = await AskAsync<bool>(exchange, HyperXProtocol.Cloud3Request(0x82), HyperXProtocol.Cloud3Connected,
            Cloud3ReplyWait, cancellation).ConfigureAwait(false);
        if (connected is not bool online) return null;
        if (!online) return new HyperXState(false, null, false);

        bool? chargingReply = await AskAsync<bool>(exchange, HyperXProtocol.Cloud3Request(0x8A), HyperXProtocol.Cloud3Charging,
            Cloud3ReplyWait, cancellation).ConfigureAwait(false);
        int? percent = await AskAsync<int>(exchange, HyperXProtocol.Cloud3Request(0x89), HyperXProtocol.Cloud3Percent,
            Cloud3ReplyWait, cancellation).ConfigureAwait(false);
        return new HyperXState(true, percent, chargingReply == true);
    }

    /// <summary>Cloud II, HP or Kingston: the level query first, then the charging query. A headset with no reply to the first has no level.</summary>
    private static async Task<HyperXState?> ReadCloud2Async(HidExchange exchange, Func<byte, byte[]> request, Func<byte[], int?> level,
        Func<byte[], bool?> charging, CancellationToken cancellation)
    {
        int? percent = await AskAsync<int>(exchange, request(0x02), level, ReplyWait, cancellation).ConfigureAwait(false);
        if (percent is not int known) return null;

        bool? chargingReply = await AskAsync<bool>(exchange, request(0x03), charging, ReplyWait, cancellation).ConfigureAwait(false);
        return new HyperXState(true, known, chargingReply == true);
    }

    private static async Task<HyperXState?> ReadCloudAlphaAsync(HidExchange exchange, CancellationToken cancellation)
    {
        bool? connected = await AskAsync<bool>(exchange, HyperXProtocol.CloudAlphaRequest(0x03), HyperXProtocol.CloudAlphaConnected,
            ReplyWait, cancellation).ConfigureAwait(false);
        if (connected is not bool online) return null;
        if (!online) return new HyperXState(false, null, false);

        bool? chargingReply = await AskAsync<bool>(exchange, HyperXProtocol.CloudAlphaRequest(0x0C), HyperXProtocol.CloudAlphaCharging,
            ReplyWait, cancellation).ConfigureAwait(false);
        int? percent = await AskAsync<int>(exchange, HyperXProtocol.CloudAlphaRequest(0x0B), HyperXProtocol.CloudAlphaPercent,
            ReplyWait, cancellation).ConfigureAwait(false);
        return new HyperXState(true, percent, chargingReply == true);
    }

    private static async Task<HyperXState?> ReadCloudAlpha2Async(HidExchange exchange, CancellationToken cancellation)
    {
        bool? connected = await AskAsync<bool>(exchange, HyperXProtocol.CloudAlpha2Request(0x52, 0x01), HyperXProtocol.CloudAlpha2Connected,
            ReplyWait, cancellation).ConfigureAwait(false);
        if (connected is not bool online) return null;
        if (!online) return new HyperXState(false, null, false);

        HyperXCharge? level = await AskAsync<HyperXCharge>(exchange, HyperXProtocol.CloudAlpha2Request(0x50, 0x02),
            HyperXProtocol.CloudAlpha2Level, ReplyWait, cancellation).ConfigureAwait(false);
        if (level is not HyperXCharge charge) return new HyperXState(true, null, false);
        return new HyperXState(true, charge.Percent, charge.IsCharging);
    }

    /// <summary>
    /// Sends one query and returns the first reply that <paramref name="parse"/> accepts, parsed. Null when no such reply
    /// arrives in time.
    /// </summary>
    private static async Task<T?> AskAsync<T>(HidExchange exchange, byte[] request, Func<byte[], T?> parse, TimeSpan timeout, CancellationToken cancellation)
        where T : struct
    {
        byte[]? reply = await exchange.AskAsync(request, payload => parse(payload) != null, timeout, cancellation).ConfigureAwait(false);
        if (reply == null) return null;
        return parse(reply);
    }

    /// <summary>
    /// An offline headset is shown as asleep. An online headset on its charger without a level (a Cloud Flight on its
    /// charger reports none) keeps its last value with a "charging" note; otherwise a missing level shows nothing new.
    /// </summary>
    private static IReadOnlyList<PolledReading> ToReadings(HyperXModel model, HyperXState state)
    {
        string key = $"{model.VendorId:X4}:{model.ProductId:X4}";
        if (!state.Online) return new[] { new PolledReading(key, model.Name, DeviceKind.Headset, new DeviceReport.Asleep()) };
        if (state.Percent is not int percent)
        {
            return state.IsCharging
                ? new[] { new PolledReading(key, model.Name, DeviceKind.Headset, new DeviceReport.Stale(App.Text.Current.Charging)) }
                : Array.Empty<PolledReading>();
        }
        return new[] { new PolledReading(key, model.Name, DeviceKind.Headset, new DeviceReport.Charge(percent, state.IsCharging, state.IsEstimate)) };
    }
}
