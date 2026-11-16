using System.Linq;
using BatteryStrip.Devices.Kysona;
using Xunit;

namespace BatteryStrip.Tests;

public class KysonaProtocolTests
{
    [Theory]
    [InlineData(0x03, "08 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 4A")]
    [InlineData(0x04, "08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49")]
    public void Request_is_17_bytes_with_the_checksum(int command, string expected)
    {
        Assert.Equal(Hex.Bytes(expected), KysonaProtocol.BuildRequest((byte)command));
    }

    [Fact]
    public void Online_status_with_byte6_set_means_connected()
    {
        Assert.True(KysonaProtocol.TryParseStatus(Hex.Bytes("08 03 00 00 00 00 01 00 00 00 00 00 00 00 00 00 49"), out KysonaStatus status));
        Assert.True(status.Connected);
    }

    [Fact]
    public void Online_status_with_byte6_zero_means_the_mouse_is_offline()
    {
        Assert.True(KysonaProtocol.TryParseStatus(Hex.Bytes("08 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 4A"), out KysonaStatus status));
        Assert.False(status.Connected);
    }

    [Fact]
    public void Status_reply_with_a_bad_checksum_is_rejected()
    {
        Assert.False(KysonaProtocol.TryParseStatus(Hex.Bytes("08 03 00 00 00 00 01 00 00 00 00 00 00 00 00 00 48"), out _));
    }

    [Theory]
    // 75 %, not charging, 3750 mV
    [InlineData("08 04 00 00 00 00 4B 00 0E A6 00 00 00 00 00 00 4A", 75, false, 3750)]
    // 100 %, charging, 4096 mV
    [InlineData("08 04 00 00 00 00 64 01 10 00 00 00 00 00 00 00 D4", 100, true, 4096)]
    // 0 % is a valid reading
    [InlineData("08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49", 0, false, 0)]
    public void Battery_reply_gives_percent_charging_and_millivolts(string frame, int percent, bool charging, int millivolts)
    {
        Assert.True(KysonaProtocol.TryParseBattery(Hex.Bytes(frame), out KysonaBattery battery));
        Assert.Equal(percent, battery.Percent);
        Assert.Equal(charging, battery.IsCharging);
        Assert.Equal(millivolts, battery.Millivolts);
    }

    [Theory]
    [InlineData("08 04 00 00 00 00 4B 00 0E A6 00 00 00 00 00 00 4B")] // wrong checksum
    [InlineData("08 03 00 00 00 00 4B 00 0E A6 00 00 00 00 00 00 4B")] // the status command, not the battery
    [InlineData("09 04 00 00 00 00 4B 00 0E A6 00 00 00 00 00 00 49")] // another report id
    [InlineData("08 04 00 00 00 00 65 00 00 00 00 00 00 00 00 00 E4")] // 101 %
    [InlineData("08 04 00 00 00 00 4B")]                                // too short
    public void Battery_rejects_damaged_or_foreign_replies(string frame)
    {
        Assert.False(KysonaProtocol.TryParseBattery(Hex.Bytes(frame), out _));
    }

    [Fact]
    public void Longer_vendor_report_is_read_from_its_first_17_bytes()
    {
        byte[] report = Hex.Bytes("08 04 00 00 00 00 4B 00 0E A6 00 00 00 00 00 00 4A").Concat(new byte[47]).ToArray();

        Assert.True(KysonaProtocol.TryParseBattery(report, out KysonaBattery battery));
        Assert.Equal(75, battery.Percent);
    }

    [Theory]
    [InlineData(0x3554, 0x1234, true)]   // Kysona and Pulsar
    [InlineData(0x373B, 0x1234, true)]   // ATK and VXE dongles
    [InlineData(0x373B, 0x11CB, false)]  // ATK headset, handled elsewhere
    [InlineData(0x373B, 0x11CA, false)]
    [InlineData(0x3770, 0x0200, true)]   // Hitscan Hyperlight receiver
    [InlineData(0x3770, 0x0136, false)]  // other Hitscan products speak the Glorious protocol
    public void Only_the_products_of_this_protocol_are_taken(int vendorId, int productId, bool expected)
    {
        Assert.Equal(expected, KysonaProtocol.Handles((ushort)vendorId, (ushort)productId));
    }
}
