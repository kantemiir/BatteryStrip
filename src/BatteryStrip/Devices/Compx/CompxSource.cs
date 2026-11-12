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

namespace BatteryStrip.Devices.Compx;

/// <summary>
/// COMPX wireless mice (USB vendor 25A7): Lamzu, Ajazz, Attack Shark and others built on the same chipset.
/// The same mouse can be reachable through its receiver and, while it charges, through its cable;
/// both links feed one device on the strip.
/// </summary>
internal sealed class CompxSource(DeviceStore store, DeviceNaming naming, ContainerClaims claims) : IDeviceSource
{
    public const string Owner = "compx";

    private const ushort VendorId = 0x25A7;

    /// <summary>Cable product id of a mouse, mapped to the product id of its receiver.</summary>
    private static readonly Dictionary<ushort, ushort> ReceiverOfCable = new() { [0xFA7B] = 0xFA7C };

    private readonly SessionSet<CompxLink> _links = new();
    private readonly Dictionary<string, CompxMouse> _mice = new(StringComparer.Ordinal);

    public void Refresh(IReadOnlyList<HidCollection> present)
    {
        var wanted = new Dictionary<string, Func<CompxLink>>(StringComparer.OrdinalIgnoreCase);
        var presentMice = new HashSet<string>(StringComparer.Ordinal);

        var receivers = present
            .Where(c => c.VendorId == VendorId && c.IsVendorDefined)
            .GroupBy(c => (c.ProductId, c.ContainerId));

        foreach (var receiver in receivers)
        {
            HidCollection? requests = receiver.FirstOrDefault(c => c.FeatureLength == CompxProtocol.FrameLength);
            HidCollection? replies = receiver.FirstOrDefault(c => c.InputLength == CompxProtocol.FrameLength);
            if (requests == null || replies == null) continue;

            ushort productId = ReceiverOfCable.TryGetValue(receiver.Key.ProductId, out ushort paired) ? paired : receiver.Key.ProductId;
            string id = $"compx:{VendorId:X4}:{productId:X4}";
            presentMice.Add(id);

            if (!_mice.TryGetValue(id, out CompxMouse? mouse))
            {
                var descriptor = new DeviceDescriptor(id, naming.Resolve(id, DefaultName()), DeviceKind.Mouse);
                mouse = new CompxMouse(store, descriptor);
                _mice.Add(id, mouse);
                store.Connect(descriptor);
                Log.Info($"COMPX mouse found: {id} ({replies.Product}).");
            }

            CompxMouse target = mouse;
            bool cable = ReceiverOfCable.ContainsKey(receiver.Key.ProductId);
            wanted[replies.Path] = () => new CompxLink(target, requests, replies, cable);
        }

        claims.Set(Owner, receivers.Where(r => wanted.Keys.Any(path => r.Any(c => c.Path == path))).Select(r => r.Key.ContainerId));
        _links.Reconcile(wanted);

        foreach (string id in _mice.Keys.Where(id => !presentMice.Contains(id)).ToArray())
        {
            _mice.Remove(id);
            store.Disconnect(id);
            Log.Info($"COMPX mouse removed: {id}.");
        }
    }

    public void Wake(WakeReason reason)
    {
        if (reason == WakeReason.UserRefresh) _links.ForEach(link => link.AskNow());
    }

    public void Dispose() => _links.Dispose();

    /// <summary>The receiver only calls itself "2.4G Wireless Receiver", so the brand is guessed from the vendor software.</summary>
    private static string DefaultName()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return Directory.Exists(Path.Combine(programFiles, "LAMZU Gaming Mouse")) ? Text.Current.LamzuMouse : Text.Current.Mouse;
    }
}

/// <summary>
/// One mouse as shown on the strip, fed by one or two links. While the mouse is on its cable, both its cable and
/// its receiver answer, and they disagree: the cable talks to the mouse itself, the receiver repeats what the mouse
/// last told it over the air (seen on a Lamzu: the cable said "100 %, charged" while the receiver kept saying
/// "95 %, charging", and right after plugging in "95 %" against "30 %"). Showing whichever answered last made the
/// number jump, so while the cable answers, only the cable counts.
/// </summary>
internal sealed class CompxMouse(DeviceStore store, DeviceDescriptor descriptor)
{
    /// <summary>A link that answered this recently outranks another link reporting "asleep".</summary>
    private static readonly TimeSpan LinkFreshness = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime At, bool Cable)> _lastAnswer = new(StringComparer.OrdinalIgnoreCase);
    private readonly FullChargeLatch _latch = new();
    private CompxBattery? _shown;

    public string Id => descriptor.Id;

    public void OnBattery(string link, bool cable, CompxBattery battery)
    {
        lock (_gate)
        {
            if (battery.IsAsleep)
            {
                OnSilenceLocked(link);
                return;
            }

            DateTime now = DateTime.UtcNow;
            _lastAnswer[link] = (now, cable);
            if (!cable && _lastAnswer.Values.Any(a => a.Cable && a.At > now - LinkFreshness)) return;

            battery = _latch.Apply(battery);
            if (_shown != battery)
            {
                _shown = battery;
                Log.Info($"{descriptor.Id}: {battery.Percent} %{(battery.IsCharging ? ", charging" : "")} ({(cable ? "cable" : "receiver")}).");
            }
            store.Report(descriptor.Id, new DeviceReport.Charge(battery.Percent, battery.IsCharging));
        }
    }

    /// <summary>The mouse did not answer on this link: it sleeps, is switched off, or is out of range.</summary>
    public void OnSilence(string link)
    {
        lock (_gate) OnSilenceLocked(link);
    }

    /// <summary>A link went away (the cable was unplugged): its answers no longer outrank the other link's.</summary>
    public void OnLinkClosed(string link)
    {
        lock (_gate) _lastAnswer.Remove(link);
    }

    private void OnSilenceLocked(string link)
    {
        _lastAnswer.Remove(link);
        DateTime freshSince = DateTime.UtcNow - LinkFreshness;
        if (_lastAnswer.Values.Any(answered => answered.At > freshSince)) return;
        _shown = null;
        store.Report(descriptor.Id, new DeviceReport.Asleep());
    }
}

/// <summary>
/// One connection to a mouse (receiver or cable). Listens for battery replies, including those the vendor
/// software triggers with its own polling, and asks by itself only when nobody else has for a while.
/// </summary>
internal sealed class CompxLink(CompxMouse mouse, HidCollection requests, HidCollection replies, bool cable) : DeviceSession
{
    /// <summary>How often to ask when no other program does. The vendor software asks every 30 seconds.</summary>
    private static readonly TimeSpan AskInterval = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(1);

    protected override string Name => $"COMPX {(cable ? "cable" : "receiver")} {mouse.Id}";

    protected override async Task RunAsync(CancellationToken cancellation)
    {
        try
        {
            await ListenAsync(cancellation).ConfigureAwait(false);
        }
        finally
        {
            mouse.OnLinkClosed(replies.Path);
        }
    }

    private async Task ListenAsync(CancellationToken cancellation)
    {
        using HidInputPort? input = HidInputPort.Open(replies);
        if (input == null)
        {
            Log.Warn($"{Name}: cannot open the reply collection.");
            return;
        }

        DateTime nextAsk = DateTime.UtcNow;
        DateTime? replyDue = null;

        while (!cancellation.IsCancellationRequested)
        {
            DateTime now = DateTime.UtcNow;
            if (now >= nextAsk && FullScreen.IsActive())
            {
                // During a game the mouse is left alone; replies the vendor software asks for still arrive.
                nextAsk = now + FullScreen.RecheckAfter;
            }
            else if (now >= nextAsk)
            {
                Ask();
                nextAsk = now + AskInterval;
                replyDue = now + ReplyTimeout;
            }

            DateTime wakeAt = replyDue is DateTime due && due < nextAsk ? due : nextAsk;
            TimeSpan wait = wakeAt - DateTime.UtcNow;
            byte[]? report = await input.ReadAsync(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, cancellation).ConfigureAwait(false);

            if (report != null && CompxProtocol.TryParseBatteryReply(report, out CompxBattery battery))
            {
                // Whoever asked, the answer is fresh, so our own next question can wait a full interval.
                nextAsk = DateTime.UtcNow + AskInterval;
                replyDue = null;
                mouse.OnBattery(replies.Path, cable, battery);
            }
            else if (replyDue is DateTime missed && DateTime.UtcNow >= missed)
            {
                replyDue = null;
                mouse.OnSilence(replies.Path);
            }
        }
    }

    /// <summary>Sends a battery request right away; the reply arrives through the running session.</summary>
    public void AskNow() => Ask();

    private void Ask()
    {
        try
        {
            using HidFeaturePort? port = HidFeaturePort.Open(requests);
            port?.Set(CompxProtocol.BuildBatteryRequest());
        }
        catch (Win32Exception exception)
        {
            Log.Warn($"{Name}: battery request failed ({exception.Message}).");
        }
    }
}
