using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BatteryStrip.Core;
using BatteryStrip.Rendering;

namespace BatteryStrip.App;

/// <summary>
/// The --preview mode: paints every strip state on dark and light taskbar colours into a PNG, to compare
/// with the design mock without touching the real taskbar.
/// </summary>
internal static class Preview
{
    public static int Run(string path)
    {
        DateTime now = DateTime.UtcNow;
        DeviceSnapshot Device(string id, DeviceKind kind, int? percent, bool charging = false, bool asleep = false) =>
            new(id, id, kind, percent, charging, asleep, false, percent is int p ? ChargeLevels.Of(p) : ChargeLevel.Normal, now);

        var rows = new List<IReadOnlyList<DeviceSnapshot>>
        {
            new[] { Device("m", DeviceKind.Mouse, 82), Device("h", DeviceKind.Headset, 47), Device("g", DeviceKind.Gamepad, 63) },
            new[] { Device("m", DeviceKind.Mouse, 18), Device("h", DeviceKind.Headset, 7), Device("g", DeviceKind.Gamepad, 100) },
            new[] { Device("m", DeviceKind.Mouse, 34, charging: true), Device("h", DeviceKind.Headset, 82, asleep: true), Device("g", DeviceKind.Gamepad, null) },
        };
        float[] scales = [1f, 1.5f, 2.5f];

        using var glyphs = new GlyphCache();
        var painter = new StripPainter(glyphs);
        const int pad = 16;

        var images = new List<(Palette Palette, Rgba Taskbar, StripImage Image)>();
        int totalWidth = 0, totalHeight = pad;
        foreach (var (palette, taskbar) in new[] { (Palette.Dark, Rgba.FromHex(0x1C2530)), (Palette.Light, Rgba.FromHex(0xEEF1F4)) })
        {
            foreach (float scale in scales)
            {
                foreach (IReadOnlyList<DeviceSnapshot> row in rows)
                {
                    StripImage image = painter.Paint(row, palette, scale, (int)Math.Round(48 * scale));
                    images.Add((palette, taskbar, image));
                    totalWidth = Math.Max(totalWidth, image.Surface.Width + 2 * pad);
                    totalHeight += image.Surface.Height + pad;
                }
            }
        }

        using var bitmap = new Bitmap(totalWidth, totalHeight, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap)) g.Clear(Color.FromArgb(0x80, 0x80, 0x80));

        int y = pad;
        foreach (var (_, taskbar, image) in images)
        {
            Surface surface = image.Surface;
            for (int sy = 0; sy < surface.Height; sy++)
            {
                for (int sx = -pad / 2; sx < surface.Width + pad / 2; sx++)
                {
                    uint pixel = sx >= 0 && sx < surface.Width ? surface.Pixels[sy * surface.Width + sx] : 0;
                    bitmap.SetPixel(pad + sx, y + sy, Over(pixel, taskbar));
                }
            }
            y += surface.Height + pad;
        }
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine(path);
        return 0;
    }

    /// <summary>Premultiplied pixel over an opaque background.</summary>
    private static Color Over(uint pixel, Rgba background)
    {
        float a = (pixel >> 24) / 255f;
        int Channel(int shift, byte back) => (int)Math.Round(((pixel >> shift) & 0xFF) + back * (1 - a));
        return Color.FromArgb(Channel(16, background.R), Channel(8, background.G), Channel(0, background.B));
    }
}
