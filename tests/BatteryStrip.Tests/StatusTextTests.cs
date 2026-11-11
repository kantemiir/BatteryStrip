using System;
using System.Globalization;
using BatteryStrip.App;
using BatteryStrip.Core;
using Xunit;

namespace BatteryStrip.Tests;

public class StatusTextTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static DeviceSnapshot Device(int? percent = 80, bool charging = false, bool asleep = false, bool estimate = false, double minutesAgo = 0, string? note = null) =>
        new("id", "Мышь", DeviceKind.Mouse, percent, charging, asleep, estimate, ChargeLevel.Normal, Now.AddMinutes(-minutesAgo), note);

    private static string Ru(DeviceSnapshot device) => StatusText.For(device, Now, Strings.Russian);

    private static string En(DeviceSnapshot device) => StatusText.For(device, Now, Strings.English);

    [Theory]
    [InlineData(1, "1 минуту")]
    [InlineData(2, "2 минуты")]
    [InlineData(5, "5 минут")]
    [InlineData(11, "11 минут")]
    [InlineData(12, "12 минут")]
    [InlineData(21, "21 минуту")]
    [InlineData(24, "24 минуты")]
    [InlineData(111, "111 минут")]
    public void Russian_plural_forms(int n, string expected)
    {
        Assert.Equal(expected, RussianStrings.Count(n, "минуту", "минуты", "минут"));
    }

    [Fact]
    public void Fresh_reading()
    {
        Assert.Equal("Обновлено только что", Ru(Device(minutesAgo: 1)));
        Assert.Equal("Updated just now", En(Device(minutesAgo: 1)));
    }

    [Fact]
    public void Older_reading_shows_its_age()
    {
        Assert.Equal("Обновлено 12 минут назад", Ru(Device(minutesAgo: 12)));
        Assert.Equal("Обновлено 2 часа назад", Ru(Device(minutesAgo: 125)));
        Assert.Equal("Updated 12 minutes ago", En(Device(minutesAgo: 12)));
        Assert.Equal("Updated 1 hour ago", En(Device(minutesAgo: 61)));
    }

    [Fact]
    public void Estimated_value_is_marked()
    {
        Assert.Equal("Обновлено 5 минут назад, примерно", Ru(Device(estimate: true, minutesAgo: 5)));
        Assert.Equal("Updated 5 minutes ago, approximate", En(Device(estimate: true, minutesAgo: 5)));
    }

    [Fact]
    public void Charging_asleep_and_no_data()
    {
        Assert.Equal("Заряжается", Ru(Device(charging: true)));
        Assert.Equal("Спит, данные 2 часа назад", Ru(Device(asleep: true, minutesAgo: 120)));
        Assert.Equal("Спит, данные 3 дня назад", Ru(Device(asleep: true, minutesAgo: 3 * 24 * 60)));
        Assert.Equal("Не передаёт заряд", Ru(Device(percent: null)));
        Assert.Equal("Asleep, read 3 days ago", En(Device(asleep: true, minutesAgo: 3 * 24 * 60)));
        Assert.Equal("Waiting for silence, read 4 hours ago", En(Device(asleep: true, minutesAgo: 240, note: Strings.English.WaitingForSilence)));
    }

    [Theory]
    [InlineData("ru-RU", "Заряд устройств")]
    [InlineData("be-BY", "Заряд устройств")]
    [InlineData("en-US", "BatteryStrip")]
    [InlineData("de-DE", "BatteryStrip")]
    [InlineData("uk-UA", "BatteryStrip")]
    public void Language_follows_windows(string culture, string appName)
    {
        Assert.Equal(appName, Strings.For(new CultureInfo(culture)).AppName);
    }

    [Fact]
    public void Language_can_be_forced_in_settings()
    {
        Assert.Same(Strings.English, Strings.ForCode("en"));
        Assert.Same(Strings.Russian, Strings.ForCode(" RU "));
    }
}
