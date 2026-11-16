using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Bluetooth;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.Jbl;

/// <summary>
/// JBL speakers connected to this computer over Bluetooth. Windows does not know their charge (they connect for
/// music only), so it is asked from the speaker itself over Bluetooth Low Energy, the way the JBL Portable app does.
/// Only speakers paired with and connected to this computer are looked at; the neighbours' JBLs are not.
/// </summary>
internal sealed class JblSource : IDeviceSource
{
    public const string Owner = "jbl";

    /// <summary>Connections come and go without a device-interface event often enough to also look once a minute.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private static readonly DevicePropertyKey[] Keys = [DevicePropertyKey.IsConnected];

    private static readonly DevicePropertyKey[] DetailKeys =
    [
        DevicePropertyKey.FriendlyName,
        DevicePropertyKey.ContainerId,
        DevicePropertyKey.ContainerCategory,
    ];

    private readonly DeviceStore _store;
    private readonly DeviceNaming _naming;
    private readonly ContainerClaims _claims;
    private readonly HarmanAdvertisements _advertisements = new();
    private readonly SessionSet<JblSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Guid> _speakers = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Timer _poll;
    private bool _disposed;

    public JblSource(DeviceStore store, DeviceNaming naming, ContainerClaims claims)
    {
        _store = store;
        _naming = naming;
        _claims = claims;
        _poll = new Timer(_ => Scan(), null, PollInterval, PollInterval);
    }

    public void Refresh(IReadOnlyList<HidCollection> present) => Scan();

    public void Wake(WakeReason reason)
    {
        lock (_gate)
        {
            if (!_disposed) _sessions.ForEach(session => session.Wake(reason));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _poll.Dispose();
            _sessions.Dispose();
            _advertisements.Dispose();
        }
    }

    private void Scan()
    {
        try
        {
            lock (_gate)
            {
                if (!_disposed) ScanLocked();
            }
        }
        catch (Exception exception)
        {
            Log.Error("JBL scan failed", exception);
        }
    }

    private void ScanLocked()
    {
        var wanted = new Dictionary<string, Func<JblSession>>(StringComparer.Ordinal);
        foreach (PnpDevice node in Pnp.List("BTHENUM", Keys, node => node.GetBool(DevicePropertyKey.IsConnected) == true, DetailKeys))
        {
            // Only the device node itself ("BTHENUM\DEV_<address>\…"); its service nodes give no address.
            if (BluetoothSource.BluetoothAddress(node.InstanceId) is not string address) continue;

            string id = "jbl:" + address;
            Guid container = node.GetGuid(DevicePropertyKey.ContainerId) ?? Guid.Empty;
            string name = _naming.Resolve(id, node.GetString(DevicePropertyKey.FriendlyName) ?? "JBL");
            DeviceKind kind = BluetoothSource.KindOf(node.GetStrings(DevicePropertyKey.ContainerCategory));
            var descriptor = new DeviceDescriptor(id, name, kind is DeviceKind.Other ? DeviceKind.Speaker : kind);
            ulong classic = Convert.ToUInt64(address, 16);
            wanted[address] = () => new JblSession(_store, descriptor, classic, _advertisements, isSpeaker => Identified(address, container, isSpeaker));
        }

        foreach (string gone in _speakers.Keys.Where(address => !wanted.ContainsKey(address)).ToArray()) _speakers.TryRemove(gone, out _);
        _sessions.Reconcile(wanted);
        _claims.Set(Owner, _speakers.Values);
    }

    /// <summary>A session found out whether its device is a JBL speaker; a speaker's container is claimed from the general Bluetooth source.</summary>
    private void Identified(string address, Guid container, bool isSpeaker)
    {
        if (isSpeaker) _speakers[address] = container;
        else _speakers.TryRemove(address, out _);
        _claims.Set(Owner, _speakers.Values);
    }
}

/// <summary>
/// Follows one connected Bluetooth device. First listens whether it is a JBL speaker (its advertisement carries a
/// checksum of its address); if so, asks its battery every ten minutes and when the user asks. A device that turns
/// out not to be one is left alone until it reconnects.
/// </summary>
internal sealed class JblSession(DeviceStore store, DeviceDescriptor descriptor, ulong classicAddress, HarmanAdvertisements advertisements,
    Action<bool> identified) : DeviceSession
{
    private static readonly TimeSpan ReadInterval = TimeSpan.FromMinutes(10);

    /// <summary>How long to listen for the speaker's advertisement. It advertises several times a second while on.</summary>
    private static readonly TimeSpan ListenTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A device not heard this many times, a minute apart, is not a JBL speaker (or not one that advertises).</summary>
    private const int ListensBeforeGivingUp = 3;

    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);
    private const int QuickRetries = 3;

    private readonly WakeSignal _wake = new();
    private bool _shown;
    private (int Percent, bool IsCharging)? _last;

    protected override string Name => "JBL " + descriptor.Name;

    public void Wake(WakeReason reason) => _wake.Set();

    public override void Dispose()
    {
        base.Dispose();
        store.Disconnect(descriptor.Id);
    }

    protected override async Task RunAsync(CancellationToken cancellation)
    {
        ushort addressCrc = JblProtocol.AddressCrc(classicAddress);
        bool isSpeaker = false;
        int misses = 0, failures = 0;

        while (!cancellation.IsCancellationRequested)
        {
            // A full-screen game gets the computer to itself: nothing is sent to devices.
            if (FullScreen.IsActive())
            {
                await _wake.WaitAsync(FullScreen.RecheckAfter, cancellation).ConfigureAwait(false);
                continue;
            }

            JblAdvertisement? heard = await advertisements.FindAsync(addressCrc, ListenTimeout, cancellation).ConfigureAwait(false);
            if (heard == null)
            {
                if (!isSpeaker && ++misses >= ListensBeforeGivingUp)
                {
                    await Task.Delay(Timeout.Infinite, cancellation).ConfigureAwait(false);
                }
                if (isSpeaker) MarkNoAnswer(++failures, "not heard on the air");
                await _wake.WaitAsync(Retry, cancellation).ConfigureAwait(false);
                continue;
            }

            if (!isSpeaker)
            {
                isSpeaker = true;
                identified(true);
                Log.Info($"JBL speaker found: {descriptor.Name} [{descriptor.Id}], model {heard.ProductId:X4}.");
                Show();
            }

            TimeSpan pause = ReadInterval;
            switch (await JblLink.ReadBatteryAsync(heard, cancellation).ConfigureAwait(false))
            {
                case JblReading.Charge charge:
                    failures = 0;
                    store.Report(descriptor.Id, new DeviceReport.Charge(charge.Percent, charge.IsCharging));
                    if (_last != (charge.Percent, charge.IsCharging))
                    {
                        _last = (charge.Percent, charge.IsCharging);
                        Log.Info($"JBL {descriptor.Name}: {charge.Percent} %{(charge.IsCharging ? ", charging" : "")}.");
                    }
                    break;

                case JblReading.Unsupported unsupported:
                    await GiveUpAsync($"model {heard.ProductId:X4} cannot be read ({unsupported.Reason})", cancellation).ConfigureAwait(false);
                    break;

                case JblReading.Failed failed when _last == null && failures >= QuickRetries:
                    // Never answered once: most likely a model on another protocol. Not asked again until it reconnects.
                    await GiveUpAsync($"model {heard.ProductId:X4} never answered ({failed.Reason})", cancellation).ConfigureAwait(false);
                    break;

                case JblReading.Failed failed:
                    MarkNoAnswer(++failures, failed.Reason);
                    if (failures <= QuickRetries) pause = Retry;
                    break;
            }

            await _wake.WaitAsync(pause, cancellation).ConfigureAwait(false);
        }
    }

    private async Task GiveUpAsync(string reason, CancellationToken cancellation)
    {
        Log.Info($"JBL {descriptor.Name}: {reason}; not asked again until it reconnects.");
        identified(false);
        Hide();
        await Task.Delay(Timeout.Infinite, cancellation).ConfigureAwait(false);
    }

    private void MarkNoAnswer(int failures, string reason)
    {
        if (failures == 1 || failures == QuickRetries + 1) Log.Warn($"JBL {descriptor.Name}: read failed ({reason}).");
        if (_shown) store.Report(descriptor.Id, new DeviceReport.Stale(Text.Current.NoAnswer));
    }

    private void Show()
    {
        if (_shown) return;
        _shown = true;
        store.Connect(descriptor);
    }

    private void Hide()
    {
        if (!_shown) return;
        _shown = false;
        store.Disconnect(descriptor.Id);
    }
}
