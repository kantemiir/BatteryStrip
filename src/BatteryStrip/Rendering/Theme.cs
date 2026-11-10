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
        Text: Rgba.FromHex(0xF2F2F2),
        SecondaryText: Rgba.FromHex(0xC9D1D9),
        Dimmed: Rgba.FromHex(0x5F6B76),
        Outline: Rgba.FromHex(0xF2F2F2, 0.45),
        Low: Rgba.FromHex(0xEF9F27),
        Critical: Rgba.FromHex(0xE24B4A),
        Charging: Rgba.FromHex(0x97C459),
        Divider: Rgba.FromHex(0xFFFFFF, 0.08),
        Background: Rgba.FromHex(0x2C2C2C));

    public static Palette Light { get; } = new(
        IsLight: true,
        Text: Rgba.FromHex(0x1B1B1B),
        SecondaryText: Rgba.FromHex(0x444441),
        Dimmed: Rgba.FromHex(0xA8ADB2),
        Outline: Rgba.FromHex(0x1B1B1B, 0.4),
        Low: Rgba.FromHex(0xBA7517),
        Critical: Rgba.FromHex(0xE24B4A),
        Charging: Rgba.FromHex(0x639922),
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
