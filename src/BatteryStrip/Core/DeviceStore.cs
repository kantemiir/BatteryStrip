using System;
using System.Collections.Generic;
using System.Linq;

namespace BatteryStrip.Core;

/// <summary>Last known charge of a device, kept across disconnects and program restarts.</summary>
/// <param name="WarnedAt">State of <see cref="LowBatteryAlarm"/> for this device.</param>
internal sealed record RememberedCharge(int Percent, bool IsEstimate, DateTime UpdatedUtc, int WarnedAt);

/// <summary>
/// The single list of connected devices and their charge. Device drivers write into it from their own
/// threads; the user interface reads immutable snapshots. All members are thread-safe.
/// </summary>
internal sealed class DeviceStore(Func<DateTime> utcNow)
{
    private sealed class Entry(DeviceDescriptor descriptor)
    {
        public DeviceDescriptor Descriptor = descriptor;
        public int? Percent;
        public bool IsCharging;
        public bool IsAsleep;
        public bool IsEstimate;
        public bool IsUnsupported;
        public ChargeLevel Level;
        public DateTime? UpdatedUtc;
        public int WarnedAt;

        /// <summary>A device is shown once there is something to show: a value or a definite "no data".</summary>
        public bool IsVisible => Percent != null || IsUnsupported;

        public DeviceSnapshot ToSnapshot() => new(
            Descriptor.Id, Descriptor.Name, Descriptor.Kind,
            Percent, IsCharging, IsAsleep, IsEstimate, Level, UpdatedUtc);
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _connected = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RememberedCharge> _remembered = new(StringComparer.Ordinal);
    private IReadOnlyList<DeviceSnapshot> _snapshot = Array.Empty<DeviceSnapshot>();

    /// <summary>Raised after the visible list changed. May come from any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when a device falls to a warning threshold (20 or 10). May come from any thread.</summary>
    public event Action<DeviceSnapshot, int>? LowBattery;

    /// <summary>Devices to show, in strip order.</summary>
    public IReadOnlyList<DeviceSnapshot> Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    /// <summary>
    /// Registers a connected device. If its charge is remembered from an earlier session, the device
    /// appears at once with that value, dimmed, until the first fresh reading.
    /// </summary>
    public void Connect(DeviceDescriptor descriptor)
    {
        Update(() =>
        {
            if (_connected.TryGetValue(descriptor.Id, out Entry? existing))
            {
                existing.Descriptor = descriptor;
                return;
            }

            var entry = new Entry(descriptor);
            if (_remembered.TryGetValue(descriptor.Id, out RememberedCharge? past))
            {
                entry.Percent = past.Percent;
                entry.IsEstimate = past.IsEstimate;
                entry.UpdatedUtc = past.UpdatedUtc;
                entry.WarnedAt = past.WarnedAt;
                entry.Level = ChargeLevels.Of(past.Percent);
                entry.IsAsleep = true;
            }
            _connected.Add(descriptor.Id, entry);
        });
    }

    /// <summary>Removes a device that was unplugged or switched off. Its last charge stays remembered.</summary>
    public void Disconnect(string id)
    {
        Update(() =>
        {
            if (!_connected.TryGetValue(id, out Entry? entry)) return;
            Remember(entry);
            _connected.Remove(id);
        });
    }

    /// <summary>Records what a driver just learned about a device. Unknown ids are ignored.</summary>
    public void Report(string id, DeviceReport report)
    {
        DeviceSnapshot? warnAbout = null;
        int threshold = 0;

        Update(() =>
        {
            if (!_connected.TryGetValue(id, out Entry? entry)) return;

            switch (report)
            {
                case DeviceReport.Charge charge:
                    int percent = Math.Max(0, Math.Min(100, charge.Percent));
                    entry.Level = entry.Percent == null ? ChargeLevels.Of(percent) : ChargeLevels.Next(entry.Level, percent);
                    entry.Percent = percent;
                    entry.IsCharging = charge.IsCharging;
                    entry.IsEstimate = charge.IsEstimate;
                    entry.IsAsleep = false;
                    entry.IsUnsupported = false;
                    entry.UpdatedUtc = utcNow();
                    entry.WarnedAt = LowBatteryAlarm.Step(entry.WarnedAt, percent, charge.IsCharging, out threshold);
                    if (threshold != 0) warnAbout = entry.ToSnapshot();
                    break;

                case DeviceReport.Asleep:
                    entry.IsAsleep = true;
                    entry.IsCharging = false;
                    break;

                case DeviceReport.Unsupported:
                    entry.IsUnsupported = true;
                    entry.Percent = null;
                    entry.IsCharging = false;
                    entry.IsAsleep = false;
                    break;
            }
        });

        if (warnAbout != null) LowBattery?.Invoke(warnAbout, threshold);
    }

    /// <summary>Loads charges saved by <see cref="Export"/> in an earlier session.</summary>
    public void Import(IEnumerable<KeyValuePair<string, RememberedCharge>> remembered)
    {
        lock (_gate)
        {
            foreach (var pair in remembered) _remembered[pair.Key] = pair.Value;
        }
    }

    /// <summary>Last known charge of every device seen so far, for saving to disk.</summary>
    public IReadOnlyDictionary<string, RememberedCharge> Export()
    {
        lock (_gate)
        {
            foreach (Entry entry in _connected.Values) Remember(entry);
            return new Dictionary<string, RememberedCharge>(_remembered, StringComparer.Ordinal);
        }
    }

    private void Remember(Entry entry)
    {
        if (entry.Percent is int percent && entry.UpdatedUtc is DateTime updated)
            _remembered[entry.Descriptor.Id] = new RememberedCharge(percent, entry.IsEstimate, updated, entry.WarnedAt);
    }

    private void Update(Action change)
    {
        bool changed;
        lock (_gate)
        {
            change();
            DeviceSnapshot[] next = _connected.Values
                .Where(e => e.IsVisible)
                .Select(e => e.ToSnapshot())
                .OrderBy(s => s.Kind)
                .ThenBy(s => s.Name, StringComparer.CurrentCulture)
                .ThenBy(s => s.Id, StringComparer.Ordinal)
                .ToArray();
            changed = !next.SequenceEqual(_snapshot);
            if (changed) _snapshot = next;
        }
        if (changed) Changed?.Invoke();
    }
}
