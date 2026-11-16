using System.Linq;
using BatteryStrip.Devices.Glorious;
using Xunit;

namespace BatteryStrip.Tests;

public class GloriousProtocolTests
{
    [Fact]
    public void Request_is_a_65_byte_feature_report_with_three_marker_bytes()
    {
        byte[] request = GloriousProtocol.BuildRequest();

        Assert.Equal(65, request.Length);
        Assert.Equal((byte)0, request[0]);
        Assert.Equal(new byte[] { 0x02, 0x02, 0x83 }, new[] { request[3], request[4], request[6] });
        Assert.Equal(0x02 + 0x02 + 0x83, request.Sum(b => (int)b));  // nothing else is set
    }

    [Theory]
    // Awake and charging at 75 %
    [InlineData("00 A1 00 00 02 00 83 01 4B", GloriousState.Awake, 75, true)]
    // Awake on the battery at 100 %
    [InlineData("00 A1 00 00 02 00 83 00 64", GloriousState.Awake, 100, false)]
    // A reported 0 % is shown as 1 %
    [InlineData("00 A1 00 00 02 00 83 00 00", GloriousState.Awake, 1, false)]
    [InlineData("00 A4 00 00 02 00 83 00 00", GloriousState.Asleep, null, false)]
    [InlineData("00 A0 00 00 02 00 83 00 00", GloriousState.WakingUp, null, false)]
    public void Status_reply_gives_state_percent_and_charging(string frame, object state, int? percent, bool charging)
    {
        Assert.True(GloriousProtocol.TryParseReply(Hex.Bytes(frame), out GloriousStatus status));
        Assert.Equal((GloriousState)state, status.State);
        Assert.Equal(percent, status.Percent);
        Assert.Equal(charging, status.IsCharging);
    }

    [Fact]
    public void Awake_mouse_with_a_percent_above_100_has_no_percent()
    {
        Assert.True(GloriousProtocol.TryParseReply(Hex.Bytes("00 A1 00 00 02 00 83 00 65"), out GloriousStatus status));
        Assert.Equal(GloriousState.Awake, status.State);
        Assert.Null(status.Percent);
    }

    [Theory]
    [InlineData("00 A1 00 00 02 00 84 01 4B")] // the 0x83 marker is missing
    [InlineData("00 B1 00 00 02 00 83 01 4B")] // the status is outside the A family
    [InlineData("00 A1 00 00 02 00 83")]       // too short
    public void Rejects_replies_that_are_not_this_protocol(string frame)
    {
        Assert.False(GloriousProtocol.TryParseReply(Hex.Bytes(frame), out _));
    }

    [Theory]
    [InlineData(0x258A, 0x2011, "Glorious Model O Wireless", true)]  // cable of the Model O
    [InlineData(0x258A, 0x2022, "Glorious Model O Wireless", false)] // receiver of the Model O
    [InlineData(0x258A, 0x201A, "Glorious Model D 2 PRO", true)]     // cable of the Model D 2 PRO
    [InlineData(0x3770, 0x0136, "Hitscan Hyperlight X", true)]
    public void Product_table_names_the_model_and_marks_the_cable(int vendorId, int productId, string model, bool cable)
    {
        GloriousProduct product = GloriousProtocol.Lookup((ushort)vendorId, (ushort)productId);

        Assert.Equal(model, product.Model);
        Assert.Equal(cable, product.IsCable);
    }

    [Fact]
    public void Unknown_product_has_no_model_and_is_not_a_cable()
    {
        GloriousProduct product = GloriousProtocol.Lookup(0x258A, 0x2099);

        Assert.Null(product.Model);
        Assert.False(product.IsCable);
    }

    [Theory]
    [InlineData(0x258A, 0x2022, true)]
    [InlineData(0x3770, 0x0136, true)]
    [InlineData(0x3770, 0x0200, false)]  // the Hyperlight receiver belongs to the Kysona driver
    public void Glorious_and_hitscan_products_except_the_hyperlight_receiver_are_taken(int vendorId, int productId, bool expected)
    {
        Assert.Equal(expected, GloriousProtocol.Handles((ushort)vendorId, (ushort)productId));
    }
}
