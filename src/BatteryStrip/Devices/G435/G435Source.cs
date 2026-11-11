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

    public void Wake(WakeReason reason) => _sessions.ForEach(session => session.Wake(reason));

    public void Dispose() => _sessions.Dispose();
}

/// <summary>
/// Follows one G435 receiver. Each battery read briefly pauses the headset's audio, so scheduled reads
/// are rare and wait for a quiet moment (see <see cref="QuietGate"/>). A read the user asks for happens at once.
/// </summary>
internal sealed class G435Session(DeviceStore store, DeviceDescriptor descriptor, HidCollection service) : DeviceSession
{
    /// <summary>Time between scheduled reads while the headset is on.</summary>
    private static readonly TimeSpan ReadInterval = TimeSpan.FromMinutes(30);

    /// <summary>Time between checks while the headset is off, to notice it being switched on.</summary>
    private static readonly TimeSpan OffCheckInterval = TimeSpan.FromMinutes(2);

    /// <summary>A failed read is retried at once (the quiet moment usually lasts), then after a minute.</summary>
    private static readonly TimeSpan QuickRetry = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan SlowRetry = TimeSpan.FromMinutes(1);
    private const int MaxRetries = 3;

    /// <summary>After waking from sleep the receiver needs a moment before it answers reliably.</summary>
    private static readonly TimeSpan SettleAfterResume = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan AudioPoll = TimeSpan.FromSeconds(1);

    /// <summary>Audio playing into a headset believed to be off for this long means it is probably on.</summary>
    private static readonly TimeSpan BusyMeansOn = TimeSpan.FromSeconds(20);

    private const string WaitingNote = "Ждёт тишины в наушниках";
    private const string NoAnswerNote = "Нет ответа";

    private readonly EstimateSmoother _smoother = new();
    private readonly SemaphoreSlim _wake = new(0);
    private volatile bool _readNow;
    private DateTime _notBefore = DateTime.MinValue;
    private bool _shown;
    private bool _believedOff;

    protected override string Name => "G435";

    /// <summary>Cuts the current wait short. A user refresh reads at once, even while audio plays.</summary>
    public void Wake(WakeReason reason)
    {
        if (reason == WakeReason.UserRefresh) _readNow = true;
        else _notBefore = DateTime.UtcNow + SettleAfterResume;
        try { _wake.Release(); }
        catch (ObjectDisposedException) { }
    }

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
        using var audio = new AudioActivity(service.ContainerId);
        var gate = new QuietGate();
        int failures = 0;

        while (!cancellation.IsCancellationRequested)
        {
            bool forced = await WaitForTurnAsync(audio, gate, cancellation).ConfigureAwait(false);
            G435Reading reading = await ReadAsync(forced).ConfigureAwait(false);
            gate.Reset();

            TimeSpan pause;
            switch (reading)
            {
                case G435Reading.Voltage voltage:
                    failures = 0;
                    _believedOff = false;
                    int percent = _smoother.Apply(G435Protocol.MillivoltsToPercent(voltage.Millivolts));
                    Show();
                    store.Report(descriptor.Id, new DeviceReport.Charge(percent, IsCharging: false, IsEstimate: true));
                    Log.Info($"G435: {voltage.Millivolts} mV, about {percent} %{(forced ? " (asked by the user)" : "")}.");
                    pause = ReadInterval;
                    break;

                case G435Reading.HeadsetOff:
                    failures = 0;
                    if (!_believedOff) Log.Info("G435: the headset is off or out of range.");
                    _believedOff = true;
                    Hide();
                    _smoother.Reset();
                    pause = OffCheckInterval;
                    break;

                default:
                    failures++;
                    pause = failures == 1 ? QuickRetry : failures <= MaxRetries ? SlowRetry : ReadInterval;
                    if (_shown) store.Report(descriptor.Id, new DeviceReport.Stale(NoAnswerNote));
                    break;
            }

            await PauseAsync(pause, cancellation).ConfigureAwait(false);
        }
    }

    /// <summary>Waits for a pause, cut short by <see cref="Wake"/>.</summary>
    private async Task PauseAsync(TimeSpan pause, CancellationToken cancellation)
    {
        if (_readNow) return;
        await Task.WhenAny(Task.Delay(pause, cancellation), _wake.WaitAsync(cancellation)).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
    }

    /// <summary>Returns when a read may start: the headset is quiet, or the user asked. True for the latter.</summary>
    private async Task<bool> WaitForTurnAsync(AudioActivity audio, QuietGate gate, CancellationToken cancellation)
    {
        DateTime? busySince = null;
        bool markedWaiting = false;
        while (true)
        {
            if (_readNow)
            {
                _readNow = false;
                return true;
            }

            DateTime now = DateTime.UtcNow;
            AudioState? state = audio.Sample();
            if (now >= _notBefore && gate.Observe(state, now)) return false;

            // While it cannot be asked, a headset that is being played to is shown with its last known charge.
            bool busy = state is not AudioState s || s.HasOpenStreams;
            busySince = busy ? busySince ?? now : null;
            if (busySince is DateTime since && now - since >= BusyMeansOn && !markedWaiting)
            {
                markedWaiting = true;
                Show();
                store.Report(descriptor.Id, new DeviceReport.Stale(WaitingNote));
            }

            await Task.WhenAny(Task.Delay(AudioPoll, cancellation), _wake.WaitAsync(cancellation)).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
        }
    }

    private async Task<G435Reading> ReadAsync(bool forced)
    {
        using HidInputPort? input = HidInputPort.Open(service);
        using HidFeaturePort? feature = HidFeaturePort.Open(service);
        if (input == null || feature == null) return new G435Reading.Failed("cannot open the receiver");

        var bridge = new G435Bridge(feature, input);
        G435Reading reading = await bridge.ReadBatteryAsync().ConfigureAwait(false);
        if (reading is G435Reading.Failed failed)
            Log.Warn($"G435: read failed ({failed.Reason}){(forced ? " (asked by the user)" : "")}. Exchange:{Environment.NewLine}{bridge.Transcript}");
        return reading;
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
