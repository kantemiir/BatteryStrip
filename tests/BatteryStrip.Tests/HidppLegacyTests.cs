using BatteryStrip.Devices.Logitech;
using Xunit;

namespace BatteryStrip.Tests;

public class HidppLegacyTests
{
    [Fact]
    public void Register_read_is_a_short_report()
    {
        Assert.Equal("10-02-81-0D-00-00-00", System.BitConverter.ToString(Hidpp.RegisterRead(2, Hidpp.RegisterBatteryCharge)));
    }

    [Fact]
    public void Register_reply_and_error_are_matched()
    {
        HidppReply? reply = Hidpp.MatchRegisterReply(Hex.Bytes("10 02 81 0D 4B 00 30"), 2, 0x0D);
        Assert.NotNull(reply);
        Assert.False(reply!.Value.IsError);
        Assert.Equal(new byte[] { 0x4B, 0x00, 0x30 }, reply.Value.Parameters);

        HidppReply? error = Hidpp.MatchRegisterReply(Hex.Bytes("10 02 8F 81 0D 02 00"), 2, 0x0D);
        Assert.Equal((byte?)0x02, error!.Value.Error);

        Assert.Null(Hidpp.MatchRegisterReply(Hex.Bytes("10 03 81 0D 4B 00 30"), 2, 0x0D)); // another device
        Assert.Null(Hidpp.MatchRegisterReply(Hex.Bytes("10 02 81 07 4B 00 30"), 2, 0x0D)); // another register
    }

    [Theory]
    [InlineData(75, 0x30, 75, false)] // discharging
    [InlineData(40, 0x50, 40, true)] // recharging
    [InlineData(99, 0x90, 100, false)] // full
    public void Charge_register_gives_a_percent(byte percent, byte status, int expected, bool charging)
    {
        Assert.Equal(new HidppBattery(expected, charging, false), Hidpp.ParseRegisterBattery(0x0D, new byte[] { percent, 0, status }));
    }

    [Theory]
    [InlineData(7, 0x00, 90, false)]
    [InlineData(3, 0x21, 20, true)]
    [InlineData(1, 0x00, 5, false)]
    public void Status_register_gives_a_coarse_level(byte level, byte charging, int expected, bool isCharging)
    {
        Assert.Equal(new HidppBattery(expected, isCharging, true), Hidpp.ParseRegisterBattery(0x07, new byte[] { level, charging, 0 }));
    }

    [Fact]
    public void Status_register_full_and_unknown()
    {
        Assert.Equal(new HidppBattery(100, false, false), Hidpp.ParseRegisterBattery(0x07, new byte[] { 7, 0x22, 0 }));
        Assert.Null(Hidpp.ParseRegisterBattery(0x07, new byte[] { 0, 0, 0 }));
        Assert.Null(Hidpp.ParseRegisterBattery(0x0D, new byte[] { 0, 0, 0x30 }));
    }

    [Fact]
    public void Headsets_use_their_own_curve()
    {
        Assert.Equal(50, HeadsetCurves.Percent(0x0AC4, 3817)); // G535 calibration point
        Assert.Equal(100, HeadsetCurves.Percent(0x0A66, 4250));
        Assert.Equal(0, HeadsetCurves.Percent(0x0A5C, 3000));
        Assert.Equal(40, HeadsetCurves.Percent(0x0A5C, 3750)); // G633 family
        // Unknown products keep the generic Li-Po curve.
        Assert.Equal(Devices.LiPoBattery.Percent(3800), HeadsetCurves.Percent(0x1234, 3800));
    }

    [Fact]
    public void Adc_feature_uses_the_headset_curve_for_known_products()
    {
        byte[] parameters = { 0x0E, 0xE9, 0x01 }; // 3817 mV, on, not charging
        Assert.Equal(new HidppBattery(50, false, true), Hidpp.ParseBattery(Hidpp.FeatureAdcMeasurement, parameters, productId: 0x0AC4));
    }
}
