using BatteryStrip.Devices.Compx;
using Xunit;

namespace BatteryStrip.Tests;

public class CompxProtocolTests
{
    [Fact]
    public void Request_matches_the_one_the_vendor_software_sends()
    {
        Assert.Equal(Hex.Bytes("08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49"), CompxProtocol.BuildBatteryRequest());
    }

    [Theory]
    // Captured from the Lamzu receiver 25A7:FA7C on 10.11.2026.
    [InlineData("09 04 00 00 00 02 1E 00 00 00 00 00 00 00 00 00 28", 30, false)]
    [InlineData("09 04 00 00 00 02 19 00 00 00 00 00 00 00 00 00 2D", 25, false)]
    // Built by hand: 100 % and 95 % on the cable.
    [InlineData("09 04 00 00 00 02 64 00 00 00 00 00 00 00 00 00 E2", 100, false)]
    [InlineData("09 04 00 00 00 02 5F 01 00 00 00 00 00 00 00 00 E6", 95, true)]
    public void Reads_percent_and_charging(string frame, int percent, bool charging)
    {
        Assert.True(CompxProtocol.TryParseBatteryReply(Hex.Bytes(frame), out CompxBattery battery));
        Assert.False(battery.IsAsleep);
        Assert.Equal(percent, battery.Percent);
        Assert.Equal(charging, battery.IsCharging);
    }

    [Fact]
    public void Empty_payload_means_the_mouse_is_asleep()
    {
        Assert.True(CompxProtocol.TryParseBatteryReply(Hex.Bytes("09 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 48"), out CompxBattery battery));
        Assert.True(battery.IsAsleep);
    }

    [Theory]
    [InlineData("09 04 00 00 00 02 1E 00 00 00 00 00 00 00 00 00 29")] // wrong checksum
    [InlineData("09 08 00 00 00 02 1E 00 00 00 00 00 00 00 00 00 24")] // another command
    [InlineData("08 04 00 00 00 02 1E 00 00 00 00 00 00 00 00 00 29")] // a request, not a reply
    [InlineData("09 04 01 00 00 02 1E 00 00 00 00 00 00 00 00 00 27")] // error status
    [InlineData("09 04 00 00 00 02 C8 00 00 00 00 00 00 00 00 00 7E")] // 200 %
    [InlineData("09 04 00")]
    public void Rejects_everything_else(string frame)
    {
        Assert.False(CompxProtocol.TryParseBatteryReply(Hex.Bytes(frame), out _));
    }

    [Fact]
    public void Full_mouse_on_the_cable_stays_at_100()
    {
        var latch = new FullChargeLatch();
        Assert.Equal(new CompxBattery(false, 98, true), latch.Apply(new CompxBattery(false, 98, true)));
        Assert.Equal(new CompxBattery(false, 100, false), latch.Apply(new CompxBattery(false, 100, false)));
        Assert.Equal(new CompxBattery(false, 100, false), latch.Apply(new CompxBattery(false, 95, true)));
        Assert.Equal(new CompxBattery(false, 100, false), latch.Apply(new CompxBattery(false, 100, false)));

        // Unplugged and discharging: the real value is back.
        Assert.Equal(new CompxBattery(false, 97, false), latch.Apply(new CompxBattery(false, 97, false)));
        Assert.Equal(new CompxBattery(false, 96, true), latch.Apply(new CompxBattery(false, 96, true)));
    }
}
