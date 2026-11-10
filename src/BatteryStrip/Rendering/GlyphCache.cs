using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BatteryStrip.Rendering;

/// <summary>Font roles used on the strip and in the flyout.</summary>
internal enum GlyphFont
{
    /// <summary>Segoe Fluent Icons, the icon font of Windows 11.</summary>
    Icons,

    /// <summary>Segoe UI Variable, semibold, small optical size: the number inside a battery.</summary>
    NumberSemibold,
}

/// <summary>
/// Rasterises text and icon glyphs into coverage masks cropped to their ink, and caches them.
/// Ink cropping lets digits and icons be centred optically rather than by font metrics.
/// Not thread-safe: use one instance per thread.
/// </summary>
internal sealed class GlyphCache : IDisposable
{
    private readonly Dictionary<(GlyphFont, float, string), GlyphMask> _cache = new();
    private readonly Dictionary<(GlyphFont, float), Font> _fonts = new();

    public GlyphMask Get(GlyphFont font, float pixelSize, string text)
    {
        var key = (font, pixelSize, text);
        if (!_cache.TryGetValue(key, out GlyphMask? mask))
        {
            mask = Rasterize(FontFor(font, pixelSize), text);
            _cache[key] = mask;
        }
        return mask;
    }

    /// <summary>Forgets everything, for example after a display scale change.</summary>
    public void Clear()
    {
        _cache.Clear();
        foreach (Font font in _fonts.Values) font.Dispose();
        _fonts.Clear();
    }

    public void Dispose() => Clear();

    private Font FontFor(GlyphFont role, float pixelSize)
    {
        if (_fonts.TryGetValue((role, pixelSize), out Font? font)) return font;

        string[] families = role == GlyphFont.Icons
            ? ["Segoe Fluent Icons", "Segoe MDL2 Assets"]
            // GDI lists the named instances of the variable font with names cut to 31 characters.
            : ["Segoe UI Variable Small Semibol", "Segoe UI Variable Text Semibold", "Segoe UI Semibold"];

        foreach (string family in families)
        {
            try
            {
                font = new Font(family, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
                if (font.Name == family) break;
                font.Dispose();
                font = null;
            }
            catch (ArgumentException)
            {
                font = null;
            }
        }
        font ??= new Font(SystemFonts.MessageBoxFont.FontFamily, pixelSize, FontStyle.Bold, GraphicsUnit.Pixel);
        _fonts[(role, pixelSize)] = font;
        return font;
    }

    private static GlyphMask Rasterize(Font font, string text)
    {
        int size = (int)Math.Ceiling(font.Size * 2.5f) + 8;
        int width = size * Math.Max(1, text.Length);
        using var bitmap = new Bitmap(width, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoClip };
            g.DrawString(text, font, Brushes.White, 4, 4, format);
        }

        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new int[width * size];
        try
        {
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        int left = width, top = size, right = -1, bottom = -1;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if ((pixels[y * width + x] >>> 24) == 0) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < 0) return GlyphMask.Empty;

        int maskWidth = right - left + 1, maskHeight = bottom - top + 1;
        var coverage = new byte[maskWidth * maskHeight];
        for (int y = 0; y < maskHeight; y++)
        {
            for (int x = 0; x < maskWidth; x++)
                coverage[y * maskWidth + x] = (byte)(pixels[(top + y) * width + left + x] >>> 24);
        }
        return new GlyphMask(maskWidth, maskHeight, coverage);
    }
}
