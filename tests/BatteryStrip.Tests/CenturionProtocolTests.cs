using System;
using System.Linq;
using BatteryStrip.Devices.Logitech;
using Xunit;

namespace BatteryStrip.Tests;

public class CenturionProtocolTests
{
    private static byte[] Frame64(string hex) => Hex.Bytes(hex).Concat(new byte[64]).Take(64).ToArray();

    [Fact]
    public void Battery_request_to_a_PRO_X_2_has_the_layout_of_the_Linux_driver()
    {
        // The Linux driver sends 51 08 00 03 1A 00 03 00 04 0A (software id 0x0A); ours differs only in the software id.
        byte[] frame = Centurion.Frame(Centurion.PlainReport, 0, Centurion.BridgeRequest(3, 4, 0));
        Assert.Equal(Frame64("51 08 00 03 1C 00 03 00 04 0C"), frame);
    }

    [Fact]
    public void Addressed_frames_carry_the_address_after_the_report_id()
    {
        // Root.GetProtocolVersion to address 0x23, as the address search sends it.
        Assert.Equal(Frame64("50 23 06 00 00 1C 00 00 00"), Centurion.Frame(Centurion.AddressedReport, 0x23, Centurion.Request(0, 1, 0, 0, 0)));
        // Root.GetFeature(0x0104) on the headset behind the bridge at index 3.
        Assert.Equal(Frame64("50 23 0A 00 03 1C 00 05 00 00 0C 01 04"),
            Centurion.Frame(Centurion.AddressedReport, 0x23, Centurion.BridgeRequest(3, 0, 0, 0x01, 0x04)));
    }

    [Fact]
    public void Battery_answer_from_a_PRO_X_2_reads_50_percent()
    {
        // The frame the Linux driver's author read from a PRO X 2: 50 %, discharging.
        CenturionFrame frame = Centurion.Parse(Frame64("51 0B 00 03 10 00 06 00 04 0A 32 32 00"))!;
        var message = Assert.IsType<CenturionBridgeEvent.Message>(Centurion.ParseBridgeEvent(frame.Payload, 3));
        Assert.Equal(4, message.SubIndex);
        Assert.Equal(Hex.Bytes("32 32 00"), message.Data);
        Assert.Equal(new CenturionBattery(50, false), Centurion.ParseBattery(message.Data));
    }

    [Fact]
    public void Addressed_answer_gives_the_address_and_the_payload()
    {
        CenturionFrame frame = Centurion.Parse(Frame64("50 23 04 00 00 0C 03"))!;
        Assert.Equal((byte)0x23, frame.Address);
        Assert.Equal((byte)0, frame.Flags);
        Assert.Equal(Hex.Bytes("00 0C 03"), frame.Payload);
        Assert.Equal(Hex.Bytes("03"), Centurion.MatchReply(frame.Payload, 0, 0)!.Value.Parameters);
    }

    [Theory]
    [InlineData("51 05 00 FF 00 0C 02", 2)] // the dongle's error frame: FF, feature index, tag, code
    public void Dongle_errors_are_matched_to_the_request(string hex, int code)
    {
        CenturionFrame frame = Centurion.Parse(Frame64(hex))!;
        HidppReply reply = Centurion.MatchReply(frame.Payload, 0, 0)!.Value;
        Assert.True(reply.IsError);
        Assert.Equal((byte)code, reply.Error);
    }

    [Fact]
    public void Answers_to_other_programs_are_not_taken_for_ours()
    {
        // G HUB uses software id 0x0D.
        CenturionFrame dongle = Centurion.Parse(Frame64("51 04 00 00 0D 03"))!;
        Assert.Null(Centurion.MatchReply(dongle.Payload, 0, 0));

        CenturionFrame bridged = Centurion.Parse(Frame64("51 0B 00 03 10 00 06 00 04 0D 32 32 00"))!;
        var message = Assert.IsType<CenturionBridgeEvent.Message>(Centurion.ParseBridgeEvent(bridged.Payload, 3));
        Assert.Null(Centurion.MatchBridgeReply(message, 4, 0));
        Assert.False(Centurion.IsNotification(message.Tag));
    }

    [Fact]
    public void Bridge_answer_to_our_request_is_matched()
    {
        CenturionFrame frame = Centurion.Parse(Frame64("51 09 00 03 10 00 04 00 00 0C 04"))!;
        var message = Assert.IsType<CenturionBridgeEvent.Message>(Centurion.ParseBridgeEvent(frame.Payload, 3));
        HidppReply reply = Centurion.MatchBridgeReply(message, 0, 0)!.Value;
        Assert.False(reply.IsError);
        Assert.Equal(Hex.Bytes("04"), reply.Parameters);
    }

    [Fact]
    public void Headset_error_through_the_bridge_is_matched_to_the_request()
    {
        CenturionFrame frame = Centurion.Parse(Frame64("51 0A 00 03 10 00 05 00 FF 04 0C 05"))!;
        var message = Assert.IsType<CenturionBridgeEvent.Message>(Centurion.ParseBridgeEvent(frame.Payload, 3));
        HidppReply reply = Centurion.MatchBridgeReply(message, 4, 0)!.Value;
        Assert.True(reply.IsError);
        Assert.Equal((byte)5, reply.Error);
    }

    [Fact]
    public void The_bridge_acknowledgement_is_not_an_answer()
    {
        CenturionFrame ack = Centurion.Parse(Frame64("51 03 00 03 1C"))!;
        Assert.Null(Centurion.ParseBridgeEvent(ack.Payload, 3));
    }

    [Theory]
    [InlineData("51 05 00 03 00 00 00", false)] // empty device list: the headset went off (the Linux driver's power frame)
    [InlineData("51 06 00 03 00 00 01 01", true)]
    public void Connection_event_says_whether_the_headset_is_on(string hex, bool linked)
    {
        CenturionFrame frame = Centurion.Parse(Frame64(hex))!;
        Assert.Equal(new CenturionBridgeEvent.Connection(linked), Centurion.ParseBridgeEvent(frame.Payload, 3));
    }

    [Fact]
    public void Events_of_another_feature_index_are_not_bridge_events()
    {
        CenturionFrame frame = Centurion.Parse(Frame64("51 05 00 02 00 00 00"))!;
        Assert.Null(Centurion.ParseBridgeEvent(frame.Payload, 3));
    }

    [Theory]
    [InlineData("32 32 01", 50, true)]
    [InlineData("4B 4B 02", 75, true)]
    [InlineData("64 64 00", 100, false)]
    [InlineData("5F 5F 03", 100, false)] // charge complete
    [InlineData("14", 20, false)]        // a short answer without the state byte
    public void Battery_data_gives_percent_and_charging(string data, int percent, bool charging)
    {
        Assert.Equal(new CenturionBattery(percent, charging), Centurion.ParseBattery(Hex.Bytes(data)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("00 00 00")] // 0 % would fake a low-battery alarm
    [InlineData("65 65 00")] // 101 %
    [InlineData("32 32 04")] // unknown state
    public void Unusable_battery_data_is_rejected(string data)
    {
        Assert.Null(Centurion.ParseBattery(Hex.Bytes(data)));
    }

    [Theory]
    [InlineData("10 FF 00 00")] // a HID++ short report
    [InlineData("51 00 00 00")] // length 0
    [InlineData("51 05")]       // too short
    public void Other_reports_are_not_Centurion_frames(string hex)
    {
        Assert.Null(Centurion.Parse(Hex.Bytes(hex)));
    }

    [Theory]
    [InlineData(new byte[] { 0x51 }, 0x0B18, 0x51)]
    [InlineData(new byte[] { 0x50 }, 0x0AF7, 0x50)]
    [InlineData(new byte[] { 0x50, 0x51 }, 0x0B18, 0x51)]
    [InlineData(new byte[0], 0x0B18, 0x50)]
    [InlineData(new byte[0], 0x0AF7, 0x51)]
    public void Report_id_comes_from_the_descriptor_then_the_product(byte[] declared, int productId, int expected)
    {
        Assert.Equal((byte)expected, Centurion.ChooseReport(declared, (ushort)productId));
    }

    [Theory]
    [InlineData("G522 LIGHTSPEED - Wireless Mode", "Logitech G522 LIGHTSPEED")]
    [InlineData("PRO X 2 LIGHTSPEED", "Logitech PRO X 2 LIGHTSPEED")]
    [InlineData("Logitech G522", "Logitech G522")]
    [InlineData("  ", null)]
    public void Name_comes_from_the_product_string(string product, string? expected)
    {
        Assert.Equal(expected, Centurion.NameFromProduct(product));
    }

    [Fact]
    public void Only_the_64_byte_vendor_collection_of_Logitech_is_taken()
    {
        static Devices.Hid.HidCollection Collection(ushort vendor, ushort page, int input, int output) =>
            new("path", vendor, 0x0B18, page, 0x01, input, output, 0, "G522 LIGHTSPEED - Wireless Mode", Guid.Empty);

        Assert.True(Centurion.IsCollection(Collection(0x046D, 0xFFA0, 64, 64)));
        Assert.False(Centurion.IsCollection(Collection(0x046D, 0xFF13, 62, 62)));
        Assert.False(Centurion.IsCollection(Collection(0x2DC8, 0xFFA0, 64, 64))); // 8BitDo's receiver uses the same page
    }
}
