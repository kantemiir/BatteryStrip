using BatteryStrip.Devices.Razer;
using Xunit;

namespace BatteryStrip.Tests;

public class RazerProtocolTests
{
    /// <summary>A feature report as the device returns it: report id 0, status, transaction id, class, id, argument 1.</summary>
    private static byte[] Reply(byte status, byte commandClass, byte commandId, byte argument1 = 0)
    {
        var feature = new byte[91];
        feature[1] = status;
        feature[2] = 0x1F;
        feature[7] = commandClass;
        feature[8] = commandId;
        feature[10] = argument1;
        return feature;
    }

    [Fact]
    public void Battery_request_is_a_91_byte_feature_report_with_report_id_zero()
    {
        byte[] request = RazerProtocol.BuildRequest(0x1F, 0x07, 0x80);

        var expected = new byte[91];
        expected[2] = 0x1F;   // transaction id
        expected[6] = 0x02;   // data size
        expected[7] = 0x07;   // command class
        expected[8] = 0x80;   // command id
        expected[89] = 0x85;  // checksum: 02 ^ 07 ^ 80
        Assert.Equal(expected, request);
    }

    [Theory]
    [InlineData(0x1F, 0x80, 0x85)]
    [InlineData(0xFF, 0x80, 0x85)] // the transaction id is not part of the checksum
    [InlineData(0x3F, 0x84, 0x81)] // charging request: 02 ^ 07 ^ 84
    public void Checksum_is_the_xor_of_bytes_2_to_87(int transactionId, int commandId, int checksum)
    {
        byte[] request = RazerProtocol.BuildRequest((byte)transactionId, 0x07, (byte)commandId);
        Assert.Equal((byte)checksum, request[89]);
    }

    [Theory]
    [InlineData(0x02, RazerStatus.Ok)]
    [InlineData(0x01, RazerStatus.Busy)]
    [InlineData(0x03, RazerStatus.Failed)]
    [InlineData(0x04, RazerStatus.Offline)]
    [InlineData(0x05, RazerStatus.NotSupported)]
    public void Status_byte_is_read_from_the_reply(int status, object expected)
    {
        RazerReply? reply = RazerProtocol.ParseReply(Reply((byte)status, 0x07, 0x80), 0x07, 0x80);
        Assert.Equal((RazerStatus)expected, reply!.Status);
    }

    [Theory]
    [InlineData(255, 100)]
    [InlineData(254, 100)]
    [InlineData(128, 50)]
    [InlineData(127, 50)]
    [InlineData(0, 0)]
    public void Battery_byte_is_scaled_to_percent(int level, int percent)
    {
        RazerReply? reply = RazerProtocol.ParseReply(Reply(0x02, 0x07, 0x80, (byte)level), 0x07, 0x80);
        Assert.Equal(percent, RazerProtocol.BatteryPercent(reply!));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Charging_is_argument_one_equal_to_one(int state, bool charging)
    {
        RazerReply? reply = RazerProtocol.ParseReply(Reply(0x02, 0x07, 0x84, (byte)state), 0x07, 0x84);
        Assert.Equal(charging, RazerProtocol.IsCharging(reply!));
    }

    [Fact]
    public void Reply_to_another_command_is_ignored()
    {
        Assert.Null(RazerProtocol.ParseReply(Reply(0x02, 0x07, 0x84, 1), 0x07, 0x80));
    }

    [Fact]
    public void Unknown_status_and_short_buffers_are_not_replies()
    {
        Assert.Null(RazerProtocol.ParseReply(Reply(0x00, 0x07, 0x80), 0x07, 0x80));
        Assert.Null(RazerProtocol.ParseReply(Reply(0x09, 0x07, 0x80), 0x07, 0x80));
        Assert.Null(RazerProtocol.ParseReply(new byte[50], 0x07, 0x80));
    }

    [Theory]
    [InlineData("Razer Viper V3 Pro (Wired)", "Razer Viper V3 Pro")]
    [InlineData("Razer Viper V3 Pro", "Razer Viper V3 Pro")]
    [InlineData("Razer Viper HyperSpeed Dongle", "Razer Viper")]
    [InlineData("Razer Viper Dongle", "Razer Viper")]
    [InlineData("Razer Viper Wireless Receiver", "Razer Viper")]
    [InlineData("", "")]
    public void Receiver_and_cable_words_are_left_out_of_the_name(string product, string name)
    {
        Assert.Equal(name, RazerProtocol.CleanName(product));
    }
}
