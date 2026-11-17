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

/// <summary>Logitech headsets on the Centurion transport (G522 LIGHTSPEED, PRO X 2 LIGHTSPEED): see <see cref="Centurion"/>.</summary>
internal sealed class CenturionSource(DeviceStore store, DeviceNaming naming, ContainerClaims claims) : IDeviceSource
{
    public const string Owner = "logitech-centurion";

    private readonly SessionSet<CenturionSession> _sessions = new();

    public void Refresh(IReadOnlyList<HidCollection> present)
    {
        var wanted = new Dictionary<string, Func<CenturionSession>>(StringComparer.OrdinalIgnoreCase);
        var containers = new List<Guid>();
        foreach (HidCollection collection in present.Where(Centurion.IsCollection))
        {
            containers.Add(collection.ContainerId);
            wanted[collection.Path] = () => new CenturionSession(store, naming, collection);
        }
        claims.Set(Owner, containers);
        _sessions.Reconcile(wanted);
    }

    public void Wake(WakeReason reason) => _sessions.ForEach(session => session.Wake(reason));

    public void Dispose() => _sessions.Dispose();
}

/// <summary>
/// One Centurion dongle (or a headset on its cable). Finds the device address (report 0x50), the bridge to the headset
/// and the battery feature, reads the battery every few minutes and follows what the headset sends in between.
/// Only getters are sent. Nothing is shown until the headset has answered once.
/// </summary>
internal sealed class CenturionSession(DeviceStore store, DeviceNaming naming, HidCollection collection) : DeviceSession
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DongleTimeout = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan UsualAddressWait = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ScanStepWait = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan ScanAgainAfter = TimeSpan.FromMinutes(30);

    /// <summary>A headset that just came on may need a moment before it answers.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    private readonly string _id = $"logi:{collection.ProductId:X4}";
    private readonly WakeSignal _wake = new();
    private HidOutputPort? _output;
    private HidReportPump? _pump;
    private byte _report;
    private byte? _address;
    private byte? _bridge;
    private byte? _battery;
    private volatile bool _shown;
    private volatile bool _userAsked;
    private bool _wasAnswering;
    private bool _loggedReading;
    private DateTime _lastScan = DateTime.MinValue;
    private string? _lastNote;
    private bool _withoutBattery;

    protected override string Name => $"Logitech {collection.ProductId:X4} (Centurion)";

    /// <summary>The bridge index, or 0 when the device answers by itself (no bridge).</summary>
    private bool ViaBridge => _bridge is > 0;

    public void Wake(WakeReason reason)
    {
        if (reason == WakeReason.UserRefresh) _userAsked = true;
        _wake.Set();
    }

    public override void Dispose()
    {
        base.Dispose();
        if (_shown) store.Disconnect(_id);
    }

    protected override async Task RunAsync(CancellationToken cancellation)
    {
        IReadOnlyList<byte> declared = HidReportIds.Output(collection);
        _report = Centurion.ChooseReport(declared, collection.ProductId);
        HidInputPort? input = HidInputPort.Open(collection);
        _output = HidOutputPort.Open(collection);
        if (input == null || _output == null)
        {
            Log.Warn($"{Name}: cannot open the collection.");
            input?.Dispose();
            _output?.Dispose();
            return;
        }
        Log.Info($"{Name}: output reports [{string.Join(" ", declared.Select(id => id.ToString("X2")))}], talking with report {_report:X2}.");

        using (_output)
        using (_pump = new HidReportPump(input))
        {
            while (!cancellation.IsCancellationRequested && !_pump.IsDead)
            {
                if (FullScreen.IsActive())
                {
                    // During a game nothing is sent; battery events the headset sends by itself still count.
                    await ListenAsync(FullScreen.RecheckAfter, cancellation).ConfigureAwait(false);
                    continue;
                }

                if (_battery == null) await DiscoverAsync(cancellation).ConfigureAwait(false);
                if (_battery != null) await ReadBatteryAsync(cancellation).ConfigureAwait(false);

                if (await ListenAsync(PollInterval, cancellation).ConfigureAwait(false))
                    await Task.Delay(SettleDelay, cancellation).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Handles what arrives until <paramref name="duration"/> passes, <see cref="Wake"/> is called or the headset comes on.</summary>
    /// <returns>True when a frame asks for a read now (the headset came on, or the device address became known).</returns>
    private async Task<bool> ListenAsync(TimeSpan duration, CancellationToken cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task wake = _wake.WaitAsync(duration, stop.Token).ContinueWith(_ => { }, TaskScheduler.Default);
        try
        {
            while (!wake.IsCompleted)
            {
                Task<byte[]?> next = _pump!.NextAsync(TimeSpan.FromMinutes(1), stop.Token);
                if (await Task.WhenAny(next, wake).ConfigureAwait(false) != next) return false;
                if (await next.ConfigureAwait(false) is byte[] report && Handle(report)) return true;
            }
            return false;
        }
        finally
        {
            stop.Cancel();
        }
    }

    /// <summary>Takes in a frame that is not the answer being waited for.</summary>
    /// <returns>True when it asks for a read now.</returns>
    private bool Handle(byte[] report)
    {
        if (Centurion.Parse(report) is not CenturionFrame frame || frame.ReportId != _report || frame.Flags != 0) return false;

        bool readNow = false;
        if (frame.Address is byte address && _address == null)
        {
            _address = address;
            Log.Info($"{Name}: the device address is {address:X2} (from a frame it sent).");
            readNow = true;
        }

        if (ViaBridge)
        {
            switch (Centurion.ParseBridgeEvent(frame.Payload, _bridge!.Value))
            {
                case CenturionBridgeEvent.Connection { Linked: false }:
                    Log.Info($"{Name}: the headset went off.");
                    if (_shown) store.Report(_id, new DeviceReport.Asleep());
                    break;

                case CenturionBridgeEvent.Connection { Linked: true }:
                    Log.Info($"{Name}: the headset came on.");
                    readNow = true;
                    break;

                case CenturionBridgeEvent.Message { Error: null } message when message.SubIndex == _battery && Centurion.IsNotification(message.Tag):
                    Apply(message.Data, "battery event");
                    break;
            }
        }
        else if (_battery is byte battery && frame.Payload.Length >= 3 && frame.Payload[0] == battery && Centurion.IsNotification(frame.Payload[1]))
        {
            Apply(frame.Payload.Skip(2).ToArray(), "battery event");
        }
        return readNow;
    }

    /// <summary>Finds the address, the bridge and the battery feature; each step is kept once it succeeded.</summary>
    private async Task DiscoverAsync(CancellationToken cancellation)
    {
        if (_withoutBattery) return;
        if (_report == Centurion.AddressedReport && _address == null && !await FindAddressAsync(cancellation).ConfigureAwait(false)) return;

        if (_bridge == null)
        {
            // Root.GetFeature(0x0003): the dongle's bridge to the headset. Without one (a headset on its cable) the device answers by itself.
            if (await RequestAsync(0, 0, cancellation, 0x00, 0x03).ConfigureAwait(false) is not HidppReply bridge)
            {
                Note($"{Name}: no answer from the dongle (the headset may be off).");
                return;
            }
            _bridge = !bridge.IsError && bridge.Parameters.Length > 0 ? bridge.Parameters[0] : (byte)0;
            Log.Info(ViaBridge ? $"{Name}: bridge at index {_bridge}." : $"{Name}: no bridge ({Describe(bridge)}), the device answers by itself.");
        }

        _battery = await FindFeatureAsync(Centurion.FeatureBatterySoc, cancellation).ConfigureAwait(false);
        if (_battery != null) Log.Info($"{Name}: battery feature 0104 at index {_battery}{(ViaBridge ? " on the headset" : "")}.");
    }

    /// <summary>
    /// The address byte of report 0x50. A frame the device sends by itself tells it (see <see cref="Handle"/>); otherwise
    /// Root.GetProtocolVersion is sent to the usual address, then (at most every half hour, or when the user asks for a
    /// refresh) to all 256: the dongle answers only at its own.
    /// </summary>
    private async Task<bool> FindAddressAsync(CancellationToken cancellation)
    {
        if (await ProbeAddressAsync(Centurion.UsualAddress, UsualAddressWait, cancellation).ConfigureAwait(false)) return true;
        if (!_userAsked && DateTime.UtcNow - _lastScan < ScanAgainAfter) return false;

        _lastScan = DateTime.UtcNow;
        _userAsked = false;
        Note($"{Name}: no answer at address {Centurion.UsualAddress:X2}; trying the other addresses.");
        int failures = 0;
        for (int address = 0; address < 256; address++)
        {
            if (address == Centurion.UsualAddress) continue;
            if (!Send(ProbeFrame((byte)address)))
            {
                if (++failures > 3) return false;
                continue;
            }
            if (await CollectAsync(ScanStepWait, cancellation).ConfigureAwait(false)) return true;
        }
        // A late answer still counts.
        if (await CollectAsync(TimeSpan.FromMilliseconds(300), cancellation).ConfigureAwait(false)) return true;
        Note($"{Name}: no address answered (the headset may be off).");
        return false;
    }

    private async Task<bool> ProbeAddressAsync(byte address, TimeSpan wait, CancellationToken cancellation) =>
        Send(ProbeFrame(address)) && await CollectAsync(wait, cancellation).ConfigureAwait(false);

    /// <summary>Root.GetProtocolVersion to one address.</summary>
    private byte[] ProbeFrame(byte address) => Centurion.Frame(_report, address, Centurion.Request(0, 1, 0, 0, 0));

    /// <summary>Takes in frames for a while; true as soon as the device address is known.</summary>
    private async Task<bool> CollectAsync(TimeSpan wait, CancellationToken cancellation)
    {
        DateTime until = DateTime.UtcNow + wait;
        while (_address == null)
        {
            TimeSpan left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) break;
            if (await _pump!.NextAsync(left, cancellation).ConfigureAwait(false) is not byte[] report) break;
            Handle(report);
        }
        return _address != null;
    }

    /// <summary>
    /// The index of <paramref name="feature"/> on the headset (through the bridge) or on the device itself: Root.GetFeature,
    /// and if that gives nothing, the feature set listed one by one. Null when it was not found or nothing answered.
    /// </summary>
    private async Task<byte?> FindFeatureAsync(ushort feature, CancellationToken cancellation)
    {
        HidppReply? root = await AskAsync(0, 0, cancellation, (byte)(feature >> 8), (byte)feature).ConfigureAwait(false);
        if (root is not HidppReply r)
        {
            Note($"{Name}: no answer from the headset (it may be off).");
            return null;
        }
        if (!r.IsError && r.Parameters.Length > 0 && r.Parameters[0] != 0) return r.Parameters[0];
        Log.Info($"{Name}: Root.GetFeature({feature:X4}) gave {Describe(r)}; listing the features.");

        // FeatureSet: function 0 is the count, function 1 the feature at an index (remaining count, id high, id low, type, version).
        if (await AskAsync(0, 0, cancellation, (byte)(Centurion.FeatureSet >> 8), (byte)Centurion.FeatureSet).ConfigureAwait(false)
            is not HidppReply { IsError: false } set || set.Parameters.Length == 0 || set.Parameters[0] == 0) return null;
        byte setIndex = set.Parameters[0];
        if (await AskAsync(setIndex, 0, cancellation).ConfigureAwait(false) is not HidppReply { IsError: false } count || count.Parameters.Length == 0) return null;

        var listed = new List<string>();
        byte? found = null;
        int total = count.Parameters[0], answered = 0;
        for (int index = 0; index < total; index++)
        {
            // Silence means the headset went off: stop rather than wait out every index.
            if (await AskAsync(setIndex, 1, cancellation, (byte)index).ConfigureAwait(false) is not HidppReply entry) break;
            answered++;
            if (entry.IsError || entry.Parameters.Length < 3) continue;
            ushort id = (ushort)((entry.Parameters[1] << 8) | entry.Parameters[2]);
            listed.Add($"{index}={id:X4}");
            if (id == feature && found == null) found = (byte)index;
        }
        Log.Info($"{Name}: features {string.Join(" ", listed)}.");
        if (found == null && answered == total)
        {
            // The whole list came and the feature is not on it: asking again every few minutes would change nothing.
            _withoutBattery = true;
            Log.Info($"{Name}: no battery feature {feature:X4}; not asking again until the device is plugged in anew.");
        }
        return found;
    }

    private async Task ReadBatteryAsync(CancellationToken cancellation)
    {
        HidppReply? reply = await AskAsync(_battery!.Value, 0, cancellation).ConfigureAwait(false);
        if (reply is HidppReply { IsError: false } r)
        {
            _wasAnswering = true;
            _lastNote = null;
            Apply(r.Parameters, "battery");
            return;
        }

        // Off or out of range: the bridge sends no answer for the headset, or an error.
        if (_wasAnswering) Log.Info($"{Name}: the headset does not answer ({(reply is HidppReply e ? Describe(e) : "no reply")}).");
        _wasAnswering = false;
        if (_shown) store.Report(_id, new DeviceReport.Asleep());
    }

    private void Apply(byte[] data, string what)
    {
        if (!_loggedReading)
        {
            Log.Info($"{Name}: {what} data {Hex(data)}.");
            _loggedReading = true;
        }

        if (Centurion.ParseBattery(data) is not CenturionBattery battery)
        {
            Log.Info($"{Name}: unusable {what} data {Hex(data)}.");
            if (_shown) store.Report(_id, new DeviceReport.Asleep());
            return;
        }

        if (!_shown)
        {
            string name = Centurion.NameFromProduct(collection.Product) ?? Hidpp.KindName(DeviceKind.Headset);
            var descriptor = new DeviceDescriptor(_id, naming.Resolve(_id, name), DeviceKind.Headset);
            store.Connect(descriptor);
            _shown = true;
            Log.Info($"{Name}: found {descriptor.Name} [{_id}].");
        }
        store.Report(_id, new DeviceReport.Charge(battery.Percent, battery.IsCharging));
    }

    /// <summary>A getter to the headset: through the bridge when there is one, otherwise to the device itself.</summary>
    private Task<HidppReply?> AskAsync(byte featureIndex, byte function, CancellationToken cancellation, params byte[] parameters) =>
        ViaBridge ? BridgeRequestAsync(featureIndex, function, cancellation, parameters) : RequestAsync(featureIndex, function, cancellation, parameters);

    /// <summary>Sends a getter to the addressed device and waits for its answer; other frames meanwhile are handled after.</summary>
    private Task<HidppReply?> RequestAsync(byte featureIndex, byte function, CancellationToken cancellation, params byte[] parameters) =>
        ExchangeAsync(Centurion.Request(featureIndex, function, parameters), DongleTimeout, cancellation,
            frame => Centurion.MatchReply(frame.Payload, featureIndex, function));

    /// <summary>Sends a getter to the headset behind the bridge. The bridge acknowledges first and answers with a MessageEvent.</summary>
    private Task<HidppReply?> BridgeRequestAsync(byte subIndex, byte subFunction, CancellationToken cancellation, params byte[] parameters)
    {
        byte bridge = _bridge!.Value;
        return ExchangeAsync(Centurion.BridgeRequest(bridge, subIndex, subFunction, parameters), BridgeTimeout, cancellation, frame =>
        {
            // The bridge refused the request itself (an error to SendFragment).
            if (Centurion.MatchReply(frame.Payload, bridge, 1) is HidppReply { IsError: true } refused) return refused;
            return Centurion.ParseBridgeEvent(frame.Payload, bridge) is CenturionBridgeEvent.Message message
                ? Centurion.MatchBridgeReply(message, subIndex, subFunction)
                : null;
        });
    }

    private async Task<HidppReply?> ExchangeAsync(byte[] payload, TimeSpan timeout, CancellationToken cancellation, Func<CenturionFrame, HidppReply?> match)
    {
        if (!Send(Centurion.Frame(_report, _address ?? 0, payload))) return null;

        DateTime until = DateTime.UtcNow + timeout;
        var later = new List<byte[]>();
        try
        {
            while (true)
            {
                TimeSpan left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return null;
                if (await _pump!.NextAsync(left, cancellation).ConfigureAwait(false) is not byte[] report) return null;
                if (Centurion.Parse(report) is CenturionFrame { Flags: 0 } frame && frame.ReportId == _report && match(frame) is HidppReply reply)
                    return reply;
                later.Add(report);
            }
        }
        finally
        {
            foreach (byte[] report in later) Handle(report);
        }
    }

    private bool Send(byte[] frame)
    {
        try
        {
            _output!.Write(frame);
            return true;
        }
        catch (Win32Exception exception)
        {
            Log.Warn($"{Name}: sending failed ({exception.Message}).");
            return false;
        }
    }

    /// <summary>Logs a line unless it repeats the previous one, so a headset that stays off does not fill the log.</summary>
    private void Note(string message)
    {
        if (message == _lastNote) return;
        _lastNote = message;
        Log.Info(message);
    }

    private static string Describe(HidppReply reply) => reply.IsError ? $"error {reply.Error:X2}" : $"[{Hex(reply.Parameters)}]";

    private static string Hex(byte[] data) => BitConverter.ToString(data.Take(16).ToArray()).Replace('-', ' ');
}
