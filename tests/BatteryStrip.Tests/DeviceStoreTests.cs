using System;
using System.Collections.Generic;
using System.Linq;
using BatteryStrip.Core;
using Xunit;

namespace BatteryStrip.Tests;

public class DeviceStoreTests
{
    private static readonly DeviceDescriptor Mouse = new("mouse", "Мышь", DeviceKind.Mouse);
    private static readonly DeviceDescriptor Headset = new("headset", "Наушники", DeviceKind.Headset);
    private static readonly DeviceDescriptor Gamepad = new("gamepad", "Геймпад", DeviceKind.Gamepad);

    private DateTime _now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private DeviceStore NewStore() => new(() => _now);

    [Fact]
    public void A_device_appears_with_its_first_reading()
    {
        DeviceStore store = NewStore();
        store.Connect(Mouse);
        Assert.Empty(store.Snapshot);

        store.Report(Mouse.Id, new DeviceReport.Charge(82, IsCharging: false));

        DeviceSnapshot shown = Assert.Single(store.Snapshot);
        Assert.Equal(82, shown.Percent);
        Assert.Equal(ChargeLevel.Normal, shown.Level);
        Assert.Equal(_now, shown.UpdatedUtc);
        Assert.False(shown.IsAsleep);
    }

    [Fact]
    public void Devices_are_ordered_mouse_headset_gamepad()
    {
        DeviceStore store = NewStore();
        foreach (DeviceDescriptor device in new[] { Gamepad, Headset, Mouse })
        {
            store.Connect(device);
            store.Report(device.Id, new DeviceReport.Charge(50, false));
        }
        Assert.Equal(new[] { "mouse", "headset", "gamepad" }, store.Snapshot.Select(d => d.Id));
    }

    [Fact]
    public void A_sleeping_device_keeps_its_last_value()
    {
        DeviceStore store = NewStore();
        store.Connect(Mouse);
        store.Report(Mouse.Id, new DeviceReport.Charge(47, false));
        DateTime readAt = _now;

        _now = _now.AddHours(2);
        store.Report(Mouse.Id, new DeviceReport.Asleep());

        DeviceSnapshot shown = Assert.Single(store.Snapshot);
        Assert.True(shown.IsAsleep);
        Assert.Equal(47, shown.Percent);
        Assert.Equal(readAt, shown.UpdatedUtc);
    }

    [Fact]
    public void A_device_that_never_answered_is_not_shown()
    {
        DeviceStore store = NewStore();
        store.Connect(Mouse);
        store.Report(Mouse.Id, new DeviceReport.Asleep());
        Assert.Empty(store.Snapshot);
    }

    [Fact]
    public void A_device_without_battery_reporting_shows_no_value()
    {
        DeviceStore store = NewStore();
        store.Connect(Gamepad);
        store.Report(Gamepad.Id, new DeviceReport.Unsupported());
        Assert.Null(Assert.Single(store.Snapshot).Percent);
    }

    [Fact]
    public void A_disconnected_device_disappears()
    {
        DeviceStore store = NewStore();
        store.Connect(Mouse);
        store.Report(Mouse.Id, new DeviceReport.Charge(82, false));
        store.Disconnect(Mouse.Id);
        Assert.Empty(store.Snapshot);
    }

    [Fact]
    public void A_reconnected_device_shows_its_remembered_value_dimmed()
    {
        DeviceStore store = NewStore();
        store.Connect(Mouse);
        store.Report(Mouse.Id, new DeviceReport.Charge(15, false));
        store.Disconnect(Mouse.Id);

        store.Connect(Mouse);

        DeviceSnapshot shown = Assert.Single(store.Snapshot);
        Assert.Equal(15, shown.Percent);
        Assert.True(shown.IsAsleep);
        Assert.Equal(ChargeLevel.Low, shown.Level);
    }

    [Fact]
    public void Remembered_values_survive_a_restart()
    {
        DeviceStore first = NewStore();
        first.Connect(Mouse);
        first.Report(Mouse.Id, new DeviceReport.Charge(64, false));
        IReadOnlyDictionary<string, RememberedCharge> saved = first.Export();

        DeviceStore second = NewStore();
        second.Import(saved);
        second.Connect(Mouse);

        Assert.Equal(64, Assert.Single(second.Snapshot).Percent);
    }

    [Fact]
    public void Colour_band_uses_hysteresis_between_readings()
    {
        DeviceStore store = NewStore();
        store.Connect(Mouse);
        store.Report(Mouse.Id, new DeviceReport.Charge(20, false));
        store.Report(Mouse.Id, new DeviceReport.Charge(21, false));
        Assert.Equal(ChargeLevel.Low, store.Snapshot[0].Level);
    }

    [Fact]
    public void Warns_once_per_threshold_and_not_again_after_a_restart()
    {
        var warnings = new List<(string Id, int Threshold)>();
        DeviceStore store = NewStore();
        store.LowBattery += (device, threshold) => warnings.Add((device.Id, threshold));
        store.Connect(Mouse);
        store.Report(Mouse.Id, new DeviceReport.Charge(25, false));
        store.Report(Mouse.Id, new DeviceReport.Charge(20, false));
        store.Report(Mouse.Id, new DeviceReport.Charge(20, false));
        Assert.Equal(new[] { ("mouse", 20) }, warnings);

        DeviceStore restarted = NewStore();
        restarted.LowBattery += (device, threshold) => warnings.Add((device.Id, threshold));
        restarted.Import(store.Export());
        restarted.Connect(Mouse);
        restarted.Report(Mouse.Id, new DeviceReport.Charge(20, false));
        Assert.Single(warnings);
    }

    [Fact]
    public void Changed_fires_only_when_something_visible_changed()
    {
        int changes = 0;
        DeviceStore store = NewStore();
        store.Changed += () => changes++;

        store.Connect(Mouse);
        Assert.Equal(0, changes);

        store.Report(Mouse.Id, new DeviceReport.Charge(82, false));
        Assert.Equal(1, changes);

        store.Report(Mouse.Id, new DeviceReport.Charge(82, false));
        Assert.Equal(1, changes);

        store.Report("unknown", new DeviceReport.Charge(10, false));
        Assert.Equal(1, changes);
    }
}
