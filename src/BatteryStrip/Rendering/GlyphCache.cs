using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;

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
/// Rasterises text and icon glyphs with DirectWrite into coverage masks and caches them. Single glyphs and
/// numbers are cropped to their ink, so they can be centred optically; text lines keep their full height,
/// so different lines share a baseline. Not thread-safe: use one instance per thread.
/// </summary>
internal sealed class GlyphCache : IDisposable
{
    private readonly Dictionary<(GlyphFont, float, string, bool), GlyphMask> _cache = new();
    private readonly Dictionary<(GlyphFont, float, int, string, bool), GlyphMask> _lines = new();
    private const int MaxLines = 256;
    private DirectText? _text;

    /// <param name="lightText">The mask will be painted in a light colour (on a dark background).</param>
    public GlyphMask Get(GlyphFont font, float pixelSize, string text, bool lightText)
    {
        var key = (font, pixelSize, text, lightText);
        if (!_cache.TryGetValue(key, out GlyphMask? mask))
        {
            mask = Rasterize(font, pixelSize, text, lightText);
            _cache[key] = mask;
        }
        return mask;
    }

    /// <summary>
    /// A line of text in a box one line high, so different strings share the same baseline.
    /// Text wider than <paramref name="maxWidth"/> is cut with an ellipsis.
    /// </summary>
    public GlyphMask GetLine(GlyphFont font, float pixelSize, string text, int maxWidth, bool lightText)
    {
        var key = (font, pixelSize, maxWidth, text, lightText);
        if (!_lines.TryGetValue(key, out GlyphMask? mask))
        {
            // Status lines change with time ("updated 7 minutes ago"), so the cache is kept from growing for ever.
            if (_lines.Count >= MaxLines) _lines.Clear();
            mask = RasterizeLine(font, pixelSize, text, maxWidth, lightText);
            _lines[key] = mask;
        }
        return mask;
    }

    /// <summary>Forgets everything, for example after a display scale change.</summary>
    public void Clear()
    {
        _cache.Clear();
        _lines.Clear();
    }

    public void Dispose()
    {
        Clear();
        _text?.Dispose();
        _text = null;
    }

    private DirectText Text => _text ??= new DirectText();

    private static readonly Lazy<HashSet<string>> InstalledFamilies = new(() =>
    {
        using var fonts = new InstalledFontCollection();
        return new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
    });

    /// <summary>The first installed family for a role, with its weight. Windows 10 lacks the Windows 11 fonts.</summary>
    private static (string Family, int Weight) FontFor(GlyphFont role)
    {
        (string, int)[] candidates = role switch
        {
            GlyphFont.Icons => [("Segoe Fluent Icons", DirectText.WeightRegular), ("Segoe MDL2 Assets", DirectText.WeightRegular)],
            GlyphFont.Body => [("Segoe UI Variable Text", DirectText.WeightRegular), ("Segoe UI", DirectText.WeightRegular)],
            _ => [("Segoe UI Variable Small", DirectText.WeightSemibold), ("Segoe UI", DirectText.WeightSemibold)],
        };
        foreach (var candidate in candidates)
        {
            if (InstalledFamilies.Value.Contains(candidate.Item1)) return candidate;
        }
        return candidates[candidates.Length - 1];
    }

    private GlyphMask Rasterize(GlyphFont role, float size, string text, bool lightText)
    {
        var (family, weight) = FontFor(role);
        int height = (int)Math.Ceiling(size * 2) + 8;
        int width = (int)Math.Ceiling(size * 1.2f * Math.Max(1, text.Length)) + 16;
        uint[] pixels = Text.Render(family, weight, size, text, width, height, 8, 4, width - 8, lightText);

        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if ((pixels[y * width + x] >> 24) == 0) continue;
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
            for (int x = 0; x < maskWidth; x++) coverage[y * maskWidth + x] = (byte)(pixels[(top + y) * width + left + x] >> 24);
        }
        return new GlyphMask(maskWidth, maskHeight, coverage);
    }

    private GlyphMask RasterizeLine(GlyphFont role, float size, string text, int maxWidth, bool lightText)
    {
        var (family, weight) = FontFor(role);
        int height = (int)Math.Ceiling(size * 1.36f);
        int width = Math.Max(1, maxWidth);
        uint[] pixels = Text.Render(family, weight, size, text, width, height, 0, 0, width, lightText);

        // Keep the full line height (shared baseline) but trim empty columns on the right.
        int used = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if ((pixels[i] >> 24) != 0) used = Math.Max(used, i % width + 1);
        }
        if (used == 0) return GlyphMask.Empty;

        var coverage = new byte[used * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < used; x++) coverage[y * used + x] = (byte)(pixels[y * width + x] >> 24);
        }
        return new GlyphMask(used, height, coverage);
    }
}
