using BatteryStrip.Devices.Corsair;
using Xunit;

namespace BatteryStrip.Tests;

public class CorsairProtocolTests
{
    [Fact]
    public void Void_battery_request_is_C9_then_64()
    {
        Assert.Equal(Hex.Bytes("C9 64"), CorsairProtocol.BuildVoidBatteryRequest());
    }

    [Fact]
    public void Bragi_get_is_02_target_02_property_or_with_a_leading_00_for_unnumbered_reports()
    {
        Assert.Equal(Hex.Bytes("02 09 02 0F"), CorsairProtocol.BuildBragiGet(0x09, 0x0F, numbered: true));
        Assert.Equal(Hex.Bytes("00 02 08 02 10"), CorsairProtocol.BuildBragiGet(0x08, 0x10, numbered: false));
    }

    [Theory]
    [InlineData("00 00 5A 00 01", 90, VoidState.Connected)]
    [InlineData("00 00 5A 00 02", 90, VoidState.Connected)]
    [InlineData("00 00 3C 00 04", 60, VoidState.Charging)]
    [InlineData("00 00 3C 00 05", 60, VoidState.Charging)]
    [InlineData("00 00 00 00 00", 0, VoidState.Offline)]
    [InlineData("00 00 5A 00 03", 90, VoidState.Unknown)]
    public void Void_reply_gives_the_level_and_the_state(string payload, int percent, object state)
    {
        VoidBattery? battery = CorsairProtocol.ParseVoidReply(Hex.Bytes(payload));
        Assert.NotNull(battery);
        Assert.Equal(percent, battery!.Value.Percent);
        Assert.Equal((VoidState)state, battery.Value.State);
    }

    [Fact]
    public void Microphone_flag_in_bit_7_of_the_void_level_is_masked_off()
    {
        // 0xDA = microphone up (bit 7) and level 0x5A = 90.
        VoidBattery? battery = CorsairProtocol.ParseVoidReply(Hex.Bytes("00 00 DA 00 02"));
        Assert.Equal(90, battery!.Value.Percent);
        Assert.Equal(VoidState.Connected, battery.Value.State);
    }

    [Fact]
    public void Void_reply_shorter_than_five_bytes_is_not_a_reply()
    {
        Assert.Null(CorsairProtocol.ParseVoidReply(Hex.Bytes("00 00 5A 00")));
    }

    [Fact]
    public void Bragi_value_is_a_little_endian_32_bit_number()
    {
        BragiReply? reply = CorsairProtocol.ParseBragiReply(Hex.Bytes("02 01 02 00 E8 03 00 00"), 0x09);
        Assert.Equal(new BragiReply(0, 1000), reply!.Value);

        BragiReply? second = CorsairProtocol.ParseBragiReply(Hex.Bytes("02 01 02 00 01 02 00 00"), 0x09);
        Assert.Equal(new BragiReply(0, 513), second!.Value);
    }

    [Theory]
    [InlineData(1000, 100)]
    [InlineData(995, 100)]
    [InlineData(994, 99)]
    [InlineData(500, 50)]
    [InlineData(0, 0)]
    public void Bragi_battery_is_tenths_of_a_percent_rounded(int value, int percent)
    {
        Assert.Equal(percent, CorsairProtocol.BatteryPercent((uint)value));
    }

    [Fact]
    public void Reply_from_the_wrong_source_is_ignored()
    {
        // Devices behind a receiver answer from source 01; the device itself answers from source 00.
        Assert.Null(CorsairProtocol.ParseBragiReply(Hex.Bytes("02 00 02 00 E8 03 00 00"), 0x09));
        Assert.Null(CorsairProtocol.ParseBragiReply(Hex.Bytes("02 01 02 00 E8 03 00 00"), 0x08));
        Assert.NotNull(CorsairProtocol.ParseBragiReply(Hex.Bytes("02 00 02 00 E8 03 00 00"), 0x08));
    }

    [Fact]
    public void Reply_without_the_02_marker_or_shorter_than_eight_bytes_is_ignored()
    {
        Assert.Null(CorsairProtocol.ParseBragiReply(Hex.Bytes("02 01 03 00 E8 03 00 00"), 0x09));
        Assert.Null(CorsairProtocol.ParseBragiReply(Hex.Bytes("02 01 02 00 E8 03 00"), 0x09));
    }

    [Fact]
    public void Non_zero_status_is_an_answer_without_a_value()
    {
        BragiReply? reply = CorsairProtocol.ParseBragiReply(Hex.Bytes("02 01 02 05 00 00 00 00"), 0x09);
        Assert.Equal((byte)5, reply!.Value.Status);
    }
}
