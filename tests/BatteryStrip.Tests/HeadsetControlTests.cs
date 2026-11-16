using System.Collections.Generic;
using BatteryStrip.Devices.HeadsetControl;
using Xunit;

namespace BatteryStrip.Tests;

public class HeadsetControlTests
{
    /// <summary>What "headsetcontrol -o json" prints for two headsets: one charging, one with its battery unavailable.</summary>
    private const string RealisticOutput = """
        {
          "devices": [
            {
              "id_vendor": "0x1038",
              "id_product": "0x12ad",
              "device": "SteelSeries Arctis Nova 7",
              "capabilities": ["CAP_SIDETONE", "CAP_BATTERY_STATUS"],
              "battery": {
                "status": "BATTERY_CHARGING",
                "level": 64
              }
            },
            {
              "id_vendor": "0x046d",
              "id_product": "0x0aba",
              "device": "Logitech G435",
              "battery": {
                "status": "BATTERY_UNAVAILABLE",
                "level": -1
              }
            }
          ]
        }
        """;

    [Fact]
    public void Reads_both_headsets_with_their_batteries()
    {
        IReadOnlyList<HeadsetControlDevice> devices = HeadsetControlJson.Parse(RealisticOutput);

        Assert.Collection(devices,
            first =>
            {
                Assert.Equal((ushort)0x1038, first.VendorId);
                Assert.Equal((ushort)0x12AD, first.ProductId);
                Assert.Equal("SteelSeries Arctis Nova 7", first.Name);
                Assert.Equal("BATTERY_CHARGING", first.Status);
                Assert.Equal(64, first.Level);
            },
            second =>
            {
                Assert.Equal((ushort)0x046D, second.VendorId);
                Assert.Equal((ushort)0x0ABA, second.ProductId);
                Assert.Equal("Logitech G435", second.Name);
                Assert.Equal("BATTERY_UNAVAILABLE", second.Status);
                Assert.Equal(-1, second.Level);
            });
    }

    [Fact]
    public void Text_before_the_first_brace_is_skipped()
    {
        string output = "HeadsetControl 3.0.0\nFound 2 devices.\n" + RealisticOutput;

        Assert.Collection(HeadsetControlJson.Parse(output),
            first => Assert.Equal("SteelSeries Arctis Nova 7", first.Name),
            second => Assert.Equal("Logitech G435", second.Name));
    }

    [Fact]
    public void String_escapes_are_decoded()
    {
        const string output = """
            {"devices": [{"id_vendor": "0x1038", "id_product": "0x12ad",
              "device": "Arctis \"Nova\" 7 Дж \\ \/ tab\t🎧",
              "battery": {"status": "BATTERY_AVAILABLE", "level": 80}}]}
            """;

        HeadsetControlDevice device = Assert.Single(HeadsetControlJson.Parse(output));

        Assert.Equal("Arctis \"Nova\" 7 Дж \\ / tab\t🎧", device.Name);
        Assert.Equal("BATTERY_AVAILABLE", device.Status);
        Assert.Equal(80, device.Level);
    }

    [Fact]
    public void Device_without_a_battery_object_has_no_status_and_no_level()
    {
        const string output = """{"devices": [{"id_vendor": "0x1038", "id_product": "0x12ad", "device": "Arctis"}]}""";

        HeadsetControlDevice device = Assert.Single(HeadsetControlJson.Parse(output));

        Assert.Equal("Arctis", device.Name);
        Assert.Equal("", device.Status);
        Assert.Equal(-1, device.Level);
    }

    [Fact]
    public void Other_fields_and_number_forms_are_read_past()
    {
        const string output = """
            {"version": 3, "ok": true, "none": null, "ratio": -1.5e2,
             "list": [false, [], {}, "x"],
             "devices": [{"id_vendor": 4152, "id_product": "0x12ad", "device": "A",
               "battery": {"status": "BATTERY_AVAILABLE", "level": 12.4}}]}
            """;

        HeadsetControlDevice device = Assert.Single(HeadsetControlJson.Parse(output));

        Assert.Equal((ushort)0x1038, device.VendorId);
        Assert.Equal((ushort)0x12AD, device.ProductId);
        Assert.Equal(12, device.Level);
    }

    [Fact]
    public void Empty_device_list_is_a_valid_answer()
    {
        Assert.True(HeadsetControlJson.TryParse("{\"devices\": []}", out IReadOnlyList<HeadsetControlDevice> devices));
        Assert.Empty(devices);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{\"devices\": [")]
    [InlineData("{\"devices\": [{\"id_vendor\": \"0x1038\",}]}")]
    [InlineData("{\"devices\": 5}")]
    [InlineData("{\"other\": []}")]
    public void Unreadable_output_gives_no_devices(string output)
    {
        Assert.False(HeadsetControlJson.TryParse(output, out IReadOnlyList<HeadsetControlDevice> devices));
        Assert.Empty(devices);
        Assert.Empty(HeadsetControlJson.Parse(output));
    }

    [Fact]
    public void Nesting_past_the_limit_is_refused()
    {
        string deep = new string('[', 40) + new string(']', 40);

        Assert.False(HeadsetControlJson.TryParse("{\"devices\": [], \"deep\": " + deep + "}", out _));
    }
}
