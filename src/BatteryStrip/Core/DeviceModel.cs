using System;

namespace BatteryStrip.Core;

/// <summary>Device type. The order of the values is the left-to-right order on the strip.</summary>
internal enum DeviceKind
{
    Mouse,
    Headset,
    Gamepad,
    Keyboard,
    Other,
}

/// <summary>Colour band of a charge value.</summary>
internal enum ChargeLevel
{
    Normal,
    Low,
    Critical,
}

/// <summary>What a device driver knows about a device before any reading arrives.</summary>
/// <param name="Id">Stable key, the same across restarts, for example "compx:25A7:FA7C".</param>
/// <param name="Name">Name shown in the flyout.</param>
internal sealed record DeviceDescriptor(string Id, string Name, DeviceKind Kind);

/// <summary>One result of asking a device for its charge.</summary>
internal abstract record DeviceReport
{
    private DeviceReport() { }

    /// <summary>The device answered with a charge value.</summary>
    /// <param name="IsEstimate">True when the percent is derived from voltage, not reported by the device.</param>
    public sealed record Charge(int Percent, bool IsCharging, bool IsEstimate = false) : DeviceReport;

    /// <summary>The device is connected but does not answer; the last known value stays on screen.</summary>
    public sealed record Asleep : DeviceReport;

    /// <summary>The device is connected and has no way to report its charge.</summary>
    public sealed record Unsupported : DeviceReport;
}

/// <summary>A device exactly as the strip and the flyout show it. Immutable, safe to pass between threads.</summary>
/// <param name="Percent">Null when the device has never reported a charge.</param>
/// <param name="UpdatedUtc">When <paramref name="Percent"/> was read.</param>
internal sealed record DeviceSnapshot(
    string Id,
    string Name,
    DeviceKind Kind,
    int? Percent,
    bool IsCharging,
    bool IsAsleep,
    bool IsEstimate,
    ChargeLevel Level,
    DateTime? UpdatedUtc);
