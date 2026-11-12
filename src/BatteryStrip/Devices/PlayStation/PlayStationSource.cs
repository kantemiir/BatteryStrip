using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.PlayStation;

/// <summary>Sony DualSense, DualSense Edge and DualShock 4, by cable, Bluetooth or the DualShock 4 USB adapter.</summary>
internal sealed class PlayStationSource(DeviceStore store, DeviceNaming naming, ContainerClaims claims) : IDeviceSource
{
    public const string Owner = "playstation";

    private readonly SessionSet<PadSession> _sessions = new();

    public void Refresh(IReadOnlyList<HidCollection> present)
    {
        var pads = present.Where(c => c.VendorId == PlayStationProtocol.SonyVendorId
            && (PlayStationProtocol.IsDualSense(c.ProductId) || PlayStationProtocol.IsDualShock4(c.ProductId))
            && c.UsagePage == 0x01 && c.Usage == 0x05 && c.InputLength > 0).ToList();

        var wanted = new Dictionary<string, Func<PadSession>>(StringComparer.OrdinalIgnoreCase);
        foreach (HidCollection pad in pads)
        {
            string id = $"ps:{(pad.ContainerId != Guid.Empty ? pad.ContainerId.ToString("N") : pad.Path)}";
            string name = PlayStationProtocol.IsDualSense(pad.ProductId) ? (pad.ProductId == 0x0DF2 ? "DualSense Edge" : "DualSense") : "DualShock 4";
            var descriptor = new DeviceDescriptor(id, naming.Resolve(id, name), DeviceKind.Gamepad);
            wanted[pad.Path] = () => new PadSession(store, descriptor, pad);
        }
        claims.Set(Owner, pads.Select(p => p.ContainerId));
        _sessions.Reconcile(wanted);
    }

    public void Wake(WakeReason reason) => _sessions.ForEach(session => session.Wake());

    public void Dispose() => _sessions.Dispose();
}

/// <summary>
/// One controller. Its input reports arrive hundreds of times a second, so it is not listened to all the time:
/// once a minute the session opens the controller, takes a few reports and closes it again.
/// </summary>
internal sealed class PadSession(DeviceStore store, DeviceDescriptor descriptor, HidCollection pad) : DeviceSession
{
    private static readonly TimeSpan ReadInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SampleTime = TimeSpan.FromMilliseconds(300);
    private const int SampleReports = 8;

    private readonly WakeSignal _wake = new();
    private readonly bool _bluetooth = PlayStationProtocol.IsBluetooth(pad.InputLength);
    private bool _loggedShortMode;

    protected override string Name => descriptor.Name;

    public void Wake() => _wake.Set();

    public override void Dispose()
    {
        base.Dispose();
        store.Disconnect(descriptor.Id);
    }

    protected override async Task RunAsync(CancellationToken cancellation)
    {
        Log.Info($"{descriptor.Name} found ({descriptor.Id}, {(_bluetooth ? "Bluetooth" : "USB")}).");
        store.Connect(descriptor);
        while (!cancellation.IsCancellationRequested)
        {
            (PadBattery? battery, bool heard) = await SampleAsync(cancellation).ConfigureAwait(false);
            if (battery is PadBattery b) store.Report(descriptor.Id, new DeviceReport.Charge(b.Percent, b.IsCharging));
            else if (heard && _bluetooth) ReportShortMode();
            else store.Report(descriptor.Id, new DeviceReport.Asleep());

            await _wake.WaitAsync(ReadInterval, cancellation).ConfigureAwait(false);
        }
    }

    /// <summary>The controller talks, but in the Bluetooth short-report mode, which has no battery data.</summary>
    private void ReportShortMode()
    {
        store.Report(descriptor.Id, new DeviceReport.Unsupported());
        if (_loggedShortMode) return;
        _loggedShortMode = true;
        Log.Info($"{descriptor.Name}: Bluetooth short reports carry no battery; it shows once a game or Steam turns on full reports.");
    }

    /// <returns>The battery, if a report had it, and whether the controller sent anything at all.</returns>
    private async Task<(PadBattery? Battery, bool Heard)> SampleAsync(CancellationToken cancellation)
    {
        using HidInputPort? input = HidInputPort.Open(pad);
        if (input == null) return (null, false);

        bool heard = false;
        DateTime until = DateTime.UtcNow + SampleTime;
        for (int i = 0; i < SampleReports && DateTime.UtcNow < until; i++)
        {
            byte[]? report = await input.ReadAsync(SampleTime, cancellation).ConfigureAwait(false);
            if (report == null) break;
            heard = true;
            if (PlayStationProtocol.Parse(pad.ProductId, report, _bluetooth) is PadBattery battery) return (battery, true);
        }
        return (null, heard);
    }
}
