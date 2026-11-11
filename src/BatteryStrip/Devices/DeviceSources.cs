using System;
using System.Collections.Generic;
using BatteryStrip.Core;
using BatteryStrip.Devices.Bluetooth;
using BatteryStrip.Devices.Compx;
using BatteryStrip.Devices.G435;
using BatteryStrip.Devices.GameControllers;
using BatteryStrip.Devices.Logitech;
using BatteryStrip.Devices.PlayStation;

namespace BatteryStrip.Devices;

/// <summary>Choices device sources need from the user.</summary>
/// <param name="G435Allowed">Whether the G435 may be read through its receiver's service mode; null: not asked yet.</param>
/// <param name="AskG435">Asks the user about the G435; called when a receiver shows up and the answer is unknown.</param>
internal sealed record DeviceOptions(Func<bool?> G435Allowed, Action AskG435)
{
    /// <summary>For --diagnose and tests: nothing that writes to a device beyond plain reads.</summary>
    public static DeviceOptions ReadOnly { get; } = new(() => false, () => { });
}

/// <summary>The list of supported device families. A new family is added here.</summary>
internal static class DeviceSources
{
    /// <summary>
    /// Precise sources first: they claim their devices before the general Bluetooth source looks, so a device
    /// visible to both is shown once, with the more precise reading.
    /// </summary>
    public static IReadOnlyList<IDeviceSource> Create(DeviceStore store, DeviceNaming naming, DeviceOptions options)
    {
        var claims = new ContainerClaims();
        return
        [
            new CompxSource(store, naming, claims),
            new G435Source(store, naming, claims, options),
            new HidppSource(store, naming, claims),
            new PlayStationSource(store, naming, claims),
            new GameControllerSource(store, naming, claims),
            new BluetoothSource(store, naming, claims),
        ];
    }
}
