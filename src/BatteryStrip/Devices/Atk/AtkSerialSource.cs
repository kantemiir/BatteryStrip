using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.Atk;

/// <summary>
/// The ATK N9 Pro headset, through its 2.4 GHz dongle or its USB cable. Its charge is not on HID: the headset answers
/// on a USB virtual COM port. Every five minutes, after a wake-up, or when the listed COM ports change, the source opens
/// the port, sends two read requests, and closes it again. While the ATK desktop app holds the port, the headset is
/// skipped quietly. Nothing is sent while a full-screen program runs.
/// </summary>
internal sealed class AtkSerialSource : IDeviceSource
{
    public const string Owner = "atk-serial";

    private const string DefaultName = "ATK N9 Pro";

    /// <summary>How often the headset is asked when nothing asks sooner.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>How long a reply may take to arrive. The reference reads with 800 ms.</summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(800);

    private static readonly DevicePropertyKey[] PortKeys = [DevicePropertyKey.FriendlyName];

    private readonly DeviceStore _store;
    private readonly DeviceNaming _naming;
    private readonly WakeSignal _wake = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;

    /// <summary>Headsets connected to the store. Touched only by the scan loop.</summary>
    private readonly HashSet<string> _connected = new(StringComparer.Ordinal);

    /// <summary>The COM ports listed by the last refresh; null before the first one. Touched only by refresh.</summary>
    private string[]? _listed;

    /// <summary>Whether the last listing had an ATK port; without one the 5-minute round does nothing at all.</summary>
    private volatile bool _anyPort;

    public AtkSerialSource(DeviceStore store, DeviceNaming naming)
    {
        _store = store;
        _naming = naming;
        _run = Task.Run(() => LoopAsync(_stop.Token));
    }

    /// <summary>
    /// Compares the COM ports Windows lists with the ones seen last time, without opening anything. When they differ
    /// (the dongle or the cable was plugged in or out), the headset is asked at once.
    /// </summary>
    public void Refresh(IReadOnlyList<HidCollection> present)
    {
        try
        {
            string[] listed = FindPorts().Select(p => p.ProductId.ToString("X4") + "=" + p.PortName)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            if (_listed != null && listed.SequenceEqual(_listed, StringComparer.Ordinal)) return;
            _listed = listed;
            _anyPort = listed.Length > 0;
            _wake.Set();
        }
        catch (Exception exception)
        {
            Log.Error($"{DefaultName}: listing the COM ports failed", exception);
        }
    }

    /// <summary>After a sleep or a manual refresh the headset is asked now, unless a full-screen program runs.</summary>
    public void Wake(WakeReason reason) => _wake.Set();

    /// <summary>Stops the scan loop. A scan in progress finishes its exchange first, for up to six seconds.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _wake.Set();
        _run.Wait(TimeSpan.FromSeconds(6));
    }

    private async Task LoopAsync(CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                bool woken = await _wake.WaitAsync(Interval, cancellation).ConfigureAwait(false);
                // A refresh that saw the ports change wakes the loop even when they are all gone, to take the headset off.
                if (!woken && !_anyPort) continue;
                if (FullScreen.IsActive()) continue;

                try
                {
                    Scan();
                }
                catch (Exception exception)
                {
                    Log.Error($"{DefaultName}: reading failed", exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Asks every headset whose COM port is listed. A headset whose port has gone leaves the strip.</summary>
    private void Scan()
    {
        IReadOnlyList<AtkPort> ports = FindPorts();
        var present = new HashSet<string>(StringComparer.Ordinal);

        foreach (ushort product in ports.Select(p => p.ProductId).Distinct())
        {
            string id = IdOf(product);
            present.Add(id);

            // The first port of this product that answers is the one used.
            Answer? answer = null;
            foreach (AtkPort port in ports.Where(p => p.ProductId == product))
            {
                answer = Ask(port.PortName);
                if (answer != null) break;
            }
            Show(id, answer);
        }

        foreach (string gone in _connected.Where(id => !present.Contains(id)).ToArray())
        {
            _connected.Remove(gone);
            _store.Disconnect(gone);
            Log.Info($"{DefaultName} gone: {gone}.");
        }
    }

    /// <summary>
    /// Shows what the headset said. A headset is connected to the strip only once it has reported a charge; before that,
    /// a headset that is off is not shown at all.
    /// </summary>
    private void Show(string id, Answer? answer)
    {
        // The port is held by another program or did not answer: what is shown stays as it is.
        if (answer == null) return;

        if (!answer.Connected)
        {
            if (_connected.Contains(id)) _store.Report(id, new DeviceReport.Asleep());
            return;
        }

        // Connected, but no battery reply this time: nothing new to show.
        if (answer.Percent is not int percent) return;

        if (_connected.Add(id))
        {
            _store.Connect(new DeviceDescriptor(id, _naming.Resolve(id, DefaultName), DeviceKind.Headset));
            Log.Info($"{DefaultName} found ({id}).");
        }
        _store.Report(id, new DeviceReport.Charge(percent, answer.Charging));
    }

    /// <summary>
    /// Opens the port, asks for the status and, when the headset is connected, for the battery, then closes the port.
    /// Null when the port cannot be opened (the ATK app holds it) or nothing answers.
    /// </summary>
    private static Answer? Ask(string portName)
    {
        try
        {
            using var port = new SerialPort(portName, AtkSerialProtocol.BaudRate, Parity.None, 8, StopBits.One)
            {
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = 300,
                WriteTimeout = 300,
            };
            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            byte[]? status = Exchange(port, AtkSerialProtocol.StatusCommand, AtkSerialProtocol.StatusSub);
            if (status == null) return null;
            if (!AtkSerialProtocol.IsConnected(status)) return new Answer(false, null, false);

            byte[]? battery = Exchange(port, AtkSerialProtocol.BatteryCommand, AtkSerialProtocol.BatterySub);
            if (battery == null) return new Answer(true, null, false);

            (int percent, bool charging) = AtkSerialProtocol.ParseBattery(battery);
            return new Answer(true, percent, charging);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            // Busy, or gone between the listing and the open: skipped quietly.
            return null;
        }
    }

    /// <summary>Sends one request and returns the reply to it. Frames the headset sends on its own are skipped.</summary>
    private static byte[]? Exchange(SerialPort port, byte command, byte sub)
    {
        byte[] request = AtkSerialProtocol.Request(command, sub);
        port.Write(request, 0, request.Length);

        var received = new List<byte>();
        var chunk = new byte[128];
        DateTime until = DateTime.UtcNow + ReplyTimeout;
        while (DateTime.UtcNow < until)
        {
            int waiting = port.BytesToRead;
            if (waiting == 0)
            {
                Thread.Sleep(10);
                continue;
            }

            int count = port.Read(chunk, 0, Math.Min(waiting, chunk.Length));
            received.AddRange(new ArraySegment<byte>(chunk, 0, count));
            if (AtkSerialProtocol.TakeReply(received, command, sub) is byte[] reply) return reply;
        }
        return null;
    }

    /// <summary>The headset's COM ports as Windows lists them. Only device properties are read; no port is opened.</summary>
    private static IReadOnlyList<AtkPort> FindPorts()
    {
        var found = new List<AtkPort>();
        foreach (PnpDevice node in Pnp.List("USB", PortKeys, device => AtkSerialProtocol.ProductIdOf(device.InstanceId) != null))
        {
            ushort? product = AtkSerialProtocol.ProductIdOf(node.InstanceId);
            string? port = AtkSerialProtocol.PortNameOf(node.GetString(DevicePropertyKey.FriendlyName));
            if (product is ushort productId && port != null) found.Add(new AtkPort(productId, port));
        }
        return found;
    }

    private static string IdOf(ushort product) => $"{Owner}:{AtkSerialProtocol.VendorId:X4}:{product:X4}";

    /// <summary>What the headset said when its port was asked. Connected is false when the headset is switched off or out of range.</summary>
    private sealed record Answer(bool Connected, int? Percent, bool Charging);

    /// <summary>A COM port of the headset, with the product it belongs to.</summary>
    private sealed record AtkPort(ushort ProductId, string PortName);
}
