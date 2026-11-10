using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using BatteryStrip.Rendering;

namespace BatteryStrip.App;

/// <summary>
/// The program's icon: a battery three-quarters full, drawn with the same code as the strip.
/// Used for the exe and shortcut (Assets/app.ico, made with --make-icon) and for notifications.
/// </summary>
internal static class AppIcon
{
    private static readonly Rgba Body = Rgba.FromHex(0x2B2B2B);
    private static readonly Rgba Outline = Rgba.FromHex(0xF2F2F2);
    private static readonly Rgba Fill = Rgba.FromHex(0x97C459);

    public static Surface Paint(int size)
    {
        var surface = new Surface(size, size);
        float unit = size / 32f;
        var body = new RectF(1.5f * unit, 8 * unit, 26 * unit, 16 * unit);
        float line = Math.Max(1, 2 * unit);
        surface.FillRoundedRect(body, 5 * unit, Body);
        surface.StrokeRoundedRect(body, 5 * unit, line, Outline);
        surface.FillRoundedRect(new RectF(body.Right + unit, 13 * unit, 2.5f * unit, 6 * unit), 1.2f * unit, Outline);
        RectF track = body.Inset(line + 1.5f * unit);
        surface.FillRoundedRect(new RectF(track.X, track.Y, track.Width * 0.72f, track.Height), 2.5f * unit, Fill);
        return surface;
    }

    /// <summary>Writes a PNG of the icon, for example for the notification registration.</summary>
    public static void SavePng(string path, int size)
    {
        using Bitmap bitmap = ToBitmap(Paint(size));
        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>Writes a multi-size .ico (PNG-compressed entries) for the exe and shortcuts.</summary>
    public static void SaveIco(string path)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var images = new List<byte[]>();
        foreach (int size in sizes)
        {
            using Bitmap bitmap = ToBitmap(Paint(size));
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            images.Add(png.ToArray());
        }

        using var file = new BinaryWriter(File.Create(path));
        file.Write((short)0);
        file.Write((short)1);
        file.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            file.Write((byte)0);
            file.Write((byte)0);
            file.Write((short)1);
            file.Write((short)32);
            file.Write(images[i].Length);
            file.Write(offset);
            offset += images[i].Length;
        }
        foreach (byte[] image in images) file.Write(image);
    }

    private static Bitmap ToBitmap(Surface surface)
    {
        var bitmap = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                uint p = surface.Pixels[y * surface.Width + x];
                int a = (int)(p >> 24);
                int Unpremultiply(int shift) => a == 0 ? 0 : Math.Min(255, (int)(((p >> shift) & 0xFF) * 255 / a));
                bitmap.SetPixel(x, y, Color.FromArgb(a, Unpremultiply(16), Unpremultiply(8), Unpremultiply(0)));
            }
        }
        return bitmap;
    }
}
