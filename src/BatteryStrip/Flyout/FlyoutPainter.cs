using System;
using System.Collections.Generic;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Rendering;

namespace BatteryStrip.Flyout;

/// <summary>Paints the flyout from research/design-mock.html: one row per device and a footer with the gear.</summary>
internal sealed class FlyoutPainter(GlyphCache glyphs, StripPainter batteries)
{
    public const float Width = 300;
    private const float PadX = 14, PadTop = 4;
    private const float RowPadY = 10, IconFont = 20, IconBox = 24, IconGap = 12, TextGap = 12;
    private const float NameFont = 13, StatusFont = 12;
    private const float FooterPadTop = 10, FooterPadBottom = 8, FooterFont = 12, GearFont = 16, GearBox = 28;

    private const string Gear = "\uE713";

    /// <summary>Screen-independent result: pixels and where the gear button is.</summary>
    public sealed record Image(Surface Surface, RectF Gear);

    public Image Paint(IReadOnlyList<DeviceSnapshot> devices, Palette palette, float scale, bool gearHovered, DateTime utcNow, string footer)
    {
        float rowHeight = RowHeight(scale);
        float footerHeight = (FooterPadTop + FooterPadBottom) * scale + GearBox * scale;
        int rows = Math.Max(1, devices.Count);
        int width = (int)Math.Round(Width * scale);
        int height = (int)Math.Round(PadTop * scale + rows * rowHeight + footerHeight);

        var surface = new Surface(width, height);
        surface.FillRoundedRect(new RectF(0, 0, width, height), 0, palette.Background);

        float left = (float)Math.Round(PadX * scale), right = width - left;
        float y = (float)Math.Round(PadTop * scale);

        if (devices.Count == 0)
        {
            GlyphMask empty = glyphs.GetLine(GlyphFont.Body, Px(NameFont, scale), Text.Current.NoDevices, (int)(right - left), !palette.IsLight);
            surface.DrawMask(empty, (int)left, (int)Math.Round(y + (rowHeight - empty.Height) / 2), palette.SecondaryText);
            y += rowHeight;
            Divider(surface, palette, left, right, y);
        }

        foreach (DeviceSnapshot device in devices)
        {
            PaintRow(surface, device, palette, scale, left, right, y, rowHeight, utcNow);
            y += rowHeight;
            Divider(surface, palette, left, right, y);
        }

        // Footer: the warning thresholds and the settings button.
        float gearSize = GearBox * scale;
        var gear = new RectF(right - gearSize + 6 * scale, y + FooterPadTop * scale, gearSize, gearSize);
        float footerMiddle = gear.Y + gear.Height / 2;
        GlyphMask label = glyphs.GetLine(GlyphFont.Body, Px(FooterFont, scale), footer, (int)(gear.X - left), !palette.IsLight);
        surface.DrawMask(label, (int)left, (int)Math.Round(footerMiddle - label.Height / 2f), palette.SecondaryText);

        if (gearHovered) surface.FillRoundedRect(gear, 4 * scale, palette.Text.WithOpacity(palette.IsLight ? 0.06 : 0.08));
        GlyphMask gearIcon = glyphs.Get(GlyphFont.Icons, Px(GearFont, scale), Gear, !palette.IsLight);
        surface.DrawMask(gearIcon, (int)Math.Round(gear.X + (gear.Width - gearIcon.Width) / 2), (int)Math.Round(footerMiddle - gearIcon.Height / 2f), palette.SecondaryText);

        return new Image(surface, gear);
    }

    private void PaintRow(Surface surface, DeviceSnapshot device, Palette palette, float scale, float left, float right, float top, float height, DateTime utcNow)
    {
        float middle = top + height / 2;
        batteries.PaintIcon(surface, device, palette, IconFont * scale, left, middle, IconBox * scale);

        float batteryWidth = batteries.BatteryWidth(device, scale);
        float batteryLeft = right - batteryWidth;
        batteries.PaintBattery(surface, device, palette, scale, batteryLeft, middle);

        float textLeft = left + (IconBox + IconGap) * scale;
        int textWidth = (int)(batteryLeft - TextGap * scale - textLeft);
        GlyphMask name = glyphs.GetLine(GlyphFont.Body, Px(NameFont, scale), device.Name, textWidth, !palette.IsLight);
        GlyphMask status = glyphs.GetLine(GlyphFont.Body, Px(StatusFont, scale), StatusText.For(device, utcNow), textWidth, !palette.IsLight);
        float textTop = (float)Math.Round(middle - (name.Height + status.Height) / 2f);
        surface.DrawMask(name, (int)textLeft, (int)textTop, device.IsAsleep ? palette.SecondaryText : palette.Text);
        surface.DrawMask(status, (int)textLeft, (int)textTop + name.Height, palette.SecondaryText);
    }

    private float RowHeight(float scale)
    {
        GlyphMask name = glyphs.GetLine(GlyphFont.Body, Px(NameFont, scale), "Яg", 200, lightText: true);
        GlyphMask status = glyphs.GetLine(GlyphFont.Body, Px(StatusFont, scale), "Яg", 200, lightText: true);
        return (float)Math.Round(2 * RowPadY * scale + name.Height + status.Height);
    }

    private static void Divider(Surface surface, Palette palette, float left, float right, float y) =>
        surface.FillRoundedRect(new RectF(left, y - 1, right - left, 1), 0, palette.Divider);

    private static float Px(float size, float scale) => (float)Math.Round(size * scale * 2) / 2;
}
