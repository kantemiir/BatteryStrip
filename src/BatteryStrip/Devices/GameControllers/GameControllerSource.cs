using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.PlayStation;
using Windows.Devices.Power;
using Windows.Gaming.Input;
using Windows.System.Power;

namespace BatteryStrip.Devices.GameControllers;

/// <summary>
/// Game controllers that report their battery to Windows itself: Xbox controllers through the Xbox Wireless
/// adapter or Bluetooth, and compatible ones. Uses the Windows.Gaming.Input API, which only reads.
/// </summary>
internal sealed class GameControllerSource : IDeviceSource
{
    public const string Owner = "gamecontroller";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly DeviceStore _store;
    private readonly DeviceNaming _naming;
    private readonly ContainerClaims _claims;
    private readonly Timer _poll;
    private readonly object _gate = new();
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private bool _subscribed;

    public GameControllerSource(DeviceStore store, DeviceNaming naming, ContainerClaims claims)
    {
        _store = store;
        _naming = naming;
        _claims = claims;
        _poll = new Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    public void Refresh(IReadOnlyList<HidCollection> present)
    {
        if (!_subscribed)
        {
            // The controller list fills in asynchronously; these events also report controllers already connected.
            try
            {
                RawGameController.RawGameControllerAdded += (_, _) => Poll();
                RawGameController.RawGameControllerRemoved += (_, _) => Poll();
                _subscribed = true;
            }
            catch (Exception exception)
            {
                Log.Warn($"Game controllers are unavailable: {exception.Message}");
                return;
            }
        }
        Poll();
    }

    public void Wake(WakeReason reason) => Poll();

    public void Dispose() => _poll.Dispose();

    private void Poll()
    {
        try
        {
            lock (_gate) PollLocked();
        }
        catch (Exception exception)
        {
            Log.Error("Reading game controllers failed", exception);
        }
    }

    private void PollLocked()
    {
        var found = new Dictionary<string, (DeviceDescriptor Descriptor, int Percent, bool Charging)>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (RawGameController controller in RawGameController.RawGameControllers)
        {
            // Sony controllers are read more precisely from their own reports.
            if (controller.HardwareVendorId == PlayStationProtocol.SonyVendorId) continue;

            BatteryReport? report = controller.TryGetBatteryReport();
            if (report == null || report.Status == BatteryStatus.NotPresent) continue;
            if (Percent(report.RemainingCapacityInMilliwattHours, report.FullChargeCapacityInMilliwattHours) is not int percent) continue;

            string id = "pad:" + StableId(controller);
            names.Add(controller.DisplayName);
            string name = _naming.Resolve(id, string.IsNullOrWhiteSpace(controller.DisplayName) ? Text.Current.Gamepad : controller.DisplayName);
            found[id] = (new DeviceDescriptor(id, name, DeviceKind.Gamepad), percent, report.Status == BatteryStatus.Charging);
        }

        // An Xbox controller on Bluetooth is also known to the Bluetooth source, under the same name.
        _claims.SetNames(Owner, names);

        foreach (var pair in found)
        {
            if (_shown.Add(pair.Key))
            {
                _store.Connect(pair.Value.Descriptor);
                Log.Info($"Game controller found: {pair.Value.Descriptor.Name} [{pair.Key}].");
            }
            _store.Report(pair.Key, new DeviceReport.Charge(pair.Value.Percent, pair.Value.Charging));
        }

        foreach (string gone in _shown.Where(id => !found.ContainsKey(id)).ToArray())
        {
            _shown.Remove(gone);
            _store.Disconnect(gone);
        }
    }

    internal static int? Percent(int? remaining, int? full)
    {
        if (remaining is not int r || full is not int f || f <= 0 || r < 0) return null;
        return (int)Math.Round(Math.Min(100.0, 100.0 * r / f));
    }

    /// <summary>The same controller keeps its id across reconnects and restarts.</summary>
    private static string StableId(RawGameController controller)
    {
        string raw = controller.NonRoamableId ?? $"{controller.HardwareVendorId:X4}{controller.HardwareProductId:X4}";
        using var sha = System.Security.Cryptography.SHA1.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
        return $"{controller.HardwareVendorId:X4}:{controller.HardwareProductId:X4}:{BitConverter.ToString(hash, 0, 4).Replace("-", "")}";
    }
}
