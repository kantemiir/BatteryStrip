using System;

namespace BatteryStrip.Rendering;

/// <summary>Colour with straight (not premultiplied) alpha.</summary>
internal readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba FromHex(uint rgb, double opacity = 1.0) =>
        new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)Math.Round(255 * opacity));

    public Rgba WithOpacity(double opacity) => this with { A = (byte)Math.Round(A * opacity) };
}

/// <summary>Axis-aligned rectangle in pixels; edges may fall between pixels.</summary>
internal readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public RectF Inset(float by) => new(X + by, Y + by, Width - 2 * by, Height - 2 * by);
}

/// <summary>Coverage bitmap of some text or an icon, cropped to its ink.</summary>
internal sealed class GlyphMask(int width, int height, byte[] coverage)
{
    public static GlyphMask Empty { get; } = new(0, 0, Array.Empty<byte>());

    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>Coverage 0..255 at a pixel; 0 outside the mask.</summary>
    public byte At(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? coverage[y * Width + x] : (byte)0;
}

/// <summary>
/// A small software canvas with per-pixel transparency, in the premultiplied BGRA layout Windows expects
/// for layered windows. Shapes are antialiased analytically, so they stay crisp at any display scale.
/// </summary>
internal sealed class Surface(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>Pixels as 0xAARRGGBB, premultiplied, top row first.</summary>
    public uint[] Pixels { get; } = new uint[width * height];

    public void Clear() => Array.Clear(Pixels, 0, Pixels.Length);

    public void FillRoundedRect(RectF rect, float radius, Rgba color, RectF? clip = null) =>
        FillRoundedRect(rect, radius, color, clip, hole: null, holeX: 0, holeY: 0);

    /// <summary>Fills a rounded rectangle, leaving the pixels covered by <paramref name="hole"/> transparent.</summary>
    /// <param name="holeX">Left edge of the hole mask on the surface.</param>
    public void FillRoundedRect(RectF rect, float radius, Rgba color, RectF? clip, GlyphMask? hole, int holeX, int holeY)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        RectF bounds = clip ?? rect;

        int x0 = Math.Max(0, (int)Math.Floor(Math.Max(rect.X, bounds.X)));
        int y0 = Math.Max(0, (int)Math.Floor(Math.Max(rect.Y, bounds.Y)));
        int x1 = Math.Min(Width, (int)Math.Ceiling(Math.Min(rect.Right, bounds.Right)));
        int y1 = Math.Min(Height, (int)Math.Ceiling(Math.Min(rect.Bottom, bounds.Bottom)));

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                float coverage = RoundedRectCoverage(x + 0.5f, y + 0.5f, rect, radius);
                if (clip is RectF c) coverage *= BoxCoverage(x, y, c);
                if (coverage <= 0) continue;

                float kept = hole == null ? 1f : 1f - hole.At(x - holeX, y - holeY) / 255f;
                Composite(x, y, color, coverage, kept);
            }
        }
    }

    /// <summary>Draws the outline of a rounded rectangle, lying inside <paramref name="rect"/>.</summary>
    public void StrokeRoundedRect(RectF rect, float radius, float thickness, Rgba color)
    {
        RectF inner = rect.Inset(thickness);
        float innerRadius = Math.Max(0, radius - thickness);

        int x0 = Math.Max(0, (int)Math.Floor(rect.X)), y0 = Math.Max(0, (int)Math.Floor(rect.Y));
        int x1 = Math.Min(Width, (int)Math.Ceiling(rect.Right)), y1 = Math.Min(Height, (int)Math.Ceiling(rect.Bottom));

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                float coverage = RoundedRectCoverage(x + 0.5f, y + 0.5f, rect, radius)
                    - RoundedRectCoverage(x + 0.5f, y + 0.5f, inner, innerRadius);
                if (coverage > 0) Composite(x, y, color, coverage, 1f);
            }
        }
    }

    /// <summary>Paints a glyph mask in one colour with its top-left corner at (<paramref name="left"/>, <paramref name="top"/>).</summary>
    public void DrawMask(GlyphMask mask, int left, int top, Rgba color)
    {
        for (int y = Math.Max(0, top); y < Math.Min(Height, top + mask.Height); y++)
        {
            for (int x = Math.Max(0, left); x < Math.Min(Width, left + mask.Width); x++)
            {
                byte coverage = mask.At(x - left, y - top);
                if (coverage != 0) Composite(x, y, color, coverage / 255f, 1f);
            }
        }
    }

    /// <summary>
    /// Gives every fully transparent pixel the faintest possible opacity. A layered window ignores mouse
    /// clicks on fully transparent pixels; this makes the whole surface clickable while staying invisible.
    /// </summary>
    public void MakeClickable()
    {
        for (int i = 0; i < Pixels.Length; i++)
        {
            if (Pixels[i] >> 24 == 0) Pixels[i] = 0x01000000;
        }
    }

    /// <summary>Source-over blend. <paramref name="kept"/> scales what the source adds without reducing how much it covers.</summary>
    private void Composite(int x, int y, Rgba color, float coverage, float kept)
    {
        float alpha = color.A / 255f * coverage;
        float add = alpha * kept;
        float keepDestination = 1f - alpha;

        uint destination = Pixels[y * Width + x];
        float a = (destination >> 24) * keepDestination + 255f * add;
        float r = ((destination >> 16) & 0xFF) * keepDestination + color.R * add;
        float g = ((destination >> 8) & 0xFF) * keepDestination + color.G * add;
        float b = (destination & 0xFF) * keepDestination + color.B * add;

        Pixels[y * Width + x] = ((uint)(a + 0.5f) << 24) | ((uint)(r + 0.5f) << 16) | ((uint)(g + 0.5f) << 8) | (uint)(b + 0.5f);
    }

    /// <summary>Share of the pixel centred at (px, py) that lies inside the rounded rectangle.</summary>
    private static float RoundedRectCoverage(float px, float py, RectF rect, float radius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return 0;
        float halfW = rect.Width / 2, halfH = rect.Height / 2;
        float qx = Math.Abs(px - (rect.X + halfW)) - (halfW - radius);
        float qy = Math.Abs(py - (rect.Y + halfH)) - (halfH - radius);
        float outsideX = Math.Max(qx, 0), outsideY = Math.Max(qy, 0);
        float distance = (float)Math.Sqrt(outsideX * outsideX + outsideY * outsideY) + Math.Min(Math.Max(qx, qy), 0) - radius;
        return Math.Max(0, Math.Min(1, 0.5f - distance));
    }

    /// <summary>Share of pixel (x, y) that lies inside a sharp-cornered box.</summary>
    private static float BoxCoverage(int x, int y, RectF box)
    {
        float w = Math.Min(x + 1, box.Right) - Math.Max(x, box.X);
        float h = Math.Min(y + 1, box.Bottom) - Math.Max(y, box.Y);
        return Math.Max(0, Math.Min(1, w)) * Math.Max(0, Math.Min(1, h));
    }
}
