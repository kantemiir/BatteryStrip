using System.Collections.Generic;
using BatteryStrip.Core;
using BatteryStrip.Devices.Bluetooth;
using BatteryStrip.Devices.Compx;
using BatteryStrip.Devices.G435;
using BatteryStrip.Devices.GameControllers;
using BatteryStrip.Devices.Logitech;
using BatteryStrip.Devices.PlayStation;

namespace BatteryStrip.Devices;

/// <summary>The list of supported device families. A new family is added here.</summary>
internal static class DeviceSources
{
    /// <summary>
    /// Precise sources first: they claim their devices before the general Bluetooth source looks, so a device
    /// visible to both is shown once, with the more precise reading.
    /// </summary>
    public static IReadOnlyList<IDeviceSource> Create(DeviceStore store, DeviceNaming naming)
    {
        var claims = new ContainerClaims();
        return
        [
            new CompxSource(store, naming, claims),
            new G435Source(store, naming, claims),
            new HidppSource(store, naming, claims),
            new PlayStationSource(store, naming, claims),
            new GameControllerSource(store, naming, claims),
            new BluetoothSource(store, naming, claims),
        ];
    }
}
