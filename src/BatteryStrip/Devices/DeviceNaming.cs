using System;
using System.Collections.Generic;

namespace BatteryStrip.Devices;

/// <summary>Device names shown to the user: the name a driver suggests, unless the settings file overrides it.</summary>
internal sealed class DeviceNaming(IReadOnlyDictionary<string, string> overrides)
{
    public static DeviceNaming Default { get; } = new(new Dictionary<string, string>());

    public string Resolve(string deviceId, string suggested) =>
        overrides.TryGetValue(deviceId, out string? custom) && !string.IsNullOrWhiteSpace(custom) ? custom.Trim() : suggested;
}
