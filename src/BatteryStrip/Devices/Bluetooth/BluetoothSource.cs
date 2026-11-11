using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.Bluetooth;

/// <summary>
/// Bluetooth devices whose charge Windows already knows: headphones, earbuds, speakers, mice and keyboards
/// that report it over Bluetooth (the value shown in Settings → Bluetooth). Windows talks to the device;
/// this source only reads the property Windows keeps, so it never disturbs the device.
/// </summary>
internal sealed class BluetoothSource : IDeviceSource
{
    public const string Owner = "bluetooth";

    /// <summary>Windows updates the value by itself; reading it is cheap, so once a minute is plenty.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private static readonly DevicePropertyKey[] Keys =
    [
        DevicePropertyKey.FriendlyName,
        DevicePropertyKey.ContainerId,
        DevicePropertyKey.ContainerCategory,
        DevicePropertyKey.BluetoothBattery,
        DevicePropertyKey.IsConnected,
    ];

    private readonly DeviceStore _store;
    private readonly DeviceNaming _naming;
    private readonly ContainerClaims _claims;
    private readonly Timer _poll;
    private readonly object _gate = new();
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);

    public BluetoothSource(DeviceStore store, DeviceNaming naming, ContainerClaims claims)
    {
        _store = store;
        _naming = naming;
        _claims = claims;
        _poll = new Timer(_ => Scan(), null, PollInterval, PollInterval);
    }

    public void Refresh(IReadOnlyList<HidCollection> present) => Scan();

    public void Wake(WakeReason reason) => Scan();

    public void Dispose() => _poll.Dispose();

    private void Scan()
    {
        try
        {
            lock (_gate) ScanLocked();
        }
        catch (Exception exception)
        {
            Log.Error("Bluetooth scan failed", exception);
        }
    }

    private void ScanLocked()
    {
        var found = new Dictionary<string, (DeviceDescriptor Descriptor, int Percent)>(StringComparer.Ordinal);
        IEnumerable<PnpDevice> nodes = Pnp.List("BTHENUM", Keys).Concat(Pnp.List("BTHLE", Keys));
        foreach (PnpDevice node in nodes)
        {
            if (BluetoothAddress(node.InstanceId) is not string address) continue;
            if (node.GetBool(DevicePropertyKey.IsConnected) != true) continue;
            if (node.GetByte(DevicePropertyKey.BluetoothBattery) is not byte percent || percent > 100) continue;
            string? friendlyName = node.GetString(DevicePropertyKey.FriendlyName);
            if (_claims.IsClaimedByOther(Owner, node.GetGuid(DevicePropertyKey.ContainerId) ?? Guid.Empty, friendlyName)) continue;

            // A dual-mode device has a classic and a Low Energy node; both lead to the same address.
            string id = "bt:" + address;
            string name = _naming.Resolve(id, friendlyName ?? "Bluetooth-устройство");
            DeviceKind kind = KindOf(node.GetStrings(DevicePropertyKey.ContainerCategory));
            found[id] = (new DeviceDescriptor(id, name, kind), percent);
        }

        foreach (var (id, (descriptor, percent)) in found.Select(p => (p.Key, p.Value)))
        {
            if (_shown.Add(id))
            {
                _store.Connect(descriptor);
                Log.Info($"Bluetooth device found: {descriptor.Name} [{id}], {descriptor.Kind}.");
            }
            _store.Report(id, new DeviceReport.Charge(percent, IsCharging: false));
        }

        foreach (string gone in _shown.Where(id => !found.ContainsKey(id)).ToArray())
        {
            _shown.Remove(gone);
            _store.Disconnect(gone);
            Log.Info($"Bluetooth device gone: {gone}.");
        }
    }

    /// <summary>"BTHENUM\DEV_7866F30ABDD8\..." → "7866F30ABDD8". Service nodes and adapters are not devices.</summary>
    internal static string? BluetoothAddress(string instanceId)
    {
        string[] parts = instanceId.Split('\\');
        if (parts.Length < 2) return null;
        bool bus = parts[0].Equals("BTHENUM", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("BTHLE", StringComparison.OrdinalIgnoreCase);
        if (!bus || !parts[1].StartsWith("DEV_", StringComparison.OrdinalIgnoreCase)) return null;
        string address = parts[1].Substring(4).ToUpperInvariant();
        return address.Length == 12 && address.All(Uri.IsHexDigit) ? address : null;
    }

    /// <summary>Device type from the categories Windows assigns, such as "Input.Mouse" or "Audio.Headphone".</summary>
    internal static DeviceKind KindOf(IReadOnlyList<string> categories)
    {
        bool Has(string prefix) => categories.Any(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (Has("Input.Mouse") || Has("Input.Touchpad") || Has("Input.Pen")) return DeviceKind.Mouse;
        if (Has("Input.Keyboard")) return DeviceKind.Keyboard;
        if (Has("Input.Gaming") || Has("Input.Gamepad") || Has("Input.Joystick")) return DeviceKind.Gamepad;
        if (Has("Audio.Headphone") || Has("Audio.Headset") || Has("Communication.Headset")) return DeviceKind.Headset;
        if (Has("Audio.Speakers") || Has("Audio.Speaker")) return DeviceKind.Speaker;
        return DeviceKind.Other;
    }
}
