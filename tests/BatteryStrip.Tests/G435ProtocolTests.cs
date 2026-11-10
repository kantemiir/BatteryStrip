using System.Linq;
using BatteryStrip.Devices.G435;
using Xunit;

namespace BatteryStrip.Tests;

public class G435ProtocolTests
{
    private static string Head(byte[] frame, int count) => Hex.Text(frame.Take(count).ToArray());

    [Fact]
    public void Frames_match_the_ones_verified_on_the_receiver()
    {
        Assert.Equal("06 01 03", Head(G435Protocol.EnterServiceMode(), 3));
        Assert.Equal("06 08 03", Head(G435Protocol.ExitServiceMode(), 3));
        Assert.Equal("06 04 03 06 50 41 0E 01 01 E3", Head(G435Protocol.QueryConnectStatus(1), 10));
        Assert.Equal("06 04 03 06 50 41 0E 02 01 E0", Head(G435Protocol.QueryRemoteMode(2), 10));
        Assert.Equal("06 04 03 07 50 41 0E 03 02 E1 01", Head(G435Protocol.ChangeRemoteMode(3, 1), 11));
        Assert.Equal("06 04 03 07 50 41 06 04 01 00 31", Head(G435Protocol.QueryBatteryVoltage(4), 11));
        Assert.Equal("06 04 03 07 50 41 0E 05 02 E1 00", Head(G435Protocol.ChangeRemoteMode(5, 0), 11));
    }

    [Fact]
    public void Every_frame_is_a_full_zero_padded_feature_report()
    {
        byte[] frame = G435Protocol.QueryBatteryVoltage(1);
        Assert.Equal(64, frame.Length);
        Assert.All(frame.Skip(11), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Sequence_wraps_after_127()
    {
        Assert.Equal(2, G435Protocol.NextSequence(1));
        Assert.Equal(1, G435Protocol.NextSequence(127));
    }

    [Fact]
    public void Reads_the_replies_captured_from_the_users_receiver()
    {
        // 10.11.2026, receiver 046D:0ACB.
        Assert.Equal(1, G435Protocol.ParseConnectStatus(Hex.Bytes("06 50 49 0E C4 00 00 00 00 02 00 E3 01 FF FF FF")));
        Assert.Equal(0, G435Protocol.ParseRemoteMode(Hex.Bytes("06 50 49 0E C5 00 00 00 00 02 00 E0 00 FF FF FF")));
        Assert.Equal(4029, G435Protocol.ParseBatteryMillivolts(Hex.Bytes("06 50 49 01 C6 EA 85 31 00 07 00 06 84 00 BD 0F 00 00 FF FF")));
    }

    [Fact]
    public void An_acknowledgement_frame_carries_no_value()
    {
        byte[] ack = Hex.Bytes("06 50 49 01 C0 73 A3 33 00 03 00 0E 81 00 FF FF FF FF");
        Assert.Null(G435Protocol.ParseConnectStatus(ack));
        Assert.Null(G435Protocol.ParseRemoteMode(ack));
        Assert.Null(G435Protocol.ParseBatteryMillivolts(ack));
    }

    [Fact]
    public void Rejects_the_garbage_voltage_the_receiver_returns_in_the_wrong_mode()
    {
        // From deddroid/logitech-g435-battery: remote mode 0, value 0x00D2.
        Assert.Null(G435Protocol.ParseBatteryMillivolts(Hex.Bytes("06 50 49 01 C0 D6 43 00 00 07 00 06 82 00 D2 00 00 00 FF FF")));
    }

    [Theory]
    [InlineData(4300, 100)]
    [InlineData(4200, 100)]
    [InlineData(4146, 95)]
    [InlineData(4029, 83)]
    [InlineData(3725, 40)]
    [InlineData(3400, 0)]
    [InlineData(3300, 0)]
    public void Voltage_to_percent(int millivolts, int percent)
    {
        Assert.Equal(percent, G435Protocol.MillivoltsToPercent(millivolts));
    }

    [Fact]
    public void Shown_percent_ignores_small_upward_jitter()
    {
        var smoother = new EstimateSmoother();
        Assert.Equal(83, smoother.Apply(83));
        Assert.Equal(82, smoother.Apply(82));
        Assert.Equal(82, smoother.Apply(83));
        Assert.Equal(82, smoother.Apply(84));
        Assert.Equal(85, smoother.Apply(85));
        Assert.Equal(60, smoother.Apply(60));
    }
}
