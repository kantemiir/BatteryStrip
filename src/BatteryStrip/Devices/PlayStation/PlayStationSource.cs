using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private bool _switchedToFullReports;

    protected override string Name => descriptor.Name;

    public void Wake() => _wake.Set();

    public override void Dispose()
    {
        base.Dispose();
        store.Disconnect(descriptor.Id);
    }

    protected override async Task RunAsync(CancellationToken cancellation)
    {
        Log.Info($"{descriptor.Name} found ({descriptor.Id}).");
        store.Connect(descriptor);
        while (!cancellation.IsCancellationRequested)
        {
            PadBattery? battery = await SampleAsync(cancellation).ConfigureAwait(false);
            if (battery is PadBattery b) store.Report(descriptor.Id, new DeviceReport.Charge(b.Percent, b.IsCharging));
            else store.Report(descriptor.Id, new DeviceReport.Asleep());

            await _wake.WaitAsync(ReadInterval, cancellation).ConfigureAwait(false);
        }
    }

    private async Task<PadBattery?> SampleAsync(CancellationToken cancellation)
    {
        using HidInputPort? input = HidInputPort.Open(pad);
        if (input == null) return null;

        DateTime until = DateTime.UtcNow + SampleTime;
        for (int i = 0; i < SampleReports && DateTime.UtcNow < until; i++)
        {
            byte[]? report = await input.ReadAsync(SampleTime, cancellation).ConfigureAwait(false);
            if (report == null) break;
            if (PlayStationProtocol.Parse(pad.ProductId, report) is PadBattery battery) return battery;
        }

        // Over Bluetooth (the only link with reports longer than 64 bytes) the controller may still be in its
        // short-report mode, in which it reports nothing or only buttons. Ask once for the full reports.
        if (pad.InputLength > 64 && !_switchedToFullReports) SwitchToFullReports();
        return null;
    }

    /// <summary>A read of the calibration feature report; it changes nothing in the controller's settings.</summary>
    private void SwitchToFullReports()
    {
        _switchedToFullReports = true;
        try
        {
            using HidFeaturePort? feature = HidFeaturePort.Open(pad);
            if (feature == null || pad.FeatureLength <= 0) return;
            feature.Get(PlayStationProtocol.FullReportFeatureId(pad.ProductId), pad.FeatureLength);
            Log.Info($"{descriptor.Name}: asked for full input reports over Bluetooth.");
        }
        catch (Win32Exception exception)
        {
            Log.Warn($"{descriptor.Name}: could not switch to full reports ({exception.Message}).");
        }
    }
}
