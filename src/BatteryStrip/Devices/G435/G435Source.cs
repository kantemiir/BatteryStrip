using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.G435;

/// <summary>Logitech G435 headset on its original LIGHTSPEED receiver (046D:0ACB).</summary>
internal sealed class G435Source(DeviceStore store, DeviceNaming naming) : IDeviceSource
{
    private const ushort VendorId = 0x046D;
    private const ushort ProductId = 0x0ACB;
    private const ushort ServicePage = 0xFFC0;

    private readonly SessionSet<G435Session> _sessions = new();

    public void Refresh(IReadOnlyList<HidCollection> present)
    {
        var wanted = new Dictionary<string, Func<G435Session>>(StringComparer.OrdinalIgnoreCase);
        HidCollection? service = present.FirstOrDefault(c =>
            c.VendorId == VendorId && c.ProductId == ProductId && c.UsagePage == ServicePage
            && c.FeatureLength == G435Protocol.FeatureLength && c.InputLength > 0);

        if (service != null)
        {
            const string id = "g435:046D:0ACB";
            var descriptor = new DeviceDescriptor(id, naming.Resolve(id, "Logitech G435"), DeviceKind.Headset);
            wanted[service.Path] = () => new G435Session(store, descriptor, service);
        }
        _sessions.Reconcile(wanted);
    }

    public void Dispose() => _sessions.Dispose();
}

/// <summary>
/// Follows one G435 receiver. Each battery read briefly pauses the headset's audio, so reads are rare
/// and wait for a quiet moment (see <see cref="QuietGate"/>).
/// </summary>
internal sealed class G435Session(DeviceStore store, DeviceDescriptor descriptor, HidCollection service) : DeviceSession
{
    /// <summary>Time between reads while the headset is on.</summary>
    private static readonly TimeSpan ReadInterval = TimeSpan.FromMinutes(30);

    /// <summary>Time between checks while the headset is off, to notice it being switched on.</summary>
    private static readonly TimeSpan OffCheckInterval = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);
    private const int MaxQuickRetries = 3;

    private static readonly TimeSpan GatePoll = TimeSpan.FromSeconds(2);

    /// <summary>Audio playing into a headset believed to be off for this long means it is probably on.</summary>
    private static readonly TimeSpan BusyMeansOn = TimeSpan.FromSeconds(20);

    private readonly EstimateSmoother _smoother = new();
    private bool _shown;

    protected override string Name => "G435";

    public override void Dispose()
    {
        base.Dispose();
        store.Disconnect(descriptor.Id);
    }

    protected override async Task RunAsync(CancellationToken cancellation)
    {
        Log.Info($"G435 receiver found ({service.Product}).");

        // Until the first read, show the charge remembered from last time (dimmed), if there is one.
        Show();
        var gate = new QuietGate();
        int failures = 0;

        while (!cancellation.IsCancellationRequested)
        {
            await WaitForQuietAsync(gate, cancellation).ConfigureAwait(false);
            G435Reading reading = await ReadAsync().ConfigureAwait(false);
            gate.Reset();

            TimeSpan pause;
            switch (reading)
            {
                case G435Reading.Voltage voltage:
                    failures = 0;
                    int percent = _smoother.Apply(G435Protocol.MillivoltsToPercent(voltage.Millivolts));
                    Show();
                    store.Report(descriptor.Id, new DeviceReport.Charge(percent, IsCharging: false, IsEstimate: true));
                    Log.Info($"G435: {voltage.Millivolts} mV, about {percent} %.");
                    pause = ReadInterval;
                    break;

                case G435Reading.HeadsetOff:
                    failures = 0;
                    Hide();
                    _smoother.Reset();
                    pause = OffCheckInterval;
                    break;

                default:
                    failures++;
                    Log.Warn($"G435: read failed ({((G435Reading.Failed)reading).Reason}), attempt {failures}.");
                    pause = failures <= MaxQuickRetries ? RetryInterval : ReadInterval;
                    break;
            }

            await Task.Delay(pause, cancellation).ConfigureAwait(false);
        }
    }

    /// <summary>Returns when the headset is unused and a read cannot be heard.</summary>
    private async Task WaitForQuietAsync(QuietGate gate, CancellationToken cancellation)
    {
        DateTime? busySince = null;
        while (true)
        {
            DateTime now = DateTime.UtcNow;
            AudioState? audio = AudioActivity.Query(service.ContainerId);
            if (gate.Observe(audio, now)) return;

            // While it cannot be asked, a headset that is being played to is shown with its last known charge.
            bool busy = audio is not AudioState state || state.HasOpenStreams;
            busySince = busy ? busySince ?? now : null;
            if (!_shown && busySince is DateTime since && now - since >= BusyMeansOn)
            {
                Show();
                store.Report(descriptor.Id, new DeviceReport.Asleep());
            }

            await Task.Delay(GatePoll, cancellation).ConfigureAwait(false);
        }
    }

    private async Task<G435Reading> ReadAsync()
    {
        using HidInputPort? input = HidInputPort.Open(service);
        using HidFeaturePort? feature = HidFeaturePort.Open(service);
        if (input == null || feature == null) return new G435Reading.Failed("cannot open the receiver");
        return await new G435Bridge(feature, input).ReadBatteryAsync().ConfigureAwait(false);
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
