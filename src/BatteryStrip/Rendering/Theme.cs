using BatteryStrip.App;
using Microsoft.Win32;

namespace BatteryStrip.Rendering;

/// <summary>Colours of the strip and the flyout for a dark or a light taskbar, as agreed in research/design-mock.html.</summary>
internal sealed record Palette(
    bool IsLight,
    Rgba Text,
    Rgba SecondaryText,
    Rgba Dimmed,
    Rgba Outline,
    Rgba Low,
    Rgba Critical,
    Rgba Charging,
    Rgba Divider,
    Rgba Background)
{
    public static Palette Dark { get; } = new(
        IsLight: false,
        Text: Rgba.FromHex(0xFFFFFF),
        SecondaryText: Rgba.FromHex(0xC9D1D9),
        Dimmed: Rgba.FromHex(0x8A9099),
        Outline: Rgba.FromHex(0xFFFFFF, 0.6),
        // Windows 11's own caution and critical colours for a dark surface.
        Low: Rgba.FromHex(0xFCE100),
        Critical: Rgba.FromHex(0xFF99A4),
        Charging: Rgba.FromHex(0x97C459),
        Divider: Rgba.FromHex(0xFFFFFF, 0.08),
        Background: Rgba.FromHex(0x2C2C2C));

    public static Palette Light { get; } = new(
        IsLight: true,
        Text: Rgba.FromHex(0x1A1A1A),
        SecondaryText: Rgba.FromHex(0x444441),
        Dimmed: Rgba.FromHex(0x767676),
        Outline: Rgba.FromHex(0x1A1A1A, 0.55),
        // Windows 11's caution and critical colours for a light surface; the cut-out number shows the light taskbar
        // through the fill, and these keep it at 4.5:1 or better against #F3F3F3.
        Low: Rgba.FromHex(0x9D5D00),
        Critical: Rgba.FromHex(0xC42B1C),
        Charging: Rgba.FromHex(0x467318),
        Divider: Rgba.FromHex(0x000000, 0.08),
        Background: Rgba.FromHex(0xF9F9F9));
}

internal static class Theme
{
    /// <summary>
    /// Palette matching the taskbar. The taskbar follows the "Windows mode" setting, not the app mode,
    /// so it can be dark while apps are light.
    /// </summary>
    public static Palette Current()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int light && light != 0 ? Palette.Light : Palette.Dark;
        }
        catch (System.Exception exception) when (exception is System.Security.SecurityException or System.IO.IOException)
        {
            Log.Warn($"Cannot read the theme: {exception.Message}");
            return Palette.Dark;
        }
    }
}
