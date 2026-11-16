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

namespace BatteryStrip.Devices.Polled;

/// <summary>What a driver read about one device.</summary>
/// <param name="Key">Identifies the device within the driver, for example "1532:00B7". The device id becomes "owner:key".
/// Two connections of one device (its receiver and, while it charges, its cable) give the same key, and the source shows one device.</param>
/// <param name="Name">Name to show when the user has not set one.</param>
/// <param name="ViaCable">The reading came over the device's own cable; it outranks a receiver, which may repeat a stale value.</param>
internal sealed record PolledReading(string Key, string Name, DeviceKind Kind, DeviceReport Report, bool ViaCable = false);

/// <summary>One USB product (or Bluetooth device) as Windows lists it: every HID collection it has.</summary>
/// <param name="AllPresent">Every collection on the computer, for drivers that look at sibling collections (for example to guess the device type).</param>
internal sealed record PolledTarget(ushort VendorId, ushort ProductId, Guid ContainerId, IReadOnlyList<HidCollection> Collections, IReadOnlyList<HidCollection> AllPresent)
{
    public string Product => Collections.Select(c => c.Product).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))?.Trim() ?? "";
}

/// <summary>
/// One vendor protocol read by asking the device every few minutes. The driver says which products it handles and how to
/// read them; <see cref="PolledHidSource"/> does the scheduling, the game mode, merging of two connections and the store.
/// </summary>
internal abstract class PolledHidDriver
{
    /// <summary>Prefix of device ids and the name under which devices are claimed, for example "razer".</summary>
    public abstract string Owner { get; }

    /// <summary>How often a device that answered is read again.</summary>
    public virtual TimeSpan Interval => TimeSpan.FromMinutes(5);

    /// <summary>Whether this driver handles the product. Must not talk to the device.</summary>
    public abstract bool Wants(PolledTarget target);

    /// <summary>
    /// Reads the device. Returns null when the device did not answer in this protocol (not this protocol, unplugged,
    /// busy); an empty list when it answered but has no device to show (a receiver with nothing paired).
    /// A device that is known but switched off is returned with <see cref="DeviceReport.Asleep"/>.
    /// </summary>
    public abstract Task<IReadOnlyList<PolledReading>?> ReadAsync(PolledTarget target, CancellationToken cancellation);
}

/// <summary>Runs one <see cref="PolledHidDriver"/>: one session per product, merged into devices on the strip.</summary>
internal sealed class PolledHidSource(DeviceStore store, DeviceNaming naming, ContainerClaims claims, PolledHidDriver driver) : IDeviceSource
{
    /// <summary>A reading older than this no longer counts when two connections disagree.</summary>
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(15);

    private readonly SessionSet<PolledSession> _sessions = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, MergedDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _answered = new();

    public PolledHidDriver Driver => driver;

    public void Refresh(IReadOnlyList<HidCollection> present)
    {
        var wanted = new Dictionary<string, Func<PolledSession>>(StringComparer.OrdinalIgnoreCase);
        var containers = new HashSet<Guid>();
        foreach (var group in present.GroupBy(c => (c.VendorId, c.ProductId, c.ContainerId)))
        {
            var target = new PolledTarget(group.Key.VendorId, group.Key.ProductId, group.Key.ContainerId, group.ToList(), present);
            // A more precise source listed earlier already shows this device.
            if (claims.IsClaimedByOther(driver.Owner, target.ContainerId, null)) continue;
            bool wants;
            try
            {
                wants = driver.Wants(target);
            }
            catch (Exception exception)
            {
                Log.Error($"{driver.Owner}: looking at {target.VendorId:X4}:{target.ProductId:X4} failed", exception);
                continue;
            }
            if (!wants) continue;

            containers.Add(target.ContainerId);
            string key = $"{target.VendorId:X4}:{target.ProductId:X4}:{target.ContainerId:N}";
            wanted[key] = () => new PolledSession(this, target);
        }

        lock (_gate)
        {
            _answered.IntersectWith(containers);
            claims.Set(driver.Owner, _answered.ToArray());
        }
        _sessions.Reconcile(wanted);
    }

    public void Wake(WakeReason reason) => _sessions.ForEach(session => session.Wake());

    public void Dispose() => _sessions.Dispose();

    internal void OnReadings(PolledSession link, IReadOnlyList<PolledReading> readings)
    {
        lock (_gate)
        {
            if (link.Ended) return;
            if (_answered.Add(link.Target.ContainerId)) claims.Set(driver.Owner, _answered.ToArray());

            var fed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime now = DateTime.UtcNow;
            foreach (PolledReading reading in readings)
            {
                string id = $"{driver.Owner}:{reading.Key}";
                fed.Add(id);
                if (!_devices.TryGetValue(id, out MergedDevice? device)) _devices.Add(id, device = new MergedDevice());
                device.Links[link] = (now, reading);
                Publish(id, device);
            }

            // A device this connection showed before and no longer mentions (switched off, unpaired) sleeps from its point of view.
            foreach (var (id, device) in _devices.Select(p => (p.Key, p.Value)).ToArray())
            {
                if (fed.Contains(id) || !device.Links.TryGetValue(link, out var last)) continue;
                device.Links[link] = (now, last.Reading with { Report = new DeviceReport.Asleep() });
                Publish(id, device);
            }
        }
    }

    /// <summary>A connection that answered before stopped answering.</summary>
    internal void OnSilence(PolledSession link)
    {
        lock (_gate)
        {
            if (link.Ended) return;
            DateTime now = DateTime.UtcNow;
            foreach (var (id, device) in _devices.Select(p => (p.Key, p.Value)).ToArray())
            {
                if (!device.Links.TryGetValue(link, out var last)) continue;
                device.Links[link] = (now, last.Reading with { Report = new DeviceReport.Asleep() });
                Publish(id, device);
            }
        }
    }

    /// <summary>A connection went away (unplugged); devices nobody feeds any more leave the strip.</summary>
    internal void OnLinkEnded(PolledSession link)
    {
        lock (_gate)
        {
            foreach (var (id, device) in _devices.Select(p => (p.Key, p.Value)).ToArray())
            {
                if (!device.Links.Remove(link)) continue;
                if (device.Links.Count > 0)
                {
                    Publish(id, device);
                    continue;
                }
                _devices.Remove(id);
                if (device.Connected)
                {
                    store.Disconnect(id);
                    Log.Info($"{driver.Owner}: {id} removed.");
                }
            }
        }
    }

    /// <summary>
    /// Shows the best of what the connections of one device say: a fresh reading over the cable first, then a reading
    /// that says "charging" (the cable is in, the receiver may not know yet), then the most recent one.
    /// </summary>
    private void Publish(string id, MergedDevice device)
    {
        DateTime freshSince = DateTime.UtcNow - Freshness;
        var charges = device.Links.Values.Where(l => l.Reading.Report is DeviceReport.Charge && l.At > freshSince).ToList();
        PolledReading? chosen = charges
            .OrderByDescending(l => l.Reading.ViaCable)
            .ThenByDescending(l => ((DeviceReport.Charge)l.Reading.Report).IsCharging)
            .ThenByDescending(l => l.At)
            .Select(l => l.Reading)
            .FirstOrDefault()
            ?? device.Links.Values.Where(l => l.Reading.Report is DeviceReport.Unsupported).Select(l => l.Reading).FirstOrDefault();

        if (chosen != null)
        {
            if (!device.Connected)
            {
                device.Connected = true;
                var descriptor = new DeviceDescriptor(id, naming.Resolve(id, chosen.Name), chosen.Kind);
                store.Connect(descriptor);
                Log.Info($"{driver.Owner}: found {descriptor.Name} [{id}], {descriptor.Kind}.");
            }
            if (!Equals(device.Shown, chosen.Report))
            {
                device.Shown = chosen.Report;
                Log.Info($"{id}: {Describe(chosen.Report)}{(chosen.ViaCable ? " (cable)" : "")}.");
            }
            store.Report(id, chosen.Report);
            return;
        }

        // Never seen with a charge: not shown at all, whatever the connections say.
        if (!device.Connected) return;

        // Awake but without a value this round (a headset on its charger that reports no level): the last value stays.
        if (device.Links.Values.Where(l => l.At > freshSince).Select(l => l.Reading.Report).OfType<DeviceReport.Stale>().FirstOrDefault() is DeviceReport.Stale stale)
        {
            if (!Equals(device.Shown, stale)) Log.Info($"{id}: {stale.Note}, no value this round.");
            device.Shown = stale;
            store.Report(id, stale);
            return;
        }

        if (device.Shown is not DeviceReport.Asleep) Log.Info($"{id}: asleep.");
        device.Shown = new DeviceReport.Asleep();
        store.Report(id, device.Shown);
    }

    private static string Describe(DeviceReport report) => report switch
    {
        DeviceReport.Charge c => $"{c.Percent} %{(c.IsCharging ? ", charging" : "")}{(c.IsEstimate ? ", estimated" : "")}",
        DeviceReport.Unsupported => "no charge reported",
        _ => report.GetType().Name.ToLowerInvariant(),
    };

    private sealed class MergedDevice
    {
        public readonly Dictionary<PolledSession, (DateTime At, PolledReading Reading)> Links = new();
        public bool Connected;
        public DeviceReport? Shown;
    }
}

/// <summary>
/// Reads one product on a schedule. Nothing is sent while a full-screen program runs. A product that never answers
/// in the driver's protocol is asked a few times and then left alone until it is plugged in again.
/// </summary>
internal sealed class PolledSession(PolledHidSource source, PolledTarget target) : DeviceSession
{
    private const int TriesBeforeGivingUp = 3;
    private static readonly TimeSpan RetryUnanswered = TimeSpan.FromMinutes(2);

    private readonly WakeSignal _wake = new();

    public PolledTarget Target => target;

    /// <summary>Set once the session is disposed; a read finishing afterwards is dropped.</summary>
    public volatile bool Ended;

    protected override string Name => $"{source.Driver.Owner} {target.VendorId:X4}:{target.ProductId:X4}";

    public void Wake() => _wake.Set();

    public override void Dispose()
    {
        base.Dispose();
        Ended = true;
        source.OnLinkEnded(this);
    }

    protected override async Task RunAsync(CancellationToken cancellation)
    {
        bool answered = false;
        int failures = 0;
        while (!cancellation.IsCancellationRequested)
        {
            if (FullScreen.IsActive())
            {
                await _wake.WaitAsync(FullScreen.RecheckAfter, cancellation).ConfigureAwait(false);
                continue;
            }

            IReadOnlyList<PolledReading>? readings = await ReadAsync(cancellation).ConfigureAwait(false);
            if (readings != null)
            {
                if (!answered) Log.Info($"{Name}: answers ({target.Product}).");
                answered = true;
                failures = 0;
                source.OnReadings(this, readings);
            }
            else if (answered)
            {
                source.OnSilence(this);
            }
            else if (++failures >= TriesBeforeGivingUp)
            {
                Log.Info($"{Name}: no answer after {failures} tries; left alone until it is plugged in again.");
                await Task.Delay(Timeout.Infinite, cancellation).ConfigureAwait(false);
            }

            await _wake.WaitAsync(answered ? source.Driver.Interval : RetryUnanswered, cancellation).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<PolledReading>?> ReadAsync(CancellationToken cancellation)
    {
        try
        {
            return await source.Driver.ReadAsync(target, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or TimeoutException or UnauthorizedAccessException)
        {
            Log.Info($"{Name}: read failed ({exception.Message}).");
            return null;
        }
    }
}
