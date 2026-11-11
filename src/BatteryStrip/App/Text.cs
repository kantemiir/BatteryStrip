using System;
using System.Globalization;

namespace BatteryStrip.App;

/// <summary>Every text the user sees, in Russian and English.</summary>
internal abstract class Strings
{
    public static Strings Russian { get; } = new RussianStrings();
    public static Strings English { get; } = new EnglishStrings();

    public abstract string AppName { get; }

    // Flyout and tray: device state.
    public abstract string NoCharge { get; }
    public abstract string Asleep { get; }
    public abstract string Charging { get; }
    public abstract string Approximate { get; }
    public abstract string WaitingForSilence { get; }
    public abstract string NoAnswer { get; }
    public abstract string NoData { get; }
    public abstract string NoDevices { get; }
    public abstract string DataFrom(string state, string age);
    public abstract string Updated(TimeSpan age);
    public abstract string Age(TimeSpan age);

    // Flyout footer and menus.
    public abstract string WarnsAt(int low, int critical);
    public abstract string WarningsOff { get; }
    public abstract string MenuAutostart { get; }
    public abstract string MenuWarnings { get; }
    public abstract string MenuG435 { get; }
    public abstract string MenuUpdates { get; }
    public abstract string MenuRefresh { get; }
    public abstract string MenuOpenLog { get; }
    public abstract string MenuExit { get; }
    public abstract string MenuInstallUpdate(string version);

    // Default device names.
    public abstract string LamzuMouse { get; }
    public abstract string Mouse { get; }
    public abstract string Keyboard { get; }
    public abstract string Headset { get; }
    public abstract string Gamepad { get; }
    public abstract string Device { get; }
    public abstract string BluetoothDevice { get; }
    public abstract string LogitechDevice(Core.DeviceKind kind);

    // Notifications.
    public abstract string LowBattery { get; }
    public abstract string CriticalBattery { get; }
    public abstract string NotificationSample { get; }
    public abstract string UpdateAvailable(string version);
    public abstract string UpdateAvailableBody { get; }

    // Installer and questions.
    public abstract string InstallQuestion(string version);
    public abstract string UpdateQuestion(string installed, string version);
    public abstract string InstallFailed(string reason);
    public abstract string Installed { get; }
    public abstract string UninstallQuestion { get; }
    public abstract string Uninstalled { get; }
    public abstract string AskUpdates { get; }
    public abstract string AskG435 { get; }
    public abstract string UpdateFailed(string reason);

    /// <summary>Russian for Russian, Belarusian and Kazakh Windows; English for everything else.</summary>
    public static Strings For(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName is "ru" or "be" or "kk" ? Russian : English;

    public static Strings ForCode(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "ru" => Russian,
        "en" => English,
        _ => For(CultureInfo.CurrentUICulture),
    };
}

/// <summary>The language chosen at startup.</summary>
internal static class Text
{
    public static Strings Current { get; set; } = Strings.For(CultureInfo.CurrentUICulture);
}

internal sealed class RussianStrings : Strings
{
    public override string AppName => "Заряд устройств";

    public override string NoCharge => "Не передаёт заряд";
    public override string Asleep => "Спит";
    public override string Charging => "Заряжается";
    public override string Approximate => ", примерно";
    public override string WaitingForSilence => "Ждёт тишины в наушниках";
    public override string NoAnswer => "Нет ответа";
    public override string NoData => "нет данных";
    public override string NoDevices => "Устройства с известным зарядом не найдены";
    public override string DataFrom(string state, string age) => $"{state}, данные {age}";
    public override string Updated(TimeSpan age) => age < JustNowLimit ? "Обновлено только что" : $"Обновлено {Age(age)}";

    public override string Age(TimeSpan age)
    {
        if (age < JustNowLimit) return "только что";
        if (age < TimeSpan.FromHours(1)) return $"{Count((int)age.TotalMinutes, "минуту", "минуты", "минут")} назад";
        if (age < TimeSpan.FromDays(1)) return $"{Count((int)age.TotalHours, "час", "часа", "часов")} назад";
        return $"{Count((int)age.TotalDays, "день", "дня", "дней")} назад";
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

    public override string WarnsAt(int low, int critical) => $"Предупреждать при {low}% и {critical}%";
    public override string WarningsOff => "Предупреждения выключены";
    public override string MenuAutostart => "Запускать вместе с Windows";
    public override string MenuWarnings => "Предупреждать о низком заряде";
    public override string MenuG435 => "Читать заряд Logitech G435 (звук прерывается на секунду)";
    public override string MenuUpdates => "Проверять обновления";
    public override string MenuRefresh => "Обновить заряд сейчас";
    public override string MenuOpenLog => "Открыть журнал";
    public override string MenuExit => "Выход";
    public override string MenuInstallUpdate(string version) => $"Установить версию {version}";

    public override string LamzuMouse => "Мышь Lamzu";
    public override string Mouse => "Мышь";
    public override string Keyboard => "Клавиатура";
    public override string Headset => "Наушники";
    public override string Gamepad => "Геймпад";
    public override string Device => "Устройство";
    public override string BluetoothDevice => "Bluetooth-устройство";
    public override string LogitechDevice(Core.DeviceKind kind) => kind switch
    {
        Core.DeviceKind.Mouse => "Мышь Logitech",
        Core.DeviceKind.Keyboard => "Клавиатура Logitech",
        Core.DeviceKind.Headset => "Наушники Logitech",
        Core.DeviceKind.Gamepad => "Геймпад Logitech",
        _ => "Устройство Logitech",
    };

    public override string LowBattery => "Заряд меньше 20 %.";
    public override string CriticalBattery => "Заряд почти закончился, пора зарядить.";
    public override string NotificationSample => "Так будут выглядеть предупреждения о низком заряде.";
    public override string UpdateAvailable(string version) => $"Доступна версия {version}";
    public override string UpdateAvailableBody => "Установить её можно в окне программы: нажмите на полоску, затем на шестерёнку.";

    public override string InstallQuestion(string version) =>
        $"Установить «{AppName}» {version}?\n\nПрограмма покажет заряд беспроводных устройств на панели задач и будет запускаться вместе с Windows. Права администратора не нужны.";
    public override string UpdateQuestion(string installed, string version) =>
        $"«{AppName}» уже установлена (версия {installed}).\n\nЗаменить её версией {version}? Настройки и сохранённые значения останутся.";
    public override string InstallFailed(string reason) => $"Не удалось установить программу:\n{reason}";
    public override string Installed =>
        $"Готово. «{AppName}» установлена и запущена.\n\nПолоска появится слева от значков в правом углу панели задач, когда программа прочитает заряд первого устройства.";
    public override string UninstallQuestion => $"Удалить «{AppName}»?\n\nПрограмма, её настройки и журнал будут удалены.";
    public override string Uninstalled => $"«{AppName}» удалена.";
    public override string AskUpdates =>
        "Проверять раз в день, не вышла ли новая версия?\n\nПрограмма будет спрашивать GitHub, какая версия последняя, и больше ничего никуда не отправит. Изменить это можно в настройках (шестерёнка в окне программы).";
    public override string AskG435 =>
        "Найдены наушники Logitech G435.\n\nУ них нет обычного способа сообщить заряд. Программа может читать его так же, как утилита обновления Logitech: приёмник на секунду переходит в служебный режим, и звук в наушниках на это время прерывается. Программа делает это не чаще раза в 30 минут и только в паузах звука.\n\nВключить чтение заряда G435?";
    public override string UpdateFailed(string reason) => $"Не удалось обновить программу:\n{reason}";

    private static readonly TimeSpan JustNowLimit = TimeSpan.FromMinutes(2);
}

internal sealed class EnglishStrings : Strings
{
    public override string AppName => "BatteryStrip";

    public override string NoCharge => "Doesn't report its charge";
    public override string Asleep => "Asleep";
    public override string Charging => "Charging";
    public override string Approximate => ", approximate";
    public override string WaitingForSilence => "Waiting for silence";
    public override string NoAnswer => "No answer";
    public override string NoData => "no data";
    public override string NoDevices => "No devices with a known charge";
    public override string DataFrom(string state, string age) => $"{state}, read {age}";
    public override string Updated(TimeSpan age) => age < JustNowLimit ? "Updated just now" : $"Updated {Age(age)}";

    public override string Age(TimeSpan age)
    {
        if (age < JustNowLimit) return "just now";
        if (age < TimeSpan.FromHours(1)) return Plural((int)age.TotalMinutes, "minute");
        if (age < TimeSpan.FromDays(1)) return Plural((int)age.TotalHours, "hour");
        return Plural((int)age.TotalDays, "day");
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";

    public override string WarnsAt(int low, int critical) => $"Warns at {low}% and {critical}%";
    public override string WarningsOff => "Warnings are off";
    public override string MenuAutostart => "Start with Windows";
    public override string MenuWarnings => "Low battery warnings";
    public override string MenuG435 => "Read Logitech G435 battery (pauses audio for a second)";
    public override string MenuUpdates => "Check for updates";
    public override string MenuRefresh => "Refresh now";
    public override string MenuOpenLog => "Open log";
    public override string MenuExit => "Exit";
    public override string MenuInstallUpdate(string version) => $"Install version {version}";

    public override string LamzuMouse => "Lamzu mouse";
    public override string Mouse => "Mouse";
    public override string Keyboard => "Keyboard";
    public override string Headset => "Headset";
    public override string Gamepad => "Controller";
    public override string Device => "Device";
    public override string BluetoothDevice => "Bluetooth device";
    public override string LogitechDevice(Core.DeviceKind kind) => kind switch
    {
        Core.DeviceKind.Mouse => "Logitech mouse",
        Core.DeviceKind.Keyboard => "Logitech keyboard",
        Core.DeviceKind.Headset => "Logitech headset",
        Core.DeviceKind.Gamepad => "Logitech controller",
        _ => "Logitech device",
    };

    public override string LowBattery => "Below 20%.";
    public override string CriticalBattery => "Almost empty, time to charge.";
    public override string NotificationSample => "This is how low battery warnings will look.";
    public override string UpdateAvailable(string version) => $"Version {version} is available";
    public override string UpdateAvailableBody => "Install it from the app: click the strip, then the gear.";

    public override string InstallQuestion(string version) =>
        $"Install {AppName} {version}?\n\nIt shows the charge of your wireless devices on the taskbar and starts with Windows. No administrator rights needed.";
    public override string UpdateQuestion(string installed, string version) =>
        $"{AppName} {installed} is already installed.\n\nReplace it with version {version}? Settings and remembered values are kept.";
    public override string InstallFailed(string reason) => $"Installation failed:\n{reason}";
    public override string Installed =>
        $"Done. {AppName} is installed and running.\n\nThe strip appears left of the icons in the right corner of the taskbar once the first device reports its charge.";
    public override string UninstallQuestion => $"Uninstall {AppName}?\n\nThe app, its settings and its log will be removed.";
    public override string Uninstalled => $"{AppName} has been uninstalled.";
    public override string AskUpdates =>
        "Check once a day whether a new version is out?\n\nThe app will ask GitHub for the latest version number and send nothing else anywhere. You can change this in the settings (the gear in the app's window).";
    public override string AskG435 =>
        "A Logitech G435 headset was found.\n\nIt has no regular way to report its charge. The app can read it the way Logitech's firmware updater does: the receiver switches to service mode for a second, and the headset's audio pauses meanwhile. The app does this at most every 30 minutes and only during silence.\n\nTurn on G435 battery reading?";
    public override string UpdateFailed(string reason) => $"The update failed:\n{reason}";

    private static readonly TimeSpan JustNowLimit = TimeSpan.FromMinutes(2);
}
