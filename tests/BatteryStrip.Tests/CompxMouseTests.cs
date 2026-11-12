using System;
using BatteryStrip.Core;
using BatteryStrip.Devices.Compx;
using Xunit;

namespace BatteryStrip.Tests;

public class CompxMouseTests
{
    private const string Receiver = @"\\?\hid#vid_25a7&pid_fa7c&mi_01&col05";
    private const string Cable = @"\\?\hid#vid_25a7&pid_fa7b&mi_01&col05";

    private static (DeviceStore Store, CompxMouse Mouse) NewMouse()
    {
        var store = new DeviceStore(() => DateTime.UtcNow);
        var descriptor = new DeviceDescriptor("compx:25A7:FA7C", "Мышь Lamzu", DeviceKind.Mouse);
        store.Connect(descriptor);
        return (store, new CompxMouse(store, descriptor));
    }

    private static int? Shown(DeviceStore store) => Assert.Single(store.Snapshot).Percent;

    [Fact]
    public void While_on_the_cable_the_receivers_stale_answers_are_ignored()
    {
        var (store, mouse) = NewMouse();

        // Seen on a Lamzu after plugging in: the receiver repeats the old value, the cable reports the new one.
        mouse.OnBattery(Receiver, cable: false, new CompxBattery(false, 30, true));
        Assert.Equal(30, Shown(store));
        mouse.OnBattery(Cable, cable: true, new CompxBattery(false, 95, true));
        Assert.Equal(95, Shown(store));
        mouse.OnBattery(Receiver, cable: false, new CompxBattery(false, 35, true));
        Assert.Equal(95, Shown(store));
        mouse.OnBattery(Cable, cable: true, new CompxBattery(false, 100, false));
        mouse.OnBattery(Receiver, cable: false, new CompxBattery(false, 95, true));
        Assert.Equal(100, Shown(store));
        Assert.False(Assert.Single(store.Snapshot).IsCharging);
    }

    [Fact]
    public void After_unplugging_the_receiver_counts_again()
    {
        var (store, mouse) = NewMouse();
        mouse.OnBattery(Cable, cable: true, new CompxBattery(false, 60, true));
        mouse.OnLinkClosed(Cable);

        mouse.OnBattery(Receiver, cable: false, new CompxBattery(false, 58, false));
        Assert.Equal(58, Shown(store));
    }

    [Fact]
    public void A_sleeping_receiver_does_not_hide_a_fresh_cable_reading()
    {
        var (store, mouse) = NewMouse();
        mouse.OnBattery(Cable, cable: true, new CompxBattery(false, 70, true));
        mouse.OnBattery(Receiver, cable: false, new CompxBattery(IsAsleep: true, 0, false));
        Assert.False(Assert.Single(store.Snapshot).IsAsleep);
        Assert.Equal(70, Shown(store));
    }
}
