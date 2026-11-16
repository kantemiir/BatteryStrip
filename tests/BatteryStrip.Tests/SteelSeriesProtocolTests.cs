using System;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;
using BatteryStrip.Devices.SteelSeries;
using Xunit;

namespace BatteryStrip.Tests;

public class SteelSeriesProtocolTests
{
    private static HidCollection Collection(string path, ushort usagePage, ushort usage, int outputLength) =>
        new(path, 0x1038, 0x1838, usagePage, usage, 64, outputLength, 0, "", Guid.Empty);

    [Fact]
    public void Arctis_1_reads_level_and_the_off_flag()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 75, false), SteelSeriesProtocol.ParseArctis1(Hex.Bytes("06 12 00 4B")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseArctis1(Hex.Bytes("06 12 01 00")));
        Assert.Null(SteelSeriesProtocol.ParseArctis1(Hex.Bytes("06 12 00")));
    }

    [Fact]
    public void Arctis_7_zero_means_off_and_the_level_is_clamped()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseArctis7(Hex.Bytes("06 18 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 100, false), SteelSeriesProtocol.ParseArctis7(Hex.Bytes("06 18 64")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 100, false), SteelSeriesProtocol.ParseArctis7(Hex.Bytes("06 18 80")));
        Assert.Null(SteelSeriesProtocol.ParseArctis7(Hex.Bytes("06 18")));
    }

    [Fact]
    public void Arctis_9_scales_its_raw_range_0x64_to_0x9A()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 0, false), SteelSeriesProtocol.ParseArctis9(Hex.Bytes("00 20 00 64 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 100, true), SteelSeriesProtocol.ParseArctis9(Hex.Bytes("00 20 00 9A 01")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 46, false), SteelSeriesProtocol.ParseArctis9(Hex.Bytes("00 20 00 7D 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 0, false), SteelSeriesProtocol.ParseArctis9(Hex.Bytes("00 20 00 50 00")));
    }

    [Fact]
    public void Arctis_Pro_Wireless_status_and_level_in_steps_of_four()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseArctisProWireless(Hex.Bytes("02")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, null, false), SteelSeriesProtocol.ParseArctisProWireless(Hex.Bytes("00")));
        Assert.Null(SteelSeriesProtocol.ParseArctisProWireless(Array.Empty<byte>()));

        Assert.Equal<int?>(100, SteelSeriesProtocol.ParseArctisProWirelessLevel(Hex.Bytes("04")));
        Assert.Equal<int?>(50, SteelSeriesProtocol.ParseArctisProWirelessLevel(Hex.Bytes("02")));
        Assert.Equal<int?>(25, SteelSeriesProtocol.ParseArctisProWirelessLevel(Hex.Bytes("01")));
        Assert.Equal<int?>(0, SteelSeriesProtocol.ParseArctisProWirelessLevel(Hex.Bytes("00")));
    }

    [Fact]
    public void Nova_Pro_Wireless_level_at_byte_6_and_status_at_byte_15()
    {
        var on = new byte[16];
        on[6] = 4;
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 50, false), SteelSeriesProtocol.ParseNovaProWireless(on));

        var charging = new byte[16];
        charging[6] = 8;
        charging[15] = 0x02;
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 100, true), SteelSeriesProtocol.ParseNovaProWireless(charging));

        var off = new byte[16];
        off[6] = 4;
        off[15] = 0x01;
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseNovaProWireless(off));

        Assert.Null(SteelSeriesProtocol.ParseNovaProWireless(new byte[15]));
    }

    [Fact]
    public void Arctis_7_plus_layout_of_status_block_00_B0()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 75, true), SteelSeriesProtocol.ParseArctis7Plus(Hex.Bytes("00 00 03 01 00 00 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseArctis7Plus(Hex.Bytes("00 01 00 00 00 00 00")));
        Assert.Null(SteelSeriesProtocol.ParseArctis7Plus(Hex.Bytes("00 00 03 01 00 00")));
    }

    [Fact]
    public void Nova_3P_and_Nova_5_layout_of_status_block_00_B0()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 90, true), SteelSeriesProtocol.ParseNova3P(Hex.Bytes("00 00 00 5A 01 00 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseNova3P(Hex.Bytes("00 02 00 00 00 00 00")));
    }

    [Fact]
    public void GameBuds_show_the_lower_of_the_buds_out_of_the_case()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 40, false), SteelSeriesProtocol.ParseGameBuds(Hex.Bytes("00 00 00 03 03 50 28")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 80, false), SteelSeriesProtocol.ParseGameBuds(Hex.Bytes("00 00 00 03 00 50 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 60, false), SteelSeriesProtocol.ParseGameBuds(Hex.Bytes("00 00 00 00 03 00 3C")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseGameBuds(Hex.Bytes("00 00 00 00 00 00 00")));
    }

    [Fact]
    public void Nova_7_with_level_in_steps_and_charging_from_byte_3()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 100, false), SteelSeriesProtocol.ParseNova7Steps(Hex.Bytes("00 00 04 03 00 00 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 50, true), SteelSeriesProtocol.ParseNova7Steps(Hex.Bytes("00 00 02 01 00 00 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseNova7Steps(Hex.Bytes("00 00 04 00 00 00 00")));
        Assert.Null(SteelSeriesProtocol.ParseNova7Steps(Hex.Bytes("00 00 04 03 00 00")));
    }

    [Fact]
    public void Nova_7_with_level_already_in_percent()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 64, false), SteelSeriesProtocol.ParseNova7Percent(Hex.Bytes("00 00 40 03 00 00 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 64, true), SteelSeriesProtocol.ParseNova7Percent(Hex.Bytes("00 00 40 02 00 00 00")));
    }

    [Fact]
    public void Aerox_level_is_raw_minus_one_times_five_and_bit_7_is_charging()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 50, false), SteelSeriesProtocol.ParseAerox(Hex.Bytes("00 0B")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 50, true), SteelSeriesProtocol.ParseAerox(Hex.Bytes("00 8B")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 100, false), SteelSeriesProtocol.ParseAerox(Hex.Bytes("00 15")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 0, false), SteelSeriesProtocol.ParseAerox(Hex.Bytes("00 01")));
        Assert.Null(SteelSeriesProtocol.ParseAerox(Hex.Bytes("00")));
    }

    [Fact]
    public void Aerox_raw_zero_means_the_mouse_is_off_even_with_the_charging_bit()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseAerox(Hex.Bytes("00 00")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(false, null, false), SteelSeriesProtocol.ParseAerox(Hex.Bytes("00 80")));
    }

    [Fact]
    public void Rival_reads_level_in_byte_0_and_charging_in_byte_2()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 75, true), SteelSeriesProtocol.ParseRival(Hex.Bytes("4B 00 01")));
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 100, false), SteelSeriesProtocol.ParseRival(Hex.Bytes("64 00 00")));
        Assert.Null(SteelSeriesProtocol.ParseRival(Hex.Bytes("4B 00")));
    }

    [Fact]
    public void Rival_Pro_needs_a_reply_that_starts_92_00_and_a_level_up_to_100()
    {
        Assert.Equal<SteelSeriesState?>(new SteelSeriesState(true, 90, false), SteelSeriesProtocol.ParseRivalPro(Hex.Bytes("92 00 5A 00 64")));
        Assert.Null(SteelSeriesProtocol.ParseRivalPro(Hex.Bytes("92 00 65 00 00")));
        Assert.Null(SteelSeriesProtocol.ParseRivalPro(Hex.Bytes("92 01 50 00 00")));
        Assert.Null(SteelSeriesProtocol.ParseRivalPro(Hex.Bytes("92 00")));
    }

    [Fact]
    public void Cable_id_of_a_mouse_shares_the_key_of_its_receiver_and_asks_with_the_wired_request()
    {
        SteelSeriesModel cable = SteelSeriesProtocol.Find(0x183A)!;
        Assert.True(cable.Wired);
        Assert.Equal((ushort)0x1838, cable.KeyProductId);
        Assert.Equal(DeviceKind.Mouse, cable.Kind);
        Assert.Equal(Hex.Bytes("00 92"), SteelSeriesProtocol.BuildRequest(cable));

        SteelSeriesModel receiver = SteelSeriesProtocol.Find(0x1838)!;
        Assert.False(receiver.Wired);
        Assert.Equal((ushort)0x1838, receiver.KeyProductId);
        Assert.Equal(Hex.Bytes("00 D2"), SteelSeriesProtocol.BuildRequest(receiver));

        Assert.Null(SteelSeriesProtocol.Find(0x0001));
    }

    [Fact]
    public void Requests_of_the_other_families_are_the_reference_ones()
    {
        Assert.Equal(Hex.Bytes("06 18"), SteelSeriesProtocol.BuildRequest(SteelSeriesProtocol.Find(0x1260)!));
        Assert.Equal(Hex.Bytes("41 AA"), SteelSeriesProtocol.BuildRequest(SteelSeriesProtocol.Find(0x1290)!));
        Assert.Equal(Hex.Bytes("40 AA"), SteelSeriesProtocol.BuildArctisProWirelessLevelRequest());
        Assert.Equal(Hex.Bytes("00 B0"), SteelSeriesProtocol.BuildRequest(SteelSeriesProtocol.Find(0x230A)!));
        Assert.Equal(Hex.Bytes("00 AA 01"), SteelSeriesProtocol.BuildRequest(SteelSeriesProtocol.Find(0x1830)!));
        Assert.Equal(Hex.Bytes("00 92"), SteelSeriesProtocol.BuildRequest(SteelSeriesProtocol.Find(0x1880)!));
    }

    [Fact]
    public void Interface_routes_need_output_reports()
    {
        string receiver = @"\\?\HID#VID_1038&PID_1838&MI_03#7&1A2B3C&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        Assert.True(SteelSeriesProtocol.Accepts(SteelSeriesRoute.Interface3, Collection(receiver, 0xFF00, 0x0001, 64)));
        Assert.False(SteelSeriesProtocol.Accepts(SteelSeriesRoute.Interface3, Collection(receiver, 0xFF00, 0x0001, 0)));
        Assert.False(SteelSeriesProtocol.Accepts(SteelSeriesRoute.Interface0, Collection(receiver, 0xFF00, 0x0001, 64)));
    }

    [Fact]
    public void Nova_family_collection_is_vendor_page_ffc0()
    {
        string headset = @"\\?\HID#VID_1038&PID_220E#7&1A2B3C&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        Assert.True(SteelSeriesProtocol.Accepts(SteelSeriesRoute.PageFFC0, Collection(headset, 0xFFC0, 0x0001, 64)));
        Assert.False(SteelSeriesProtocol.Accepts(SteelSeriesRoute.PageFF43, Collection(headset, 0xFFC0, 0x0001, 64)));
    }
}
