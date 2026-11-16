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

namespace BatteryStrip.Devices.Corsair;

/// <summary>
/// Corsair wireless devices (USB vendor 1B1C). The Void family (headsets) is read with HeadsetControl's protocol
/// (github.com/Sapd/HeadsetControl). Bragi devices (Virtuoso, Void V2, Slipstream mice and keyboards) are read with GET
/// requests for the battery and charge state. The protocol facts are collected from Wireless Battery
/// (github.com/krisgfx9/Wireless-Device-Battery-Monitor). Read-only: no SET, and no switch to software mode.
/// </summary>
internal sealed class CorsairDriver : PolledHidDriver
{
    private static readonly TimeSpan VoidTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BragiTimeout = TimeSpan.FromMilliseconds(300);

    public override string Owner => "corsair";

    public override bool Wants(PolledTarget target)
    {
        if (target.VendorId != CorsairProtocol.VendorId) return false;
        return CorsairProtocol.IsVoid(target.ProductId)
            ? target.Collections.Any(CorsairProtocol.IsVoidCollection)
            : target.Collections.Any(collection => CorsairProtocol.IsBragiCollection(collection, target.ProductId));
    }

    public override Task<IReadOnlyList<PolledReading>?> ReadAsync(PolledTarget target, CancellationToken cancellation) =>
        CorsairProtocol.IsVoid(target.ProductId)
            ? ReadVoidAsync(target, cancellation)
            : ReadBragiAsync(target, cancellation);

    /// <summary>Void family: one battery request on the vendor collection, one reply with the level and the status.</summary>
    private static async Task<IReadOnlyList<PolledReading>?> ReadVoidAsync(PolledTarget target, CancellationToken cancellation)
    {
        foreach (HidCollection collection in target.Collections.Where(CorsairProtocol.IsVoidCollection))
        {
            cancellation.ThrowIfCancellationRequested();
            using HidExchange? exchange = HidExchange.Open(collection);
            if (exchange == null) continue;

            byte[]? reply = await AskAsync(exchange, CorsairProtocol.BuildVoidBatteryRequest(),
                payload => CorsairProtocol.ParseVoidReply(payload) != null, VoidTimeout, cancellation).ConfigureAwait(false);
            VoidBattery? battery = reply == null ? null : CorsairProtocol.ParseVoidReply(reply);
            if (battery == null) continue;

            DeviceReport report = battery.Value.State switch
            {
                VoidState.Offline => new DeviceReport.Asleep(),
                VoidState.Connected => new DeviceReport.Charge(battery.Value.Percent, false),
                VoidState.Charging => new DeviceReport.Charge(battery.Value.Percent, true),
                _ => new DeviceReport.Unsupported(),
            };
            string key = $"{CorsairProtocol.VendorId:X4}:{target.ProductId:X4}";
            return new[] { new PolledReading(key, NameOf(target, DeviceKind.Headset), DeviceKind.Headset, report) };
        }
        return null;
    }

    /// <summary>
    /// Bragi: a GET of the battery and the charge state for each target. The devices behind a receiver are asked first;
    /// the device itself only when none of them answered with a value. A receiver that answers but has nothing paired
    /// gives an empty list, which means "answered, nothing to show".
    /// </summary>
    private static async Task<IReadOnlyList<PolledReading>?> ReadBragiAsync(PolledTarget target, CancellationToken cancellation)
    {
        DeviceKind kind = DeviceGuess.FromName(target.Product);
        if (kind == DeviceKind.Other) kind = DeviceGuess.FromCollections(target.AllPresent, target.VendorId, target.ProductId);
        string name = NameOf(target, kind);

        foreach (HidCollection collection in target.Collections.Where(c => CorsairProtocol.IsBragiCollection(c, target.ProductId)))
        {
            cancellation.ThrowIfCancellationRequested();
            using HidExchange? exchange = HidExchange.Open(collection);
            if (exchange == null) continue;

            // 64-byte output report: the request goes out as it is. 65 bytes: a 00 report id leads the request.
            bool numbered = collection.OutputLength == 64;
            var readings = new List<PolledReading>();
            bool answered = false;
            foreach (byte targetId in CorsairProtocol.BragiTargets)
            {
                if (targetId == CorsairProtocol.DeviceItself && readings.Count > 0) break;

                BragiReply? battery = await GetAsync(exchange, numbered, targetId, CorsairProtocol.PropertyBattery, cancellation).ConfigureAwait(false);

                // A value of 0 or one above 1000 is read again, up to twice, as in the reference.
                for (int retry = 0; retry < 2 && battery is { Status: 0, Number: 0 or > 1000 }; retry++)
                {
                    battery = await GetAsync(exchange, numbered, targetId, CorsairProtocol.PropertyBattery, cancellation).ConfigureAwait(false);
                }
                if (battery == null) continue;   // nothing answered for this target

                answered = true;
                if (battery.Value.Status != 0 || battery.Value.Number > 1000) continue;   // empty slot, device off, or no usable value

                BragiReply? charge = await GetAsync(exchange, numbered, targetId, CorsairProtocol.PropertyCharge, cancellation).ConfigureAwait(false);
                bool charging = charge is { Status: 0, Number: 1 or 3 };

                string key = $"{CorsairProtocol.VendorId:X4}:{target.ProductId:X4}:{targetId:X2}";
                var report = new DeviceReport.Charge(CorsairProtocol.BatteryPercent(battery.Value.Number), charging);
                readings.Add(new PolledReading(key, name, kind, report));
            }

            if (answered) return readings;
        }
        return null;
    }

    /// <summary>One GET to one target. Null when no reply from that target came back in time.</summary>
    private static async Task<BragiReply?> GetAsync(HidExchange exchange, bool numbered, byte targetId, byte property, CancellationToken cancellation)
    {
        byte[]? reply = await AskAsync(exchange, CorsairProtocol.BuildBragiGet(targetId, property, numbered),
            payload => CorsairProtocol.ParseBragiReply(payload, targetId) != null, BragiTimeout, cancellation).ConfigureAwait(false);
        return reply == null ? null : CorsairProtocol.ParseBragiReply(reply, targetId);
    }

    /// <summary>A refused transfer counts as no reply, so the next request is still sent.</summary>
    private static async Task<byte[]?> AskAsync(HidExchange exchange, byte[] request, Func<byte[], bool> match, TimeSpan timeout, CancellationToken cancellation)
    {
        try
        {
            return await exchange.AskAsync(request, match, timeout, cancellation).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>The product string, or the default name for a brand device when Windows gives none.</summary>
    private static string NameOf(PolledTarget target, DeviceKind kind) =>
        target.Product.Length > 0 ? target.Product : Text.Current.BrandDevice("Corsair", kind);
}
