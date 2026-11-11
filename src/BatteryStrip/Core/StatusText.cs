using System;

namespace BatteryStrip.Core;

/// <summary>The one-line status under a device name in the flyout.</summary>
internal static class StatusText
{
    public static string For(DeviceSnapshot device, DateTime utcNow)
    {
        if (device.Percent == null) return "Не передаёт заряд";

        string age = device.UpdatedUtc is DateTime updated ? Age(utcNow - updated) : "давно";
        if (device.IsAsleep) return $"{device.Note ?? "Спит"}, данные {age}";
        if (device.IsCharging) return "Заряжается";

        string text = age == JustNow ? "Обновлено только что" : $"Обновлено {age}";
        return device.IsEstimate ? text + ", примерно" : text;
    }

    private const string JustNow = "только что";

    /// <summary>"только что", "5 минут назад", "2 часа назад", "3 дня назад".</summary>
    internal static string Age(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromMinutes(2)) return JustNow;
        if (elapsed < TimeSpan.FromHours(1)) return $"{Count((int)elapsed.TotalMinutes, "минуту", "минуты", "минут")} назад";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Count((int)elapsed.TotalHours, "час", "часа", "часов")} назад";
        return $"{Count((int)elapsed.TotalDays, "день", "дня", "дней")} назад";
    }

    /// <summary>Russian plural: 1 минуту, 2 минуты, 5 минут, 11 минут, 21 минуту.</summary>
    internal static string Count(int n, string one, string few, string many)
    {
        int lastTwo = n % 100, last = n % 10;
        string word = lastTwo is >= 11 and <= 14 ? many
            : last == 1 ? one
            : last is >= 2 and <= 4 ? few
            : many;
        return $"{n} {word}";
    }
}
