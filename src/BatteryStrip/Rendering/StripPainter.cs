using System;
using System.Collections.Generic;
using BatteryStrip.Core;

namespace BatteryStrip.Rendering;

/// <summary>Where one device ended up on the strip, for clicks and tooltips.</summary>
internal readonly record struct DeviceSlot(string DeviceId, RectF Bounds);

/// <summary>A painted strip: pixels plus the position of each device.</summary>
internal sealed record StripImage(Surface Surface, IReadOnlyList<DeviceSlot> Slots);

/// <summary>
/// Paints the strip as agreed in research/design-mock.html: for each device an icon and an iPhone-style
/// battery with the number inside. Sizes are given at 100 % display scale and multiplied by <c>scale</c>.
/// </summary>
internal sealed class StripPainter(GlyphCache glyphs)
{
    private const float IconBox = 16, IconFont = 16, IconGap = 5;
    private const float BodyWidth = 30, BodyHeight = 15, BodyRadius = 5;
    private const float FillInset = 2, FillRadius = 3;
    private const float NubGap = 1, NubWidth = 2, NubHeight = 5;
    private const float NumberFont = 12;

    // A sleeping device is faded like a disabled icon in Windows. Its number is not cut out of the fill:
    // a faded fill with holes reads as dark digits on grey, so the number is drawn on top instead.
    private const double AsleepIcon = 0.45, AsleepOutline = 0.5, AsleepFill = 0.22, AsleepNumber = 0.7;

    /// <summary>
    /// Opacity of the battery's empty part. The number is cut out of the whole battery, so it reads in one colour
    /// against both the fill and the empty part; the empty part has to be dense enough for the cut-out to show.
    /// </summary>
    private const double BodyOpacityDark = 0.5, BodyOpacityLight = 0.56;

    /// <summary>Charging bolt: small and solid, beside the battery, like the iPhone's with the percentage shown.</summary>
    private const float BoltGap = 2, BoltHeight = 10;

    private const float DeviceGap = 14, CompactGap = 8;

    private const string Dash = "\u2013";

    /// <summary>The bolt outline on a 10 × 16 grid: a classic lightning shape with a sharp lower tip.</summary>
    private static readonly (float X, float Y)[] BoltShape =
    [
        (6.4f, 0f), (0.6f, 9.2f), (4.6f, 9.2f), (3.6f, 16f), (9.4f, 6.6f), (5.4f, 6.6f),
    ];

    private readonly System.Collections.Generic.Dictionary<float, GlyphMask> _bolts = new();


    public static string IconOf(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "\uE962",
        DeviceKind.Headset => "\uE7F6",
        DeviceKind.Gamepad => "\uE7FC",
        DeviceKind.Keyboard => "\uE765",
        DeviceKind.Speaker => "\uE7F5",
        _ => "\uE772",
    };

    /// <param name="height">Height of the strip in pixels; content is centred vertically.</param>
    /// <param name="compact">Batteries only, without device icons: for a crowded taskbar.</param>
    public StripImage Paint(IReadOnlyList<DeviceSnapshot> devices, Palette palette, float scale, int height, bool compact = false)
    {
        var surface = new Surface(Math.Max(1, (int)Math.Ceiling(Measure(devices, scale, compact))), height);
        var slots = new List<DeviceSlot>(devices.Count);
        float x = 0;
        foreach (DeviceSnapshot device in devices)
        {
            float w = DeviceWidth(device, scale, compact);
            float middle = height / 2f;
            if (compact)
            {
                PaintBattery(surface, device, palette, scale, x, middle);
            }
            else
            {
                PaintIcon(surface, device, palette, IconFont * scale, x, middle, IconBox * scale);
                PaintBattery(surface, device, palette, scale, x + (IconBox + IconGap) * scale, middle);
            }
            slots.Add(new DeviceSlot(device.Id, new RectF(x, 0, w, height)));
            x += w + Gap(compact) * scale;
        }
        surface.MakeClickable();
        return new StripImage(surface, slots);
    }

    /// <summary>Width of the strip for these devices, without painting it.</summary>
    public float Measure(IReadOnlyList<DeviceSnapshot> devices, float scale, bool compact = false)
    {
        float width = Gap(compact) * scale * Math.Max(0, devices.Count - 1);
        foreach (DeviceSnapshot device in devices) width += DeviceWidth(device, scale, compact);
        return (float)Math.Ceiling(width);
    }

    private static float Gap(bool compact) => compact ? CompactGap : DeviceGap;

    private float DeviceWidth(DeviceSnapshot device, float scale, bool compact) =>
        (float)Math.Ceiling((compact ? 0 : (IconBox + IconGap) * scale) + BatteryWidth(device, scale));

    /// <summary>Width of a battery with its nub and, when charging, the bolt.</summary>
    public float BatteryWidth(DeviceSnapshot device, float scale)
    {
        float width = (BodyWidth + NubGap + NubWidth) * scale;
        if (device.IsCharging && device.Percent != null) width += BoltGap * scale + BoltMask(scale).Width;
        return (float)Math.Ceiling(width);
    }

    /// <summary>Draws a device icon centred in a square box of <paramref name="box"/> pixels.</summary>
    public void PaintIcon(Surface surface, DeviceSnapshot device, Palette palette, float fontSize, float left, float middle, float box)
    {
        GlyphMask icon = glyphs.Get(GlyphFont.Icons, (float)Math.Round(fontSize * 2) / 2, IconOf(device.Kind), !palette.IsLight);
        int x = (int)Math.Round(left + (box - icon.Width) / 2), y = (int)Math.Round(middle - icon.Height / 2f);
        surface.DrawMask(icon, x, y, device.IsAsleep ? palette.Text.WithOpacity(AsleepIcon) : palette.Text);
    }

    /// <summary>Draws the battery with its left edge at <paramref name="left"/>, centred on <paramref name="middle"/>.</summary>
    public void PaintBattery(Surface surface, DeviceSnapshot device, Palette palette, float scale, float left, float middle)
    {
        bool asleep = device.IsAsleep;
        // A solid battery (as on the iPhone) means fresh data; a sleeping device or one without data is a hollow outline.
        bool solid = !asleep && device.Percent != null;
        Rgba outline = asleep ? palette.Outline.WithOpacity(AsleepOutline) : palette.Outline;
        Rgba body = palette.Text.WithOpacity(palette.IsLight ? BodyOpacityLight : BodyOpacityDark);

        // Battery body, on whole pixels so the 1 px outline stays sharp.
        float bodyLeft = (float)Math.Round(left);
        float bodyTop = (float)Math.Round(middle - BodyHeight * scale / 2);
        var shell = new RectF(bodyLeft, bodyTop, (float)Math.Round(BodyWidth * scale), (float)Math.Round(BodyHeight * scale));
        float line = Math.Max(1, (float)Math.Round(scale));
        if (!solid) surface.StrokeRoundedRect(shell, BodyRadius * scale, line, outline);

        float nubHeight = (float)Math.Round(NubHeight * scale);
        var nub = new RectF(shell.Right + (float)Math.Round(NubGap * scale), (float)Math.Round(middle - nubHeight / 2),
            Math.Max(1, (float)Math.Round(NubWidth * scale)), nubHeight);
        surface.FillRoundedRect(new RectF(nub.X - nub.Width, nub.Y, nub.Width * 2, nub.Height), nub.Width, solid ? body : outline, clip: nub);

        RectF inner = shell.Inset(line);
        if (device.Percent is not int percent)
        {
            GlyphMask dash = glyphs.Get(GlyphFont.NumberSemibold, Px(NumberFont, scale), Dash, !palette.IsLight);
            surface.DrawMask(dash, Centre(inner.X, inner.Width, dash.Width), Centre(inner.Y, inner.Height, dash.Height), palette.SecondaryText);
            return;
        }

        GlyphMask number = Number(percent, palette, scale, inner.Width);
        int numberX = Centre(inner.X, inner.Width, number.Width), numberY = Centre(inner.Y, inner.Height, number.Height);
        RectF track = solid ? shell : shell.Inset(FillInset * scale);
        float radius = solid ? BodyRadius * scale : FillRadius * scale;
        float fillWidth = (float)Math.Round(track.Width * percent / 100f);
        var fill = new RectF(track.X, track.Y, fillWidth, track.Height);
        var empty = new RectF(track.X + fillWidth, track.Y, track.Width - fillWidth, track.Height);

        if (asleep)
        {
            if (fillWidth > 0) surface.FillRoundedRect(track, radius, FillColour(device, palette).WithOpacity(AsleepFill), clip: fill);
            surface.DrawMask(number, numberX, numberY, palette.Text.WithOpacity(AsleepNumber));
            return;
        }

        // The number is cut out of both parts, so it never splits into two colours where the fill ends.
        if (empty.Width > 0) surface.FillRoundedRect(track, radius, body, clip: empty, hole: number, holeX: numberX, holeY: numberY);
        if (fillWidth > 0)
            surface.FillRoundedRect(track, radius, FillColour(device, palette), clip: fill, hole: number, holeX: numberX, holeY: numberY);

        if (device.IsCharging)
        {
            GlyphMask bolt = BoltMask(scale);
            surface.DrawMask(bolt, (int)Math.Round(nub.Right + BoltGap * scale), (int)Math.Round(middle - bolt.Height / 2f), palette.Text);
        }
    }

    /// <summary>The number inside a battery; "100" is set a little smaller so it keeps clear of the edges.</summary>
    private GlyphMask Number(int percent, Palette palette, float scale, float room)
    {
        GlyphMask number = glyphs.Get(GlyphFont.NumberSemibold, Px(NumberFont, scale), percent.ToString(), !palette.IsLight);
        if (number.Width <= room - 4 * scale) return number;
        return glyphs.Get(GlyphFont.NumberSemibold, Px(NumberFont - 1.5f, scale), percent.ToString(), !palette.IsLight);
    }

    private GlyphMask BoltMask(float scale)
    {
        float height = (float)Math.Round(BoltHeight * scale);
        if (!_bolts.TryGetValue(height, out GlyphMask? mask))
        {
            float unit = height / 16f;
            var points = new (float X, float Y)[BoltShape.Length];
            for (int i = 0; i < points.Length; i++) points[i] = (BoltShape[i].X * unit, BoltShape[i].Y * unit);
            mask = Surface.PolygonMask(points);
            _bolts[height] = mask;
        }
        return mask;
    }

    private static Rgba FillColour(DeviceSnapshot device, Palette palette) =>
        device.IsCharging ? palette.Charging
        : device.Level switch
        {
            ChargeLevel.Critical => palette.Critical,
            ChargeLevel.Low => palette.Low,
            _ => palette.Text,
        };

    private static int Centre(float start, float length, int size) => (int)Math.Round(start + (length - size) / 2);

    private static float Px(float size, float scale) => (float)Math.Round(size * scale * 2) / 2;
}
