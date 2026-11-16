using System;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.HyperX;
using Xunit;

namespace BatteryStrip.Tests;

public class HyperXProtocolTests
{
    private static byte[] Head(byte[] bytes, int count)
    {
        var head = new byte[count];
        Array.Copy(bytes, head, count);
        return head;
    }

    [Fact]
    public void Products_map_to_their_models()
    {
        Assert.Equal(HyperXFamily.Cloud2Kingston, HyperXProtocol.Find(0x0951, 0x1718)!.Family);
        Assert.Equal("HyperX Cloud Flight", HyperXProtocol.Find(0x0951, 0x1723)!.Name);
        Assert.Null(HyperXProtocol.Find(0x03F0, 0x1718));
        Assert.Null(HyperXProtocol.Find(0x0951, 0x0C9D));
    }

    [Fact]
    public void Cloud_3_collection_has_62_byte_reports_both_ways()
    {
        string path = @"\\?\HID#VID_03F0&PID_0C9D&MI_03#7&1A2B3C&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        var both = new HidCollection(path, 0x03F0, 0x0C9D, 0xFF00, 0x0001, 62, 62, 0, "", Guid.Empty);
        var outputOnly = new HidCollection(path, 0x03F0, 0x0C9D, 0xFF00, 0x0001, 0, 62, 0, "", Guid.Empty);
        Assert.True(HyperXProtocol.Accepts(HyperXRoute.Report62, both));
        Assert.False(HyperXProtocol.Accepts(HyperXRoute.Report62, outputOnly));
    }

    [Fact]
    public void Cloud_3_replies_are_matched_by_their_query()
    {
        Assert.Equal(Hex.Bytes("66 82"), HyperXProtocol.Cloud3Request(0x82));
        Assert.True(HyperXProtocol.Cloud3Connected(Hex.Bytes("66 82 01 00 00")) is true);
        Assert.True(HyperXProtocol.Cloud3Connected(Hex.Bytes("66 82 00 00 00")) is false);
        Assert.Null(HyperXProtocol.Cloud3Connected(Hex.Bytes("66 8A 01 00 00")));
        Assert.Null(HyperXProtocol.Cloud3Connected(Hex.Bytes("66 82 01")));

        Assert.True(HyperXProtocol.Cloud3Charging(Hex.Bytes("66 8A 01 00 00")) is true);
        Assert.True(HyperXProtocol.Cloud3Charging(Hex.Bytes("66 8A 00 00 00")) is false);
    }

    [Fact]
    public void Cloud_3_level_is_byte_4_and_a_level_above_100_is_refused()
    {
        Assert.Equal<int?>(75, HyperXProtocol.Cloud3Percent(Hex.Bytes("66 89 0E 6A 4B")));
        Assert.Null(HyperXProtocol.Cloud3Percent(Hex.Bytes("66 89 00 00 65")));
        Assert.Null(HyperXProtocol.Cloud3Percent(Hex.Bytes("66 89 00 00")));
    }

    [Fact]
    public void Cloud_2_wireless_HP_queries_and_replies()
    {
        Assert.Equal(Hex.Bytes("06 FF BB 02"), HyperXProtocol.Cloud2Request(0x02));
        Assert.Equal<int?>(90, HyperXProtocol.Cloud2Percent(Hex.Bytes("06 FF BB 02 00 0E 6A 5A")));
        Assert.Null(HyperXProtocol.Cloud2Percent(Hex.Bytes("06 FF BB 02 00 0E 6A 65")));
        Assert.Null(HyperXProtocol.Cloud2Percent(Hex.Bytes("06 FF BB 03 00 0E 6A 5A")));

        Assert.True(HyperXProtocol.Cloud2Charging(Hex.Bytes("06 FF BB 03 01 00 00 00")) is true);
        Assert.True(HyperXProtocol.Cloud2Charging(Hex.Bytes("06 FF BB 03 00 00 00 00")) is false);
    }

    [Fact]
    public void Cloud_2_Kingston_request_is_a_fixed_62_byte_frame()
    {
        byte[] frame = HyperXProtocol.Cloud2KingstonRequest(0x02);
        Assert.Equal(62, frame.Length);
        Assert.Equal(Hex.Bytes("06 00 02 00 9A 00 00 68 4A 8E 0A 00 00 00 BB 02 00"), Head(frame, 17));
        for (int i = 17; i < frame.Length; i++) Assert.Equal<byte>(0, frame[i]);
    }

    [Fact]
    public void Cloud_2_Kingston_replies()
    {
        Assert.Equal<int?>(75, HyperXProtocol.Cloud2KingstonPercent(Hex.Bytes("0B 00 BB 02 00 00 00 4B")));
        Assert.Null(HyperXProtocol.Cloud2KingstonPercent(Hex.Bytes("0B 00 BB 03 00 00 00 4B")));
        Assert.Null(HyperXProtocol.Cloud2KingstonPercent(Hex.Bytes("0C 00 BB 02 00 00 00 4B")));
        Assert.True(HyperXProtocol.Cloud2KingstonCharging(Hex.Bytes("0B 00 BB 03 01 00 00 00")) is true);
    }

    [Fact]
    public void Cloud_Alpha_status_charge_and_level_by_query()
    {
        Assert.Equal(Hex.Bytes("21 BB 0B"), HyperXProtocol.CloudAlphaRequest(0x0B));
        Assert.True(HyperXProtocol.CloudAlphaConnected(Hex.Bytes("21 00 03 00")) is true);
        Assert.True(HyperXProtocol.CloudAlphaConnected(Hex.Bytes("21 00 03 01")) is false);
        Assert.Null(HyperXProtocol.CloudAlphaConnected(Hex.Bytes("21 00 0C 00")));

        Assert.True(HyperXProtocol.CloudAlphaCharging(Hex.Bytes("21 00 0C 01")) is true);
        Assert.Equal<int?>(75, HyperXProtocol.CloudAlphaPercent(Hex.Bytes("21 00 0B 4B")));
        Assert.Null(HyperXProtocol.CloudAlphaPercent(Hex.Bytes("21 00 0B 65")));
    }

    [Fact]
    public void Cloud_Alpha_2_replies_are_matched_by_command_plus_one_and_sub_command()
    {
        Assert.Equal(Hex.Bytes("52 01"), HyperXProtocol.CloudAlpha2Request(0x52, 0x01));
        Assert.True(HyperXProtocol.CloudAlpha2Connected(Hex.Bytes("53 01 01 00 00 00 00 00")) is true);
        Assert.True(HyperXProtocol.CloudAlpha2Connected(Hex.Bytes("53 01 00 00 00 00 00 00")) is false);
        Assert.Null(HyperXProtocol.CloudAlpha2Connected(Hex.Bytes("53 01 01 00 00 00 00")));

        Assert.Equal<HyperXCharge?>(new HyperXCharge(75, true), HyperXProtocol.CloudAlpha2Level(Hex.Bytes("51 02 4B 01 00 00 00 00")));
        Assert.Null(HyperXProtocol.CloudAlpha2Level(Hex.Bytes("51 02 65 00 00 00 00 00")));
    }

    [Fact]
    public void Cloud_Flight_estimates_the_percent_from_the_voltage()
    {
        Assert.Equal(Hex.Bytes("21 FF 05"), HyperXProtocol.CloudFlightRequest());

        // 0x0ED8 = 3800 mV, 0x0F3C = 3900 mV, 0x0FA0 = 4000 mV, 0x100B = 4107 mV (the last one is still not charging).
        Assert.Equal<HyperXState?>(new HyperXState(true, 55, false, true), HyperXProtocol.CloudFlightState(Hex.Bytes("21 FF 05 0E D8")));
        Assert.Equal<HyperXState?>(new HyperXState(true, 82, false, true), HyperXProtocol.CloudFlightState(Hex.Bytes("21 FF 05 0F 3C")));
        Assert.Equal<HyperXState?>(new HyperXState(true, 100, false, true), HyperXProtocol.CloudFlightState(Hex.Bytes("21 FF 05 0F A0")));
        Assert.Equal<HyperXState?>(new HyperXState(true, 100, false, true), HyperXProtocol.CloudFlightState(Hex.Bytes("21 FF 05 10 0B")));

        // 0x0BB8 = 3000 mV, below 3648 mV the reference uses its linear branch: 0.00125 * mV = 3.75, truncated to 3.
        Assert.Equal<HyperXState?>(new HyperXState(true, 3, false, true), HyperXProtocol.CloudFlightState(Hex.Bytes("21 FF 05 0B B8")));

        Assert.Null(HyperXProtocol.CloudFlightState(Hex.Bytes("21 FF 05 0E")));
    }

    [Fact]
    public void Cloud_Flight_above_4107_mV_is_charging_and_has_no_percent()
    {
        // 0x100C = 4108 mV.
        Assert.Equal<HyperXState?>(new HyperXState(true, null, true, true), HyperXProtocol.CloudFlightState(Hex.Bytes("21 FF 05 10 0C")));
        Assert.Null(HyperXProtocol.CloudFlightPercent(4108));
    }
}
