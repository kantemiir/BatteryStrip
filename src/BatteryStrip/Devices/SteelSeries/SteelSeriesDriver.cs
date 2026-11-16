using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.Polled;

namespace BatteryStrip.Devices.SteelSeries;

/// <summary>
/// Reads SteelSeries headsets and mice (USB vendor 1038) through <see cref="SteelSeriesProtocol"/>. For a known product,
/// the collections that the model names are tried in order; the first one that answers gives the device. Protocol facts
/// from HeadsetControl and rivalcfg, collected from Wireless Battery (github.com/krisgfx9/Wireless-Device-Battery-Monitor).
/// </summary>
internal sealed class SteelSeriesDriver : PolledHidDriver
{
    public override string Owner => "steelseries";

    public override bool Wants(PolledTarget target) =>
        target.VendorId == SteelSeriesProtocol.VendorId && SteelSeriesProtocol.Find(target.ProductId) != null;

    public override async Task<IReadOnlyList<PolledReading>?> ReadAsync(PolledTarget target, CancellationToken cancellation)
    {
        SteelSeriesModel? model = SteelSeriesProtocol.Find(target.ProductId);
        if (model == null) return null;

        foreach (HidCollection collection in target.Collections)
        {
            if (!SteelSeriesProtocol.Accepts(model.Route, collection)) continue;
            using HidExchange? exchange = HidExchange.Open(collection);
            if (exchange == null) continue;

            SteelSeriesState? state = await ReadStateAsync(model, exchange, cancellation).ConfigureAwait(false);
            if (state is SteelSeriesState answered) return ToReadings(model, answered);
        }
        return null;
    }

    /// <summary>Asks the device for its status; null when the model's reply does not arrive on this collection.</summary>
    private static async Task<SteelSeriesState?> ReadStateAsync(SteelSeriesModel model, HidExchange exchange, CancellationToken cancellation)
    {
        TimeSpan timeout = SteelSeriesProtocol.TimeoutFor(model.Family);
        SteelSeriesState? state = await AskAsync<SteelSeriesState>(exchange, SteelSeriesProtocol.BuildRequest(model),
            reply => SteelSeriesProtocol.Parse(model.Family, reply), timeout, cancellation).ConfigureAwait(false);

        // The Arctis Pro Wireless gives its level with a second query, sent only while the headset is on.
        if (model.Family != SteelSeriesFamily.ArctisProWireless || state is not SteelSeriesState online || !online.Online) return state;
        int? level = await AskAsync<int>(exchange, SteelSeriesProtocol.BuildArctisProWirelessLevelRequest(),
            SteelSeriesProtocol.ParseArctisProWirelessLevel, timeout, cancellation).ConfigureAwait(false);
        return online with { Percent = level };
    }

    /// <summary>
    /// Sends one request and returns the first reply that <paramref name="parse"/> accepts, parsed. Null when no such
    /// reply arrives in time.
    /// </summary>
    private static async Task<T?> AskAsync<T>(HidExchange exchange, byte[] request, Func<byte[], T?> parse, TimeSpan timeout, CancellationToken cancellation)
        where T : struct
    {
        byte[]? reply = await exchange.AskAsync(request, payload => parse(payload) != null, timeout, cancellation).ConfigureAwait(false);
        if (reply == null) return null;
        return parse(reply);
    }

    /// <summary>
    /// An offline device is shown as asleep. An online device without a level has nothing to show this round, so no
    /// reading is returned for it.
    /// </summary>
    private static IReadOnlyList<PolledReading> ToReadings(SteelSeriesModel model, SteelSeriesState state)
    {
        string key = $"{SteelSeriesProtocol.VendorId:X4}:{model.KeyProductId:X4}";
        if (!state.Online) return new[] { new PolledReading(key, model.Name, model.Kind, new DeviceReport.Asleep(), model.Wired) };
        if (state.Percent is not int percent) return Array.Empty<PolledReading>();
        return new[] { new PolledReading(key, model.Name, model.Kind, new DeviceReport.Charge(percent, state.IsCharging), model.Wired) };
    }
}
