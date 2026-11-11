using System.Linq;
using BatteryStrip.Rendering;
using Xunit;

namespace BatteryStrip.Tests;

/// <summary>Skips a test where the Windows 11 fonts are missing, as on Windows Server build machines.</summary>
public sealed class WindowsFontsFactAttribute : FactAttribute
{
    public WindowsFontsFactAttribute()
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        bool present = fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") && fonts.Families.Any(f => f.Name == "Segoe UI Variable Text");
        if (!present) Skip = "Segoe Fluent Icons and Segoe UI Variable are not installed here.";
    }
}

/// <summary>Renders real glyphs through DirectWrite, so these tests need Windows with its standard fonts.</summary>
public class GlyphCacheTests
{
    private static int FullPixels(GlyphMask mask) =>
        Enumerable.Range(0, mask.Width * mask.Height).Count(i => mask.At(i % mask.Width, i / mask.Width) >= 230);

    [WindowsFontsFact]
    public void Icon_strokes_land_on_whole_pixels()
    {
        using var glyphs = new GlyphCache();
        GlyphMask mouse = glyphs.Get(GlyphFont.Icons, 16, StripPainter.IconOf(Core.DeviceKind.Mouse), lightText: true);

        // The 16 px icon is about 11 × 16; a crisp 1 px outline has most of its pixels fully covered.
        Assert.InRange(mouse.Width, 9, 14);
        Assert.InRange(mouse.Height, 14, 17);
        Assert.True(FullPixels(mouse) >= 30, $"only {FullPixels(mouse)} fully covered pixels: the icon is blurred");
    }

    [WindowsFontsFact]
    public void Numbers_are_cropped_to_their_ink()
    {
        using var glyphs = new GlyphCache();
        GlyphMask number = glyphs.Get(GlyphFont.NumberSemibold, 12, "30", lightText: true);
        Assert.InRange(number.Height, 8, 10);
        Assert.InRange(number.Width, 11, 16);
    }

    [WindowsFontsFact]
    public void Text_lines_share_one_height_so_baselines_match()
    {
        using var glyphs = new GlyphCache();
        GlyphMask plain = glyphs.GetLine(GlyphFont.Body, 13, "Мышь", 200, lightText: true);
        GlyphMask descender = glyphs.GetLine(GlyphFont.Body, 13, "Logitech G435", 200, lightText: true);
        Assert.Equal(plain.Height, descender.Height);
    }

    [WindowsFontsFact]
    public void Long_lines_are_cut_to_the_given_width()
    {
        using var glyphs = new GlyphCache();
        GlyphMask line = glyphs.GetLine(GlyphFont.Body, 13, new string('Ж', 60), 120, lightText: true);
        Assert.True(line.Width <= 120);
    }
}
