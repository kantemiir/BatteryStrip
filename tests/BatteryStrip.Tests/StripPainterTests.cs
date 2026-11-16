using System;
using BatteryStrip.Core;
using BatteryStrip.Rendering;
using Xunit;

namespace BatteryStrip.Tests;

/// <summary>The number on the strip has to stay readable: one solid colour, never split by the edge of the charge.</summary>
public class StripPainterTests
{
    private const float Left = 4, Middle = 20;

    private static DeviceSnapshot Device(int? percent, bool charging = false, bool asleep = false) =>
        new("d", "d", DeviceKind.Mouse, percent, charging, asleep, false,
            percent is int p ? ChargeLevels.Of(p) : ChargeLevel.Normal, DateTime.UtcNow);

    private static Surface Paint(StripPainter painter, DeviceSnapshot device, Palette palette, float scale)
    {
        var surface = new Surface((int)Math.Ceiling(60 * scale), (int)Math.Ceiling(40 * scale));
        painter.PaintBattery(surface, device, palette, scale, Left * scale, Middle * scale);
        return surface;
    }

    /// <summary>The track the charge fills: the battery body less the outline and the hairline gap, as painted.</summary>
    private static (int Left, int Top, int Right, int Bottom) Track(float scale)
    {
        float inset = (float)Math.Round(2 * scale);
        float x = (float)Math.Round(Left * scale), y = (float)Math.Round(Middle * scale - 18 * scale / 2);
        float w = (float)Math.Round(34 * scale), h = (float)Math.Round(18 * scale);
        return ((int)(x + inset), (int)(y + inset), (int)(x + w - inset), (int)(y + h - inset));
    }

    [Fact]
    public void The_battery_width_is_what_the_strip_measures()
    {
        using var glyphs = new GlyphCache();
        var painter = new StripPainter(glyphs);

        // Body 34, gap 1 and nub 2.
        Assert.Equal(37, painter.PlainBatteryWidth(1));

        // Hover room at both ends (4 + 4), the icon with its gap (16 + 5) and the battery.
        Assert.Equal(66, painter.Measure([Device(45)], 1));
    }

    [WindowsFontsFact]
    public void A_charging_bolt_adds_room_beside_the_battery()
    {
        using var glyphs = new GlyphCache();
        var painter = new StripPainter(glyphs);
        Assert.True(painter.Measure([Device(45, charging: true)], 1) > painter.Measure([Device(45)], 1));
    }

    [WindowsFontsFact]
    public void The_number_is_never_cut_out_of_the_fill()
    {
        // An earlier battery cut the number out of its fill, so a digit crossing the edge of the fill split in two
        // colours. Now the number is painted over the fill: nothing inside the filled part may be see-through.
        using var glyphs = new GlyphCache();
        var painter = new StripPainter(glyphs);
        foreach (Palette palette in new[] { Palette.Dark, Palette.Light })
        {
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                var (left, top, right, bottom) = Track(scale);
                foreach (int percent in new[] { 25, 45, 50, 56, 82, 100 })
                {
                    Surface surface = Paint(painter, Device(percent), palette, scale);
                    int filledTo = left + (int)Math.Round((right - left) * percent / 100f);
                    int margin = (int)Math.Ceiling(2 * scale);
                    for (int y = top + margin; y < bottom - margin; y++)
                    {
                        for (int x = left + margin; x < filledTo - margin; x++)
                        {
                            Assert.True(surface.Pixels[y * surface.Width + x] >> 24 != 0,
                                $"hole at {x},{y} for {percent} % at scale {scale}, light={palette.IsLight}");
                        }
                    }
                }
            }
        }
    }

    [WindowsFontsFact]
    public void A_warning_fill_stops_short_of_the_number()
    {
        // The warning colours are painted fully saturated, which is only safe because they end before the digits.
        using var glyphs = new GlyphCache();
        var painter = new StripPainter(glyphs);
        foreach (Palette palette in new[] { Palette.Dark, Palette.Light })
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                for (int percent = 1; percent <= ChargeLevels.LowThreshold; percent++)
                {
                    DeviceSnapshot device = Device(percent);
                    Rgba warning = device.Level == ChargeLevel.Critical ? palette.Critical : palette.Low;
                    uint warningPixel = 0xFF000000u | (uint)(warning.R << 16) | (uint)(warning.G << 8) | warning.B;
                    uint textPixel = 0xFF000000u | (uint)(palette.Text.R << 16) | (uint)(palette.Text.G << 8) | palette.Text.B;

                    Surface surface = Paint(painter, device, palette, scale);
                    int lastWarning = -1, firstDigit = int.MaxValue;
                    for (int y = 0; y < surface.Height; y++)
                    {
                        for (int x = 0; x < surface.Width; x++)
                        {
                            uint pixel = surface.Pixels[y * surface.Width + x];
                            if (pixel == warningPixel) lastWarning = Math.Max(lastWarning, x);
                            if (pixel == textPixel) firstDigit = Math.Min(firstDigit, x);
                        }
                    }
                    Assert.True(lastWarning >= 0, $"no warning fill at {percent} %");
                    Assert.True(lastWarning < firstDigit,
                        $"the warning fill reaches the number at {percent} %, scale {scale}: fill to {lastWarning}, digits from {firstDigit}");
                }
            }
        }
    }
}
