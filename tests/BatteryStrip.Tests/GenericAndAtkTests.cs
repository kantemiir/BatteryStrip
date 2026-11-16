using System;
using System.Collections.Generic;
using BatteryStrip.Devices.Atk;
using BatteryStrip.Devices.Generic;
using Xunit;

namespace BatteryStrip.Tests;

public class GenericAndAtkTests
{
    [Theory]
    [InlineData(50, 0, 100, 50)]
    [InlineData(0, 0, 255, 0)]
    [InlineData(255, 0, 255, 100)]
    [InlineData(128, 0, 255, 50)]
    [InlineData(0, -128, 127, 50)]
    [InlineData(-5, 0, 100, 0)]
    [InlineData(120, 0, 100, 100)]
    [InlineData(1, 1, 101, 0)]
    [InlineData(101, 1, 101, 100)]
    [InlineData(50, 100, 50, 50)]
    [InlineData(77, 0, 0, 77)]
    public void Logical_range_maps_onto_percent(int value, int min, int max, int expected) =>
        Assert.Equal((int?)expected, GenericHidDriver.ScalePercent(value, min, max));

    [Theory]
    [InlineData(101, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(200, 50, 50)]
    public void Value_without_a_usable_range_gives_nothing(int value, int min, int max) =>
        Assert.Null(GenericHidDriver.ScalePercent(value, min, max));

    [Theory]
    [InlineData("USB Serial Device (COM5)", "COM5")]
    [InlineData("USB Serial Device (COM12)", "COM12")]
    [InlineData("USB Serial Device (com7)  ", "COM7")]
    public void Port_name_is_read_from_the_end_of_the_friendly_name(string name, string expected) =>
        Assert.Equal(expected, AtkSerialProtocol.PortNameOf(name));

    [Theory]
    [InlineData("USB Serial Device (COM)")]
    [InlineData("USB Serial Device")]
    [InlineData("USB Serial Device (COM4) extra")]
    [InlineData("Port (COMx)")]
    [InlineData("Port (COM0)")]
    [InlineData("")]
    public void Names_without_a_port_at_the_end_give_nothing(string name) =>
        Assert.Null(AtkSerialProtocol.PortNameOf(name));

    [Fact]
    public void Null_friendly_name_gives_no_port()
    {
        Assert.Null(AtkSerialProtocol.PortNameOf(null));
    }

    [Fact]
    public void Requests_carry_length_command_and_sub_then_zeros()
    {
        byte[] status = AtkSerialProtocol.Request(0x00, 0x04);

        Assert.Equal(AtkSerialProtocol.FrameLength, status.Length);
        Assert.Equal((byte)2, status[0]);
        Assert.Equal((byte)0x00, status[1]);
        Assert.Equal((byte)0x04, status[2]);
        for (int i = 3; i < status.Length; i++) Assert.Equal((byte)0, status[i]);
    }

    [Fact]
    public void A_reply_is_found_behind_unsolicited_frames()
    {
        var received = new List<byte>();
        received.AddRange(Frame(0x05, 0x01, 0x09)); // pushed by the headset on its own
        received.AddRange(Frame(0x00, 0x04, 1));    // the status reply

        byte[]? reply = AtkSerialProtocol.TakeReply(received, 0x00, 0x04);

        Assert.NotNull(reply);
        Assert.True(AtkSerialProtocol.IsConnected(reply!));
        Assert.Empty(received);
    }

    [Fact]
    public void An_incomplete_reply_waits_for_the_rest()
    {
        byte[] reply = Frame(0x00, 0x03, (byte)(0x80 | 87));
        var received = new List<byte>();
        received.AddRange(new ArraySegment<byte>(reply, 0, 40));

        Assert.Null(AtkSerialProtocol.TakeReply(received, 0x00, 0x03));

        received.AddRange(new ArraySegment<byte>(reply, 40, reply.Length - 40));
        byte[]? complete = AtkSerialProtocol.TakeReply(received, 0x00, 0x03);

        Assert.NotNull(complete);
        Assert.Empty(received);
        (int percent, bool charging) = AtkSerialProtocol.ParseBattery(complete!);
        Assert.Equal(87, percent);
        Assert.True(charging);
    }

    [Theory]
    [InlineData(0x80 | 87, 87, true)]
    [InlineData(87, 87, false)]
    [InlineData(0x00, 0, false)]
    [InlineData(0x64, 100, false)]
    [InlineData(0xFF, 100, true)]
    public void Charging_is_bit_seven_and_the_percent_is_the_rest(int valueByte, int percent, bool charging)
    {
        (int actualPercent, bool actualCharging) = AtkSerialProtocol.ParseBattery(Frame(0x00, 0x03, (byte)valueByte));

        Assert.Equal(percent, actualPercent);
        Assert.Equal(charging, actualCharging);
    }

    [Fact]
    public void Status_value_one_means_connected()
    {
        Assert.True(AtkSerialProtocol.IsConnected(Frame(0x00, 0x04, 1)));
        Assert.False(AtkSerialProtocol.IsConnected(Frame(0x00, 0x04, 0)));
    }

    [Fact]
    public void Headset_products_are_recognised_from_the_instance_id()
    {
        Assert.Equal(AtkSerialProtocol.DongleProductId, AtkSerialProtocol.ProductIdOf(@"USB\VID_373B&PID_11CB&MI_02\7&1A2B3C4D&0&0002"));
        Assert.Equal(AtkSerialProtocol.CableProductId, AtkSerialProtocol.ProductIdOf(@"USB\VID_373B&PID_11CA\6&2B3C4D5E&0&1"));
        Assert.Null(AtkSerialProtocol.ProductIdOf(@"USB\VID_046D&PID_0AB6\5&1&0&0"));
    }

    /// <summary>A 64-byte frame with the given command, sub-command and value byte (byte 4).</summary>
    private static byte[] Frame(byte command, byte sub, byte valueByte)
    {
        var frame = new byte[AtkSerialProtocol.FrameLength];
        frame[0] = 2;
        frame[1] = command;
        frame[2] = sub;
        frame[4] = valueByte;
        return frame;
    }
}
