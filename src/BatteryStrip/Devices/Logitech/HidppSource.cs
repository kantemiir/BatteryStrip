using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.Logitech;

/// <summary>Logitech devices that speak HID++ 2.0: through Unifying, Bolt or Lightspeed receivers, by cable or Bluetooth.</summary>
internal sealed class HidppSource(DeviceStore store, DeviceNaming naming, ContainerClaims claims) : IDeviceSource
{
    public const string Owner = "logitech";

    private readonly SessionSet<HidppSession> _sessions = new();

    public void Refresh(IReadOnlyList<HidCollection> present)
    {
        var wanted = new Dictionary<string, Func<HidppSession>>(StringComparer.OrdinalIgnoreCase);
        var containers = new List<Guid>();

        // One physical connection = one container; its long-report collection carries requests and answers.
        foreach (var connection in present.Where(c => c.VendorId == Hidpp.LogitechVendorId).GroupBy(c => (c.ProductId, c.ContainerId, Interface(c.Path))))
        {
            HidCollection? longReports = connection.FirstOrDefault(IsLongCollection);
            if (longReports == null) continue;
            HidCollection? shortReports = connection.FirstOrDefault(c => c.UsagePage == 0xFF00 && c.Usage == 0x01 && c.InputLength == 7);

            containers.Add(longReports.ContainerId);
            wanted[longReports.Path] = () => new HidppSession(store, naming, longReports, shortReports);
        }

        claims.Set(Owner, containers);
        _sessions.Reconcile(wanted);
    }

    public void Wake(WakeReason reason) => _sessions.ForEach(session => session.Wake());

    public void Dispose() => _sessions.Dispose();

    /// <summary>HID++ long reports: usage page FF00 usage 02 on receivers and mice, FF43 on headsets; 20 bytes both ways.</summary>
    private static bool IsLongCollection(HidCollection c) =>
        (c.UsagePage == 0xFF00 && c.Usage == 0x02 || c.UsagePage == 0xFF43) && c.InputLength == Hidpp.LongLength && c.OutputLength == Hidpp.LongLength;

    /// <summary>The "mi_02" part of a HID path, so two interfaces of one composite device are not mixed up.</summary>
    private static string Interface(string path)
    {
        int at = path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? "" : path.Substring(at + 1, 5).ToLowerInvariant();
    }
}

/// <summary>What is known about one device behind a connection.</summary>
internal sealed class HidppDevice(byte index, DeviceDescriptor descriptor, byte batteryIndex, ushort batteryFeature, bool percentSupported)
{
    public byte Index { get; } = index;
    public DeviceDescriptor Descriptor { get; } = descriptor;
    public byte BatteryIndex { get; } = batteryIndex;
    public ushort BatteryFeature { get; } = batteryFeature;
    public bool PercentSupported { get; } = percentSupported;
}

/// <summary>
/// One receiver or directly connected device. Finds the devices behind it, reads their battery every few minutes
/// and follows the notifications they send (switched on or off, battery changed) in between.
/// </summary>
internal sealed class HidppSession(DeviceStore store, DeviceNaming naming, HidCollection longReports, HidCollection? shortReports) : DeviceSession
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly Dictionary<byte, HidppDevice> _devices = new();
    private readonly HashSet<byte> _toProbe = new();
    private readonly WakeSignal _wake = new();
    private HidOutputPort? _output;
    private HidReportPump? _pump;

    protected override string Name => $"Logitech {longReports.ProductId:X4}";

    public void Wake() => _wake.Set();

    public override void Dispose()
    {
        base.Dispose();
        foreach (HidppDevice device in _devices.Values) store.Disconnect(device.Descriptor.Id);
    }

    protected override async Task RunAsync(CancellationToken cancellation)
    {
        HidInputPort? longInput = HidInputPort.Open(longReports);
        HidInputPort? shortInput = shortReports != null ? HidInputPort.Open(shortReports) : null;
        _output = HidOutputPort.Open(longReports);
        if (longInput == null || _output == null)
        {
            Log.Warn($"{Name}: cannot open the HID++ collection.");
            longInput?.Dispose();
            shortInput?.Dispose();
            _output?.Dispose();
            return;
        }

        using (_output)
        using (_pump = shortInput != null ? new HidReportPump(longInput, shortInput) : new HidReportPump(longInput))
        {
            foreach (byte index in Hidpp.CandidateDevices(longReports.ProductId)) _toProbe.Add(index);

            while (!cancellation.IsCancellationRequested && !_pump.IsDead)
            {
                if (FullScreen.IsActive())
                {
                    // During a game nothing is sent; battery notifications the devices send by themselves still count.
                    await ListenAsync(FullScreen.RecheckAfter, cancellation, quiet: true).ConfigureAwait(false);
                    continue;
                }

                foreach (byte index in _toProbe.ToArray())
                {
                    if (await ProbeAsync(index, cancellation).ConfigureAwait(false)) _toProbe.Remove(index);
                }
                foreach (HidppDevice device in _devices.Values.ToArray()) await ReadBatteryAsync(device, cancellation).ConfigureAwait(false);

                await ListenAsync(PollInterval, cancellation).ConfigureAwait(false);

                // Devices that did not answer are looked for again on every round.
                foreach (byte index in Hidpp.CandidateDevices(longReports.ProductId).Where(i => !_devices.ContainsKey(i))) _toProbe.Add(index);
            }
        }
    }

    /// <summary>Handles notifications until <paramref name="duration"/> passes or <see cref="Wake"/> is called.</summary>
    /// <param name="quiet">Only take in what devices send; answer nothing with requests of our own.</param>
    private async Task ListenAsync(TimeSpan duration, CancellationToken cancellation, bool quiet = false)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task wake = _wake.WaitAsync(duration, stop.Token).ContinueWith(_ => { }, TaskScheduler.Default);
        try
        {
            while (!wake.IsCompleted)
            {
                Task<byte[]?> next = _pump!.NextAsync(TimeSpan.FromMinutes(1), stop.Token);
                if (await Task.WhenAny(next, wake).ConfigureAwait(false) != next) return;
                if (await next.ConfigureAwait(false) is byte[] report) await HandleEventAsync(report, cancellation, quiet).ConfigureAwait(false);
            }
        }
        finally
        {
            stop.Cancel();
        }
    }

    private async Task HandleEventAsync(byte[] report, CancellationToken cancellation, bool quiet)
    {
        switch (Hidpp.ParseEvent(report))
        {
            case HidppEvent.Connection connection:
                if (!connection.Linked)
                {
                    if (_devices.TryGetValue(connection.Device, out HidppDevice? off)) store.Report(off.Descriptor.Id, new DeviceReport.Asleep());
                }
                else if (quiet)
                {
                    // Read on the next round, once the full-screen program is gone.
                    if (!_devices.ContainsKey(connection.Device)) _toProbe.Add(connection.Device);
                }
                else if (_devices.TryGetValue(connection.Device, out HidppDevice? on))
                {
                    await ReadBatteryAsync(on, cancellation).ConfigureAwait(false);
                }
                else if (await ProbeAsync(connection.Device, cancellation).ConfigureAwait(false))
                {
                    _toProbe.Remove(connection.Device);
                }
                break;

            case HidppEvent.Feature feature when _devices.TryGetValue(feature.Device, out HidppDevice? device)
                && feature.FeatureIndex == device.BatteryIndex && feature.Function == 0:
                Apply(device, Hidpp.ParseBattery(device.BatteryFeature, feature.Parameters, device.PercentSupported));
                break;
        }
    }

    /// <summary>Asks whether a device answers at this index and, if it has a battery, starts showing it.</summary>
    private async Task<bool> ProbeAsync(byte index, CancellationToken cancellation)
    {
        // Root.GetProtocolVersion: a HID++ 2.0 device answers with its version; an empty slot or a device that is off returns an error.
        HidppReply? version = await RequestAsync(index, 0, 1, cancellation, 0, 0, 0x5A).ConfigureAwait(false);
        if (version is not HidppReply v || v.IsError || v.Parameters[0] < 2) return false;

        ushort feature = 0;
        byte featureIndex = 0;
        foreach (ushort candidate in Hidpp.BatteryFeatures)
        {
            if (await FeatureIndexAsync(index, candidate, cancellation).ConfigureAwait(false) is byte found)
            {
                feature = candidate;
                featureIndex = found;
                break;
            }
        }
        if (featureIndex == 0)
        {
            Log.Info($"{Name}: device {index} has no battery feature.");
            return true;
        }

        bool percentSupported = true;
        if (feature == Hidpp.FeatureUnifiedBattery)
        {
            HidppReply? capabilities = await RequestAsync(index, featureIndex, 0, cancellation).ConfigureAwait(false);
            if (capabilities is HidppReply c && !c.IsError) percentSupported = (c.Parameters[1] & 0x02) != 0;
        }

        var (name, kind) = await NameAsync(index, cancellation).ConfigureAwait(false);
        string id = index == Hidpp.DirectDevice ? $"logi:{longReports.ProductId:X4}" : $"logi:{longReports.ProductId:X4}:{index}";
        var descriptor = new DeviceDescriptor(id, naming.Resolve(id, name ?? Hidpp.KindName(kind)), kind);
        var device = new HidppDevice(index, descriptor, featureIndex, feature, percentSupported);
        _devices[index] = device;
        store.Connect(descriptor);
        Log.Info($"{Name}: found {descriptor.Name} [{id}], battery feature {feature:X4}.");
        await ReadBatteryAsync(device, cancellation).ConfigureAwait(false);
        return true;
    }

    private async Task ReadBatteryAsync(HidppDevice device, CancellationToken cancellation)
    {
        HidppReply? reply = await RequestAsync(device.Index, device.BatteryIndex, Hidpp.BatteryFunction(device.BatteryFeature), cancellation).ConfigureAwait(false);
        if (reply is not HidppReply r || r.IsError)
        {
            // Asleep, switched off or out of range: the receiver answers for it with an error.
            store.Report(device.Descriptor.Id, new DeviceReport.Asleep());
            return;
        }
        Apply(device, Hidpp.ParseBattery(device.BatteryFeature, r.Parameters, device.PercentSupported));
    }

    private void Apply(HidppDevice device, HidppBattery? battery)
    {
        if (battery is HidppBattery { Percent: int percent } b)
            store.Report(device.Descriptor.Id, new DeviceReport.Charge(percent, b.IsCharging, b.IsEstimate));
        else
            store.Report(device.Descriptor.Id, new DeviceReport.Asleep());
    }

    private async Task<byte?> FeatureIndexAsync(byte index, ushort feature, CancellationToken cancellation)
    {
        HidppReply? reply = await RequestAsync(index, 0, 0, cancellation, (byte)(feature >> 8), (byte)feature).ConfigureAwait(false);
        return reply is HidppReply r && !r.IsError && r.Parameters[0] != 0 ? r.Parameters[0] : null;
    }

    /// <summary>The device's own name and type (feature 0x0005), when it has them.</summary>
    private async Task<(string? Name, DeviceKind Kind)> NameAsync(byte index, CancellationToken cancellation)
    {
        if (await FeatureIndexAsync(index, Hidpp.FeatureDeviceName, cancellation).ConfigureAwait(false) is not byte nameIndex) return (null, DeviceKind.Other);

        DeviceKind kind = DeviceKind.Other;
        if (await RequestAsync(index, nameIndex, 2, cancellation).ConfigureAwait(false) is HidppReply { IsError: false } type) kind = Hidpp.KindOf(type.Parameters[0]);

        if (await RequestAsync(index, nameIndex, 0, cancellation).ConfigureAwait(false) is not HidppReply { IsError: false } count) return (null, kind);
        int length = Math.Min((int)count.Parameters[0], 64);
        var name = new System.Text.StringBuilder();
        while (name.Length < length)
        {
            if (await RequestAsync(index, nameIndex, 1, cancellation, (byte)name.Length).ConfigureAwait(false) is not HidppReply { IsError: false } chunk) break;
            string part = Hidpp.NameChunk(chunk.Parameters, length - name.Length);
            if (part.Length == 0) break;
            name.Append(part);
        }
        string text = name.ToString().Trim();
        return (text.Length > 0 ? (text.StartsWith("G ", StringComparison.Ordinal) || text.StartsWith("MX", StringComparison.Ordinal) ? "Logitech " + text : text) : null, kind);
    }

    /// <summary>Sends a getter and waits for its answer; notifications arriving meanwhile are handled too.</summary>
    private async Task<HidppReply?> RequestAsync(byte device, byte featureIndex, byte function, CancellationToken cancellation, params byte[] parameters)
    {
        try
        {
            _output!.Write(Hidpp.Request(device, featureIndex, function, parameters));
        }
        catch (Win32Exception exception)
        {
            Log.Warn($"{Name}: request to device {device} failed ({exception.Message}).");
            return null;
        }

        DateTime until = DateTime.UtcNow + ReplyTimeout;
        var later = new List<byte[]>();
        try
        {
            while (DateTime.UtcNow < until)
            {
                byte[]? report = await _pump!.NextAsync(until - DateTime.UtcNow, cancellation).ConfigureAwait(false);
                if (report == null) break;
                if (Hidpp.MatchReply(report, device, featureIndex, function) is HidppReply reply) return reply;
                later.Add(report);
            }
            return null;
        }
        finally
        {
            foreach (byte[] report in later)
            {
                if (Hidpp.ParseEvent(report) is HidppEvent.Feature feature && _devices.TryGetValue(feature.Device, out HidppDevice? known)
                    && feature.FeatureIndex == known.BatteryIndex && feature.Function == 0)
                {
                    Apply(known, Hidpp.ParseBattery(known.BatteryFeature, feature.Parameters, known.PercentSupported));
                }
            }
        }
    }
}
