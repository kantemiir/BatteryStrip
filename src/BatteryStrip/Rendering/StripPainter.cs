using System;
using System.Collections.Generic;
using BatteryStrip.Core;

namespace BatteryStrip.Rendering;

/// <summary>Where one device ended up on the strip, for clicks and tooltips.</summary>
internal readonly record struct DeviceSlot(string DeviceId, RectF Bounds);

/// <summary>A painted strip: pixels plus the position of each device.</summary>
internal sealed record StripImage(Surface Surface, IReadOnlyList<DeviceSlot> Slots);

/// <summary>
/// Paints the strip: for each device an icon and a battery outlined the way Windows draws its own, with the number
/// inside. The charge shows as a lighter tone of the text colour, but the number is bold and one solid colour all the
/// way across, so no digit is ever split by the edge of the fill (chosen 2026-11-17, variant "Б", after the digits of
/// the first outlined battery proved hard to read). Sizes are given at 100 % display scale and multiplied by <c>scale</c>.
/// </summary>
internal sealed class StripPainter(GlyphCache glyphs)
{
    private const float IconBox = 16, IconFont = 16, IconGap = 5;
    private const float BodyWidth = 34, BodyHeight = 18, BodyRadius = 4;

    /// <summary>The fill keeps a hairline gap inside the outline, like the battery glyph in the Windows tray.</summary>
    private const float FillInset = 2, FillRadius = 2;
    private const float NubGap = 1, NubWidth = 2, NubHeight = 6;
    private const float NumberFont = 14;

    /// <summary>Opacity of the charge, in the text colour. Light enough for the number on top to keep 4.5:1 or
    /// better against it on a dark or a light taskbar.</summary>
    private const double ChargeOpacity = 0.30;

    // A sleeping device is faded like a disabled icon in Windows: a dim outline and number, no fill.
    private const double AsleepIcon = 0.45, AsleepOutline = 0.5, AsleepNumber = 0.7;

    /// <summary>Opacity of the outline and nub, in the text colour.</summary>
    private const double OutlineOpacityDark = 0.9, OutlineOpacityLight = 0.8;

    /// <summary>Charging bolt: small and solid, beside the battery.</summary>
    private const float BoltGap = 2, BoltHeight = 10;

    private const float DeviceGap = 14, CompactGap = 8;

    /// <summary>A nearly empty battery still shows this much of its red fill, or the warning colour would vanish.
    /// Warning fills stop at 20 %, short of the number, which is why they can be fully saturated.</summary>
    private const float MinFill = 4;

    /// <summary>Hover plate behind a device, like the one Windows draws behind tray icons. It reaches this far
    /// past the device on each side, so the strip keeps that much room at both ends.</summary>
    public const float HoverPad = 4;

    private const float HoverHeight = 32, HoverRadius = 4;

    private const string Dash = "–";

    /// <summary>The bolt outline on a 10 × 16 grid: a classic lightning shape with a sharp lower tip.</summary>
    private static readonly (float X, float Y)[] BoltShape =
    [
        (6.4f, 0f), (0.6f, 9.2f), (4.6f, 9.2f), (3.6f, 16f), (9.4f, 6.6f), (5.4f, 6.6f),
    ];

    private readonly System.Collections.Generic.Dictionary<float, GlyphMask> _bolts = new();


    public static string IconOf(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "",
        DeviceKind.Headset => "",
        DeviceKind.Gamepad => "",
        DeviceKind.Keyboard => "",
        DeviceKind.Speaker => "",
        _ => "",
    };

    /// <param name="height">Height of the strip in pixels; content is centred vertically.</param>
    /// <param name="compact">Batteries only, without device icons: for a crowded taskbar.</param>
    /// <param name="hoveredId">Device under the mouse, drawn on a faint plate.</param>
    public StripImage Paint(IReadOnlyList<DeviceSnapshot> devices, Palette palette, float scale, int height, bool compact = false, string? hoveredId = null)
    {
        var surface = new Surface(Math.Max(1, (int)Math.Ceiling(Measure(devices, scale, compact))), height);
        var slots = new List<DeviceSlot>(devices.Count);
        float pad = (float)Math.Round(HoverPad * scale);
        float x = pad;
        foreach (DeviceSnapshot device in devices)
        {
            float w = DeviceWidth(device, scale, compact);
            float middle = height / 2f;
            if (device.Id == hoveredId)
            {
                float plateHeight = Math.Min(height, (float)Math.Round(HoverHeight * scale));
                var plate = new RectF(x - pad, (float)Math.Round((height - plateHeight) / 2), w + 2 * pad, plateHeight);
                surface.FillRoundedRect(plate, HoverRadius * scale, palette.Text.WithOpacity(palette.IsLight ? 0.06 : 0.08));
            }
            if (compact)
            {
                PaintBattery(surface, device, palette, scale, x, middle);
            }
            else
            {
                PaintIcon(surface, device, palette, IconFont * scale, x, middle, IconBox * scale);
                PaintBattery(surface, device, palette, scale, x + (IconBox + IconGap) * scale, middle);
            }
            // A slot reaches halfway into the gaps, so a click or hover between two devices still finds one.
            float half = Gap(compact) * scale / 2;
            slots.Add(new DeviceSlot(device.Id, new RectF(x - half, 0, w + 2 * half, height)));
            x += w + Gap(compact) * scale;
        }
        surface.MakeClickable();
        return new StripImage(surface, slots);
    }

    /// <summary>Width of the strip for these devices, without painting it.</summary>
    public float Measure(IReadOnlyList<DeviceSnapshot> devices, float scale, bool compact = false)
    {
        float width = Gap(compact) * scale * Math.Max(0, devices.Count - 1) + 2 * (float)Math.Round(HoverPad * scale);
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
        if (ShowsBolt(device)) width += BoltSpace(scale);
        return (float)Math.Ceiling(width);
    }

    /// <summary>Width of a battery with its nub, without the bolt.</summary>
    public float PlainBatteryWidth(float scale) => (float)Math.Ceiling((BodyWidth + NubGap + NubWidth) * scale);

    /// <summary>Room the charging bolt takes beside the battery.</summary>
    public float BoltSpace(float scale) => BoltGap * scale + BoltMask(scale).Width;

    /// <summary>A sleeping device is drawn as an outline without a bolt, even if it was charging when last seen.</summary>
    public static bool ShowsBolt(DeviceSnapshot device) => device.IsCharging && device.Percent != null && !device.IsAsleep;

    /// <summary>Draws a device icon centred in a square box of <paramref name="box"/> pixels.</summary>
    public void PaintIcon(Surface surface, DeviceSnapshot device, Palette palette, float fontSize, float left, float middle, float box)
    {
        GlyphMask icon = glyphs.Get(GlyphFont.Icons, (float)Math.Round(fontSize * 2) / 2, IconOf(device.Kind), !palette.IsLight);
        int x = (int)Math.Round(left + (box - icon.Width) / 2), y = (int)Math.Round(middle - icon.Height / 2f);
        surface.DrawMask(icon, x, y, device.IsAsleep ? palette.Text.WithOpacity(AsleepIcon) : palette.Text);
    }

    /// <summary>Draws the battery with its left edge at <paramref name="left"/>, centred on <paramref name="middle"/>.</summary>
    /// <param name="boltBefore">Puts the charging bolt left of the battery (at <paramref name="left"/> minus
    /// <see cref="BoltSpace"/>), so batteries in a column stay aligned; by default it follows the nub.</param>
    public void PaintBattery(Surface surface, DeviceSnapshot device, Palette palette, float scale, float left, float middle, bool boltBefore = false)
    {
        bool asleep = device.IsAsleep;
        Rgba outline = palette.Text.WithOpacity(palette.IsLight ? OutlineOpacityLight : OutlineOpacityDark);
        if (asleep) outline = outline.WithOpacity(AsleepOutline);

        // Battery body, on whole pixels so the 1 px outline stays sharp.
        float bodyLeft = (float)Math.Round(left);
        float bodyTop = (float)Math.Round(middle - BodyHeight * scale / 2);
        var shell = new RectF(bodyLeft, bodyTop, (float)Math.Round(BodyWidth * scale), (float)Math.Round(BodyHeight * scale));
        float line = Math.Max(1, (float)Math.Round(scale));
        surface.StrokeRoundedRect(shell, BodyRadius * scale, line, outline);

        // The nub is centred on the body itself, not on the unrounded middle, or it sits a pixel low at some scales.
        float nubHeight = (float)Math.Round(NubHeight * scale);
        if ((shell.Height - nubHeight) % 2 != 0) nubHeight += 1;
        var nub = new RectF(shell.Right + (float)Math.Round(NubGap * scale), shell.Y + (shell.Height - nubHeight) / 2,
            Math.Max(1, (float)Math.Round(NubWidth * scale)), nubHeight);
        surface.FillRoundedRect(new RectF(nub.X - nub.Width, nub.Y, nub.Width * 2, nub.Height), nub.Width, outline, clip: nub);

        RectF inner = shell.Inset(line);
        if (device.Percent is not int percent)
        {
            GlyphMask dash = glyphs.Get(GlyphFont.NumberBold, Px(NumberFont, scale), Dash, !palette.IsLight);
            surface.DrawMask(dash, Centre(inner.X, inner.Width, dash.Width), Centre(inner.Y, inner.Height, dash.Height), palette.SecondaryText);
            return;
        }

        GlyphMask number = Number(percent, palette, scale, inner.Width);
        int numberX = Centre(inner.X, inner.Width, number.Width), numberY = Centre(inner.Y, inner.Height, number.Height);
        if (asleep)
        {
            surface.DrawMask(number, numberX, numberY, palette.Text.WithOpacity(AsleepNumber));
            return;
        }

        RectF track = shell.Inset((float)Math.Round(FillInset * scale));
        float fillWidth = (float)Math.Round(track.Width * percent / 100f);
        if (percent > 0) fillWidth = Math.Min(track.Width, Math.Max(fillWidth, (float)Math.Round(MinFill * scale)));

        // The number goes over the fill, never out of it, and in one colour: the fill is only a lighter tone behind it.
        if (fillWidth > 0)
        {
            var fill = new RectF(track.X, track.Y, fillWidth, track.Height);
            surface.FillRoundedRect(track, FillRadius * scale, FillColour(device, palette), clip: fill);
        }
        surface.DrawMask(number, numberX, numberY, palette.Text);

        if (device.IsCharging)
        {
            GlyphMask bolt = BoltMask(scale);
            float boltX = boltBefore ? bodyLeft - BoltGap * scale - bolt.Width : nub.Right + BoltGap * scale;
            surface.DrawMask(bolt, (int)Math.Round(boltX), (int)Math.Round(shell.Y + (shell.Height - bolt.Height) / 2f), palette.Text);
        }
    }

    /// <summary>The number inside a battery; "100" is set a little smaller if it would crowd the edges.</summary>
    private GlyphMask Number(int percent, Palette palette, float scale, float room)
    {
        GlyphMask number = glyphs.Get(GlyphFont.NumberBold, Px(NumberFont, scale), percent.ToString(), !palette.IsLight);
        if (number.Width <= room - 4 * scale) return number;
        return glyphs.Get(GlyphFont.NumberBold, Px(NumberFont - 1.5f, scale), percent.ToString(), !palette.IsLight);
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

    /// <summary>A light tone of the text colour, or a full warning colour when low. A charging device is no warning:
    /// the bolt says it charges.</summary>
    private static Rgba FillColour(DeviceSnapshot device, Palette palette) =>
        device.IsCharging ? palette.Text.WithOpacity(ChargeOpacity)
        : device.Level switch
        {
            ChargeLevel.Critical => palette.Critical,
            ChargeLevel.Low => palette.Low,
            _ => palette.Text.WithOpacity(ChargeOpacity),
        };

    private static int Centre(float start, float length, int size) => (int)Math.Round(start + (length - size) / 2);

    private static float Px(float size, float scale) => (float)Math.Round(size * scale * 2) / 2;
}
