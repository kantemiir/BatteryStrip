using BatteryStrip.Devices.Jbl;
using Xunit;

namespace BatteryStrip.Tests;

public class JblProtocolTests
{
    [Fact]
    public void Request_is_the_one_the_JBL_app_sends_for_the_battery()
    {
        Assert.Equal("AA 13 02 00 44", Hex.Text(JblProtocol.BatteryRequest()));
    }

    [Fact]
    public void Advertisement_of_the_users_Charge_6_points_at_its_classic_address()
    {
        // Heard 2026-11-17 from "Kani", paired as 78:66:F3:0A:BD:D8.
        var parsed = JblProtocol.ParseAdvertisement(Hex.Bytes("E3 20 01 2C 0B C1 60 3C 09 00"));

        Assert.Equal(((ushort)0x20E3, (ushort)0x3C60), parsed);
        Assert.Equal(0x3C60, JblProtocol.AddressCrc(0x7866F30ABDD8));
    }

    [Fact]
    public void Short_advertisement_is_not_parsed()
    {
        Assert.Null(JblProtocol.ParseAdvertisement(Hex.Bytes("E3 20 01 2C 0B C1 60")));
    }

    [Theory]
    [InlineData("AA 12 03 00 44 9D", 29, true)] // captured on the charger
    [InlineData("AA 12 03 00 44 1D", 29, false)]
    [InlineData("AA 12 03 00 44 64", 100, false)] // Charge 5 capture from jbl-charge5
    [InlineData("AA 12 03 00 44 E4", 100, true)]
    [InlineData("AA 12 03 00 44 00", 0, false)]
    public void Battery_answer(string frame, int percent, bool charging)
    {
        Assert.Equal((percent, charging), JblProtocol.ParseBattery(Hex.Bytes(frame)));
    }

    [Theory]
    [InlineData("AA 12 03 00 46 00")] // another token (audio channel)
    [InlineData("AA 12 03 00 44 65")] // 101 % is not a charge
    [InlineData("AA 12 03 00 44")] // cut short
    [InlineData("AA 42 04 00 08 06 00")] // firmware version
    [InlineData("AA EE 01 13 00")]
    public void Anything_else_is_not_a_battery_answer(string frame)
    {
        Assert.Null(JblProtocol.ParseBattery(Hex.Bytes(frame)));
    }

    [Fact]
    public void Rejection_is_recognised()
    {
        Assert.True(JblProtocol.IsRejection(Hex.Bytes("AA EE 01 13 00")));
        Assert.False(JblProtocol.IsRejection(Hex.Bytes("AA 12 03 00 44 9D")));
    }
}
