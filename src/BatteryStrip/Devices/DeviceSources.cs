using System.Collections.Generic;
using BatteryStrip.Core;
using BatteryStrip.Devices.Compx;
using BatteryStrip.Devices.G435;

namespace BatteryStrip.Devices;

/// <summary>The list of supported device families. A new family is added here.</summary>
internal static class DeviceSources
{
    public static IReadOnlyList<IDeviceSource> Create(DeviceStore store, DeviceNaming naming) =>
    [
        new CompxSource(store, naming),
        new G435Source(store, naming),
    ];
}
