using BatteryStrip.Taskbar;
using Xunit;

namespace BatteryStrip.Tests;

public class StripLayoutTests
{
    // The user's taskbar: tray at 1640, strip ends 8 px before it, app buttons end at 1293.
    private const int Right = 1632, Margin = 8;

    [Fact]
    public void Plenty_of_room_shows_the_full_strip()
    {
        Assert.Equal(StripFit.Full, StripLayout.Choose(Right, 1293, Margin, 122, 74));
    }

    [Fact]
    public void A_crowded_taskbar_drops_the_icons_first()
    {
        Assert.Equal(StripFit.Compact, StripLayout.Choose(Right, 1530, Margin, 122, 74));
    }

    [Fact]
    public void Without_room_for_batteries_the_tray_takes_over()
    {
        Assert.Equal(StripFit.None, StripLayout.Choose(Right, 1580, Margin, 122, 74));
    }

    [Fact]
    public void Unknown_app_buttons_assume_room()
    {
        Assert.Equal(StripFit.Full, StripLayout.Choose(Right, null, Margin, 500, 300));
    }

    [Fact]
    public void Exactly_enough_room_still_fits()
    {
        Assert.Equal(StripFit.Full, StripLayout.Choose(Right, Right - Margin - 122, Margin, 122, 74));
    }
}
