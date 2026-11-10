using System;
using BatteryStrip.Core;
using Xunit;

namespace BatteryStrip.Tests;

public class StatusTextTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static DeviceSnapshot Device(int? percent = 80, bool charging = false, bool asleep = false, bool estimate = false, double minutesAgo = 0) =>
        new("id", "Мышь", DeviceKind.Mouse, percent, charging, asleep, estimate, ChargeLevel.Normal, Now.AddMinutes(-minutesAgo));

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
        Assert.Equal(expected, StatusText.Count(n, "минуту", "минуты", "минут"));
    }

    [Fact]
    public void Fresh_reading()
    {
        Assert.Equal("Обновлено только что", StatusText.For(Device(minutesAgo: 1), Now));
    }

    [Fact]
    public void Older_reading_shows_its_age()
    {
        Assert.Equal("Обновлено 12 минут назад", StatusText.For(Device(minutesAgo: 12), Now));
        Assert.Equal("Обновлено 2 часа назад", StatusText.For(Device(minutesAgo: 125), Now));
    }

    [Fact]
    public void Estimated_value_is_marked()
    {
        Assert.Equal("Обновлено 5 минут назад, примерно", StatusText.For(Device(estimate: true, minutesAgo: 5), Now));
    }

    [Fact]
    public void Charging_asleep_and_no_data()
    {
        Assert.Equal("Заряжается", StatusText.For(Device(charging: true), Now));
        Assert.Equal("Спит, данные 2 часа назад", StatusText.For(Device(asleep: true, minutesAgo: 120), Now));
        Assert.Equal("Спит, данные 3 дня назад", StatusText.For(Device(asleep: true, minutesAgo: 3 * 24 * 60), Now));
        Assert.Equal("Не передаёт заряд", StatusText.For(Device(percent: null), Now));
    }
}
