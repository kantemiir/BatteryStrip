using System;
using System.Collections.Generic;
using BatteryStrip.Core;
using BatteryStrip.Devices.Atk;
using BatteryStrip.Devices.Bluetooth;
using BatteryStrip.Devices.Compx;
using BatteryStrip.Devices.Corsair;
using BatteryStrip.Devices.G435;
using BatteryStrip.Devices.GameControllers;
using BatteryStrip.Devices.Generic;
using BatteryStrip.Devices.Glorious;
using BatteryStrip.Devices.HeadsetControl;
using BatteryStrip.Devices.HyperX;
using BatteryStrip.Devices.Jbl;
using BatteryStrip.Devices.Kysona;
using BatteryStrip.Devices.Logitech;
using BatteryStrip.Devices.PlayStation;
using BatteryStrip.Devices.Polled;
using BatteryStrip.Devices.Razer;
using BatteryStrip.Devices.SteelSeries;

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
        IDeviceSource Polled(PolledHidDriver driver) => new PolledHidSource(store, naming, claims, driver);
        return
        [
            new CompxSource(store, naming, claims),
            new G435Source(store, naming, claims, options),
            new HidppSource(store, naming, claims),
            new PlayStationSource(store, naming, claims),
            Polled(new GloriousDriver()),
            Polled(new KysonaDriver()),
            Polled(new RazerDriver()),
            Polled(new SteelSeriesDriver()),
            Polled(new HyperXDriver()),
            Polled(new CorsairDriver()),
            new AtkSerialSource(store, naming),
            new GameControllerSource(store, naming, claims),
            new JblSource(store, naming, claims),
            new HeadsetControlSource(store, naming, claims),
            // General sources last: they show only what no vendor source above has claimed.
            Polled(new GenericHidDriver()),
            new BluetoothSource(store, naming, claims),
        ];
    }
}
