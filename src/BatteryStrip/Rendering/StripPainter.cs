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
    private const float NumberFont = 11;
    private const float BoltGap = 1, BoltFont = 13;
    private const float DeviceGap = 14;

    private const string Bolt = "";
    private const string Dash = "–";

    public static string IconOf(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "",
        DeviceKind.Headset => "",
        DeviceKind.Gamepad => "",
        DeviceKind.Keyboard => "",
        _ => "",
    };

    /// <param name="height">Height of the strip in pixels; content is centred vertically.</param>
    public StripImage Paint(IReadOnlyList<DeviceSnapshot> devices, Palette palette, float scale, int height)
    {
        float width = 0;
        foreach (DeviceSnapshot device in devices) width += DeviceWidth(device, scale);
        width += DeviceGap * scale * Math.Max(0, devices.Count - 1);

        var surface = new Surface(Math.Max(1, (int)Math.Ceiling(width)), height);
        var slots = new List<DeviceSlot>(devices.Count);
        float x = 0;
        foreach (DeviceSnapshot device in devices)
        {
            float w = DeviceWidth(device, scale);
            PaintDevice(surface, device, palette, scale, x);
            slots.Add(new DeviceSlot(device.Id, new RectF(x, 0, w, height)));
            x += w + DeviceGap * scale;
        }
        surface.MakeClickable();
        return new StripImage(surface, slots);
    }

    /// <summary>Width of the strip for these devices, without painting it.</summary>
    public float Measure(IReadOnlyList<DeviceSnapshot> devices, float scale)
    {
        float width = DeviceGap * scale * Math.Max(0, devices.Count - 1);
        foreach (DeviceSnapshot device in devices) width += DeviceWidth(device, scale);
        return width;
    }

    private float DeviceWidth(DeviceSnapshot device, float scale)
    {
        float width = (IconBox + IconGap + BodyWidth + NubGap + NubWidth) * scale;
        if (device.IsCharging && device.Percent != null) width += BoltGap * scale + glyphs.Get(GlyphFont.Icons, Px(BoltFont, scale), Bolt).Width;
        return (float)Math.Ceiling(width);
    }

    private void PaintDevice(Surface surface, DeviceSnapshot device, Palette palette, float scale, float left)
    {
        float middle = surface.Height / 2f;

        // Icon, centred in its box.
        GlyphMask icon = glyphs.Get(GlyphFont.Icons, Px(IconFont, scale), IconOf(device.Kind));
        float iconBox = IconBox * scale;
        surface.DrawMask(icon, (int)Math.Round(left + (iconBox - icon.Width) / 2), (int)Math.Round(middle - icon.Height / 2f),
            device.IsAsleep ? palette.Dimmed : palette.SecondaryText);

        // Battery body, on whole pixels so the 1 px outline stays sharp.
        float bodyLeft = (float)Math.Round(left + (IconBox + IconGap) * scale);
        float bodyTop = (float)Math.Round(middle - BodyHeight * scale / 2);
        var body = new RectF(bodyLeft, bodyTop, (float)Math.Round(BodyWidth * scale), (float)Math.Round(BodyHeight * scale));
        float line = Math.Max(1, (float)Math.Round(scale));
        surface.StrokeRoundedRect(body, BodyRadius * scale, line, palette.Outline);

        float nubHeight = (float)Math.Round(NubHeight * scale);
        var nub = new RectF(body.Right + (float)Math.Round(NubGap * scale), (float)Math.Round(middle - nubHeight / 2),
            Math.Max(1, (float)Math.Round(NubWidth * scale)), nubHeight);
        surface.FillRoundedRect(new RectF(nub.X - nub.Width, nub.Y, nub.Width * 2, nub.Height), nub.Width, palette.Outline, clip: nub);

        RectF inner = body.Inset(line);
        if (device.Percent is not int percent)
        {
            GlyphMask dash = glyphs.Get(GlyphFont.NumberSemibold, Px(NumberFont, scale), Dash);
            surface.DrawMask(dash, Centre(inner.X, inner.Width, dash.Width), Centre(inner.Y, inner.Height, dash.Height), palette.SecondaryText);
            return;
        }

        // The number: plain where the battery is empty, cut out of the fill where it is full.
        GlyphMask number = glyphs.Get(GlyphFont.NumberSemibold, Px(NumberFont, scale), percent.ToString());
        int numberX = Centre(inner.X, inner.Width, number.Width), numberY = Centre(inner.Y, inner.Height, number.Height);
        surface.DrawMask(number, numberX, numberY, device.IsAsleep ? palette.Dimmed : palette.Text);

        RectF track = body.Inset(FillInset * scale);
        float fillWidth = (float)Math.Round(track.Width * percent / 100f);
        if (fillWidth > 0)
        {
            var fill = new RectF(track.X, track.Y, fillWidth, track.Height);
            surface.FillRoundedRect(fill, FillRadius * scale, FillColour(device, palette), clip: null, hole: number, holeX: numberX, holeY: numberY);
        }

        if (device.IsCharging)
        {
            GlyphMask bolt = glyphs.Get(GlyphFont.Icons, Px(BoltFont, scale), Bolt);
            surface.DrawMask(bolt, (int)Math.Round(nub.Right + BoltGap * scale), (int)Math.Round(middle - bolt.Height / 2f), palette.Charging);
        }
    }

    private static Rgba FillColour(DeviceSnapshot device, Palette palette) =>
        device.IsAsleep ? palette.Dimmed
        : device.IsCharging ? palette.Charging
        : device.Level switch
        {
            ChargeLevel.Critical => palette.Critical,
            ChargeLevel.Low => palette.Low,
            _ => palette.Text,
        };

    private static int Centre(float start, float length, int size) => (int)Math.Round(start + (length - size) / 2);

    private static float Px(float size, float scale) => (float)Math.Round(size * scale * 2) / 2;
}
