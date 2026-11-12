using System;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.Core;
using BatteryStrip.Devices;
using BatteryStrip.Devices.Bluetooth;
using BatteryStrip.Devices.GameControllers;
using BatteryStrip.Devices.Logitech;
using BatteryStrip.Devices.PlayStation;
using Xunit;

namespace BatteryStrip.Tests;

public class PlayStationProtocolTests
{
    private static byte[] Report(byte id, int length, int statusOffset, byte status)
    {
        var report = new byte[length];
        report[0] = id;
        report[statusOffset] = status;
        return report;
    }

    [Theory]
    [InlineData(0x08, 85, false)] // level 8, discharging
    [InlineData(0x1A, 100, true)] // level 10, charging
    [InlineData(0x13, 35, true)]
    [InlineData(0x25, 100, false)] // full
    public void DualSense_over_usb(byte status, int percent, bool charging)
    {
        Assert.Equal(new PadBattery(percent, charging), PlayStationProtocol.Parse(0x0CE6, Report(0x01, 64, 53, status), bluetooth: false));
    }

    [Fact]
    public void DualSense_over_bluetooth_uses_the_full_report()
    {
        Assert.Equal(new PadBattery(55, false), PlayStationProtocol.Parse(0x0CE6, Report(0x31, 78, 54, 0x05), bluetooth: true));
        Assert.Null(PlayStationProtocol.Parse(0x0CE6, Report(0x01, 10, 0, 0x01), bluetooth: true)); // the short report has no battery
    }

    [Fact]
    public void A_padded_bluetooth_short_report_is_not_read_as_an_empty_battery()
    {
        // Windows pads the short report 0x01 to the collection length: byte 53 is zero, which would read as 5 %.
        Assert.True(PlayStationProtocol.IsBluetooth(78));
        Assert.False(PlayStationProtocol.IsBluetooth(64));
        Assert.Null(PlayStationProtocol.Parse(0x0CE6, Report(0x01, 78, 1, 0x80), bluetooth: true));
        Assert.Null(PlayStationProtocol.Parse(0x05C4, Report(0x01, 547, 1, 0x80), bluetooth: true));
    }

    [Fact]
    public void DualSense_errors_give_no_value()
    {
        Assert.Null(PlayStationProtocol.Parse(0x0CE6, Report(0x01, 64, 53, 0xA5), bluetooth: false));
        Assert.Null(PlayStationProtocol.Parse(0x0CE6, Report(0x01, 64, 53, 0xF0), bluetooth: false));
    }

    [Theory]
    [InlineData(0x07, 75, false)] // on battery
    [InlineData(0x0A, 100, false)]
    [InlineData(0x15, 55, true)] // cable, charging
    [InlineData(0x1B, 100, false)] // cable, full
    public void DualShock4_over_usb(byte status, int percent, bool charging)
    {
        Assert.Equal(new PadBattery(percent, charging), PlayStationProtocol.Parse(0x09CC, Report(0x01, 64, 30, status), bluetooth: false));
    }

    [Fact]
    public void DualShock4_over_bluetooth()
    {
        Assert.Equal(new PadBattery(45, false), PlayStationProtocol.Parse(0x05C4, Report(0x11, 78, 32, 0x04), bluetooth: true));
    }

    [Fact]
    public void Unknown_products_are_ignored()
    {
        Assert.Null(PlayStationProtocol.Parse(0x1234, Report(0x01, 64, 53, 0x08), bluetooth: false));
    }
}

public class HidppTests
{
    [Fact]
    public void Requests_are_long_reports_with_our_software_id()
    {
        // Root.GetFeature(0x1004) for device 2.
        Assert.Equal("11 02 00 0D 10 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00", Hex.Text(Hidpp.Request(2, 0, 0, 0x10, 0x04)));
        // Function 1 of feature index 6 for a direct device.
        Assert.Equal("11 FF 06 1D", Hex.Text(Hidpp.Request(0xFF, 6, 1)).Substring(0, 11));
    }

    [Fact]
    public void Matches_only_its_own_answer()
    {
        byte[] answer = Hex.Bytes("11 02 00 0D 05 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00");
        Assert.Equal((byte)5, Hidpp.MatchReply(answer, 2, 0, 0)!.Value.Parameters[0]);
        Assert.Null(Hidpp.MatchReply(answer, 3, 0, 0)); // another device
        Assert.Null(Hidpp.MatchReply(answer, 2, 0, 1)); // another function

        byte[] someoneElses = Hex.Bytes("11 02 00 01 05 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00");
        Assert.Null(Hidpp.MatchReply(someoneElses, 2, 0, 0));
    }

    [Fact]
    public void Recognises_both_error_forms()
    {
        Assert.Equal((byte)0x09, Hidpp.MatchReply(Hex.Bytes("11 01 FF 05 1D 09 00 00 00 00 00 00 00 00 00 00 00 00 00 00"), 1, 5, 1)!.Value.Error);
        Assert.Equal((byte)0x08, Hidpp.MatchReply(Hex.Bytes("10 03 8F 00 1D 08 00"), 3, 0, 1)!.Value.Error);
    }

    [Fact]
    public void Parses_notifications()
    {
        Assert.Equal(new HidppEvent.Connection(2, Linked: false), Hidpp.ParseEvent(Hex.Bytes("10 02 41 04 61 7B 40")));
        Assert.Equal(new HidppEvent.Connection(2, Linked: true), Hidpp.ParseEvent(Hex.Bytes("10 02 41 04 21 7B 40")));

        var battery = Assert.IsType<HidppEvent.Feature>(Hidpp.ParseEvent(Hex.Bytes("11 01 08 00 32 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00")));
        Assert.Equal((byte)8, battery.FeatureIndex);
        Assert.Equal((byte)0x32, battery.Parameters[0]);

        // Our own answers carry a software id, so they are not notifications.
        Assert.Null(Hidpp.ParseEvent(Hex.Bytes("11 01 08 1D 32 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00")));
    }

    [Fact]
    public void Unified_battery()
    {
        Assert.Equal(new HidppBattery(50, false, false), Hidpp.ParseBattery(Hidpp.FeatureUnifiedBattery, Hex.Bytes("32 04 00 00")));
        Assert.Equal(new HidppBattery(80, true, false), Hidpp.ParseBattery(Hidpp.FeatureUnifiedBattery, Hex.Bytes("50 04 01 01")));
        Assert.Equal(new HidppBattery(100, false, false), Hidpp.ParseBattery(Hidpp.FeatureUnifiedBattery, Hex.Bytes("64 08 03 01")));
        Assert.Equal(new HidppBattery(20, false, true), Hidpp.ParseBattery(Hidpp.FeatureUnifiedBattery, Hex.Bytes("00 02 00 00"), percentSupported: false));
        Assert.Null(Hidpp.ParseBattery(Hidpp.FeatureUnifiedBattery, Hex.Bytes("32 04 04 00")));
    }

    [Fact]
    public void Battery_status()
    {
        Assert.Equal(new HidppBattery(70, false, false), Hidpp.ParseBattery(Hidpp.FeatureBatteryStatus, Hex.Bytes("46 32 00")));
        Assert.Equal(new HidppBattery(70, true, false), Hidpp.ParseBattery(Hidpp.FeatureBatteryStatus, Hex.Bytes("46 32 01")));
        Assert.Equal(new HidppBattery(100, false, false), Hidpp.ParseBattery(Hidpp.FeatureBatteryStatus, Hex.Bytes("00 00 03")));
        Assert.Null(Hidpp.ParseBattery(Hidpp.FeatureBatteryStatus, Hex.Bytes("46 32 06")));
    }

    [Fact]
    public void Voltages_are_estimated()
    {
        Assert.Equal(new HidppBattery(83, false, true), Hidpp.ParseBattery(Hidpp.FeatureBatteryVoltage, Hex.Bytes("0F BD 00")));
        Assert.Equal(new HidppBattery(100, false, false), Hidpp.ParseBattery(Hidpp.FeatureBatteryVoltage, Hex.Bytes("10 40 81")));
        Assert.Equal(new HidppBattery(83, true, true), Hidpp.ParseBattery(Hidpp.FeatureAdcMeasurement, Hex.Bytes("0F BD 03")));
        Assert.Equal(new HidppBattery(null, false, true), Hidpp.ParseBattery(Hidpp.FeatureAdcMeasurement, Hex.Bytes("00 00 00"))); // headset off
    }

    [Fact]
    public void Receivers_have_six_slots_and_other_devices_one()
    {
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, Hidpp.CandidateDevices(0xC52B));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, Hidpp.CandidateDevices(0xC548));
        Assert.Equal(new byte[] { Hidpp.DirectDevice }, Hidpp.CandidateDevices(0x0AB5));
    }

    [Fact]
    public void Device_name_chunks_stop_at_padding()
    {
        Assert.Equal("G Pro", Hidpp.NameChunk(Hex.Bytes("47 20 50 72 6F 00 00 00"), 16));
        Assert.Equal("G P", Hidpp.NameChunk(Hex.Bytes("47 20 50 72 6F"), 3));
    }
}

public class BluetoothSourceTests
{
    [Theory]
    [InlineData(@"BTHENUM\DEV_7866F30ABDD8\8&3A358462&0&BLUETOOTHDEVICE_7866F30ABDD8", "7866F30ABDD8")]
    [InlineData(@"BTHLE\DEV_c2a1b3d4e5f6\7&1&0&C2A1B3D4E5F6", "C2A1B3D4E5F6")]
    [InlineData(@"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&00010ECB_PID&20E3\8&3A3&0&7866F30ABDD8_C00000000", null)]
    [InlineData(@"BTH\MS_BTHBRB\7&189AE1DC&0&1", null)]
    public void Only_device_nodes_have_an_address(string instanceId, string? address)
    {
        Assert.Equal(address, BluetoothSource.BluetoothAddress(instanceId));
    }

    [Theory]
    [InlineData("Input.Mouse", (int)DeviceKind.Mouse)]
    [InlineData("Input.Keyboard", (int)DeviceKind.Keyboard)]
    [InlineData("Input.Gaming", (int)DeviceKind.Gamepad)]
    [InlineData("Audio.Headphone", (int)DeviceKind.Headset)]
    [InlineData("Audio.Speakers", (int)DeviceKind.Speaker)]
    [InlineData("Other.Misc", (int)DeviceKind.Other)]
    public void Kind_from_the_windows_category(string category, int kind)
    {
        Assert.Equal((DeviceKind)kind, BluetoothSource.KindOf(new[] { category }));
    }
}

public class ContainerClaimsTests
{
    [Fact]
    public void A_device_claimed_by_a_precise_source_is_left_to_it()
    {
        var claims = new ContainerClaims();
        Guid mouse = Guid.NewGuid();
        claims.Set("logitech", new[] { mouse });
        claims.SetNames("gamecontroller", new[] { "Xbox Wireless Controller" });

        Assert.True(claims.IsClaimedByOther("bluetooth", mouse, "MX Master 3"));
        Assert.True(claims.IsClaimedByOther("bluetooth", Guid.NewGuid(), "xbox wireless controller "));
        Assert.False(claims.IsClaimedByOther("bluetooth", Guid.NewGuid(), "AirPods"));
        Assert.False(claims.IsClaimedByOther("logitech", mouse, null)); // its own claim

        claims.Set("logitech", Array.Empty<Guid>());
        Assert.False(claims.IsClaimedByOther("bluetooth", mouse, null));
    }
}

public class GameControllerSourceTests
{
    [Theory]
    [InlineData(500, 1000, 50)]
    [InlineData(1000, 1000, 100)]
    [InlineData(1100, 1000, 100)]
    [InlineData(0, 1000, 0)]
    public void Percent_from_capacity(int remaining, int full, int percent)
    {
        Assert.Equal(percent, GameControllerSource.Percent(remaining, full));
    }

    [Fact]
    public void Missing_capacity_gives_no_value()
    {
        Assert.Null(GameControllerSource.Percent(null, 1000));
        Assert.Null(GameControllerSource.Percent(500, 0));
    }
}

public class WakeSignalTests
{
    [Fact]
    public async Task Wakes_a_waiting_session_early()
    {
        var signal = new WakeSignal();
        Task<bool> waiting = signal.WaitAsync(TimeSpan.FromMinutes(5), CancellationToken.None);
        signal.Set();
        Assert.True(await waiting);
    }

    [Fact]
    public async Task A_wake_before_the_wait_is_not_lost_and_is_used_once()
    {
        var signal = new WakeSignal();
        signal.Set();
        Assert.True(await signal.WaitAsync(TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None));
    }
}
