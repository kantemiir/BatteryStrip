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

    /// <summary>Segoe UI Variable Text, regular: names and labels in the flyout.</summary>
    Body,
}

/// <summary>
/// Rasterises text and icon glyphs into coverage masks cropped to their ink, and caches them.
/// Ink cropping lets digits and icons be centred optically rather than by font metrics.
/// Not thread-safe: use one instance per thread.
/// </summary>
internal sealed class GlyphCache : IDisposable
{
    private readonly Dictionary<(GlyphFont, float, string), GlyphMask> _cache = new();
    private readonly Dictionary<(GlyphFont, float, int, string), GlyphMask> _lines = new();
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

    /// <summary>
    /// A line of text in a box one line high, so different strings share the same baseline.
    /// Text wider than <paramref name="maxWidth"/> is cut with an ellipsis.
    /// </summary>
    public GlyphMask GetLine(GlyphFont font, float pixelSize, string text, int maxWidth)
    {
        var key = (font, pixelSize, maxWidth, text);
        if (!_lines.TryGetValue(key, out GlyphMask? mask))
        {
            mask = RasterizeLine(FontFor(font, pixelSize), text, maxWidth);
            _lines[key] = mask;
        }
        return mask;
    }

    /// <summary>Forgets everything, for example after a display scale change.</summary>
    public void Clear()
    {
        _cache.Clear();
        _lines.Clear();
        foreach (Font font in _fonts.Values) font.Dispose();
        _fonts.Clear();
    }

    public void Dispose() => Clear();

    private Font FontFor(GlyphFont role, float pixelSize)
    {
        if (_fonts.TryGetValue((role, pixelSize), out Font? font)) return font;

        string[] families = role switch
        {
            GlyphFont.Icons => ["Segoe Fluent Icons", "Segoe MDL2 Assets"],
            GlyphFont.Body => ["Segoe UI Variable Text", "Segoe UI"],
            // GDI lists the named instances of the variable font with names cut to 31 characters.
            _ => ["Segoe UI Variable Small Semibol", "Segoe UI Variable Text Semibold", "Segoe UI Semibold"],
        };

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

    private static GlyphMask RasterizeLine(Font font, string text, int maxWidth)
    {
        int height = (int)Math.Ceiling(font.GetHeight());
        int width = Math.Max(1, maxWidth);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, width, height), format);
        }

        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new int[width * height];
        try
        {
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        // Keep the full line height (shared baseline) but trim empty columns on the right.
        int used = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if ((pixels[i] >>> 24) != 0) used = Math.Max(used, i % width + 1);
        }
        if (used == 0) return GlyphMask.Empty;

        var coverage = new byte[used * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < used; x++) coverage[y * used + x] = (byte)(pixels[y * width + x] >>> 24);
        }
        return new GlyphMask(used, height, coverage);
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
