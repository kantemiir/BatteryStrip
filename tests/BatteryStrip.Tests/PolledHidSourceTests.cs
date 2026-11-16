using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.Core;
using BatteryStrip.Devices;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.Polled;
using Xunit;

namespace BatteryStrip.Tests;

public class PolledHidSourceTests
{
    private sealed class FakeDriver : PolledHidDriver
    {
        public override string Owner => "fake";

        public override bool Wants(PolledTarget target) => false;

        public override Task<IReadOnlyList<PolledReading>?> ReadAsync(PolledTarget target, CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<PolledReading>?>(null);
    }

    private readonly DeviceStore _store = new(() => DateTime.UtcNow);
    private readonly PolledHidSource _source;

    public PolledHidSourceTests()
    {
        _source = new PolledHidSource(_store, DeviceNaming.Default, new ContainerClaims(), new FakeDriver());
    }

    private PolledSession Link(ushort productId) =>
        new(_source, new PolledTarget(0x1234, productId, Guid.NewGuid(), Array.Empty<HidCollection>(), Array.Empty<HidCollection>()));

    private static PolledReading Mouse(DeviceReport report, bool cable = false) => new("mouse", "Test mouse", DeviceKind.Mouse, report, cable);

    private DeviceSnapshot? Shown => _store.Snapshot.SingleOrDefault(d => d.Id == "fake:mouse");

    [Fact]
    public void A_device_never_seen_awake_is_not_shown()
    {
        _source.OnReadings(Link(1), new[] { Mouse(new DeviceReport.Asleep()) });
        Assert.Null(Shown);
    }

    [Fact]
    public void The_cable_outranks_the_receiver_whichever_answers_last()
    {
        PolledSession receiver = Link(1), cable = Link(2);
        _source.OnReadings(cable, new[] { Mouse(new DeviceReport.Charge(100, false), cable: true) });
        _source.OnReadings(receiver, new[] { Mouse(new DeviceReport.Charge(30, false)) });
        Assert.Equal(100, Shown!.Percent);

        _source.OnReadings(receiver, new[] { Mouse(new DeviceReport.Charge(31, false)) });
        Assert.Equal(100, Shown!.Percent);
    }

    [Fact]
    public void A_charging_reading_outranks_a_stale_receiver_without_cable_flag()
    {
        PolledSession receiver = Link(1), other = Link(2);
        _source.OnReadings(other, new[] { Mouse(new DeviceReport.Charge(64, true)) });
        _source.OnReadings(receiver, new[] { Mouse(new DeviceReport.Charge(60, false)) });
        Assert.Equal(64, Shown!.Percent);
        Assert.True(Shown.IsCharging);
    }

    [Fact]
    public void Silence_puts_a_shown_device_to_sleep_and_unplugging_removes_it()
    {
        PolledSession link = Link(1);
        _source.OnReadings(link, new[] { Mouse(new DeviceReport.Charge(50, false)) });
        Assert.False(Shown!.IsAsleep);

        _source.OnSilence(link);
        Assert.True(Shown!.IsAsleep);
        Assert.Equal(50, Shown.Percent);

        link.Dispose();
        Assert.Null(Shown);
    }

    [Fact]
    public void A_device_no_longer_listed_by_its_receiver_sleeps()
    {
        PolledSession link = Link(1);
        _source.OnReadings(link, new[] { Mouse(new DeviceReport.Charge(50, false)) });
        _source.OnReadings(link, Array.Empty<PolledReading>());
        Assert.True(Shown!.IsAsleep);
    }

    [Fact]
    public void A_reading_after_the_link_ended_is_dropped()
    {
        PolledSession link = Link(1);
        link.Dispose();
        _source.OnReadings(link, new[] { Mouse(new DeviceReport.Charge(50, false)) });
        Assert.Null(Shown);
    }

    [Fact]
    public void Charging_without_a_level_keeps_the_last_value()
    {
        PolledSession link = Link(1);
        _source.OnReadings(link, new[] { Mouse(new DeviceReport.Charge(42, false)) });
        _source.OnReadings(link, new[] { Mouse(new DeviceReport.Stale("Charging")) });
        Assert.Equal(42, Shown!.Percent);
        Assert.False(Shown.IsAsleep);
    }
}
