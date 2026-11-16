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
    public abstract string WaitingForSilence { get; }
    public abstract string NoAnswer { get; }
    public abstract string NoData { get; }
    public abstract string NoDevices { get; }
    public abstract string NoDevicesHint { get; }
    public abstract string DataFrom(string state, string age);
    public abstract string Updated(TimeSpan age);

    /// <summary>Status of a percent estimated from voltage. The word goes first, so it is not read as "about N minutes ago".</summary>
    public abstract string Estimated(TimeSpan age);

    /// <summary>Short age for one-line statuses: "12 мин назад", "12 min ago".</summary>
    public abstract string Age(TimeSpan age);

    /// <summary>A percentage as written in this language: "20 %" (with a no-break space) in Russian, "20%" in English.</summary>
    public abstract string Percent(int value);

    // Flyout footer and menus.
    public abstract string WarnsAt(int low, int critical);
    public abstract string WarningsOff { get; }
    public abstract string MenuAutostart { get; }
    public abstract string MenuWarnings { get; }
    public abstract string MenuG435 { get; }
    public abstract string MenuUpdates { get; }
    public abstract string MenuRefresh { get; }
    public abstract string MenuOpenLog { get; }
    public abstract string MenuReport { get; }
    public abstract string MenuExit { get; }

    // Device report for testers.
    public abstract string ReportStarted { get; }
    public abstract string ReportStartedBody { get; }
    public abstract string ReportReady(string fileName);
    public abstract string ReportFailed { get; }
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

    /// <summary>Default name of a device of a known brand whose model is unknown, for example "Razer mouse".</summary>
    public abstract string BrandDevice(string brand, Core.DeviceKind kind);

    // Notifications.
    public abstract string LowBattery { get; }
    public abstract string CriticalBattery { get; }
    public abstract string NotificationSample { get; }
    public abstract string UpdateAvailable(string version);
    public abstract string UpdateAvailableBody { get; }

    // Installer and questions.
    public abstract string InstallQuestion(string version);
    public abstract string UpdateQuestion(string installed, string version);
    public abstract string ReinstallQuestion(string version);
    public abstract string DowngradeQuestion(string installed, string version);
    public abstract string UpdateDownloadBroken { get; }
    public abstract string UpdateDownloadFailed { get; }
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
    public override string WaitingForSilence => "Ждёт тишины";
    public override string NoAnswer => "Нет ответа";
    public override string NoData => "нет данных";
    public override string NoDevices => "Устройства не найдены";
    public override string NoDevicesHint => "Шестерёнка → «Сообщить об устройстве»";
    public override string DataFrom(string state, string age) => $"{state} · {age}";
    public override string Updated(TimeSpan age) => age < JustNowLimit ? "Обновлено только что" : $"Обновлено {Age(age)}";
    public override string Estimated(TimeSpan age) => $"Примерный заряд, {Age(age)}";
    public override string Percent(int value) => $"{value}\u00A0%";

    public override string Age(TimeSpan age)
    {
        if (age < JustNowLimit) return "только что";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}\u00A0мин назад";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours}\u00A0ч назад";
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

    public override string WarnsAt(int low, int critical) => $"Предупреждать при {Percent(low)} и {Percent(critical)}";
    public override string WarningsOff => "Предупреждения выключены";
    public override string MenuAutostart => "Запускать вместе с Windows";
    public override string MenuWarnings => "Предупреждать о низком заряде";
    public override string MenuG435 => "Читать заряд G435 (прерывает звук на секунду)";
    public override string MenuUpdates => "Проверять обновления";
    public override string MenuRefresh => "Обновить заряд сейчас";
    public override string MenuOpenLog => "Открыть журнал";
    public override string MenuReport => "Сообщить об устройстве…";
    public override string MenuExit => "Выход";

    public override string ReportStarted => "Собираю отчёт об устройствах";
    public override string ReportStartedBody => "Это займёт меньше минуты. Устройство, о котором хотите сообщить, должно быть включено и подключено.";
    public override string ReportReady(string fileName) =>
        $"Отчёт готов: файл {fileName} выделен в открывшемся окне.\n\nОтправьте его автору программы, например в Telegram, или приложите к заявке на GitHub. Напишите, какое это устройство и какой заряд показывает его родная программа.\n\nВ отчёте коды и названия устройств и то, что программа смогла прочитать. Названия Bluetooth-устройств могут содержать ваше имя, а рядом с ними — их Bluetooth-адреса: просмотрите файл перед отправкой. Сам он никуда не отправляется.";
    public override string ReportFailed => "Не удалось собрать отчёт. Подробности в журнале.";
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
    public override string BrandDevice(string brand, Core.DeviceKind kind) => kind switch
    {
        Core.DeviceKind.Mouse => $"Мышь {brand}",
        Core.DeviceKind.Keyboard => $"Клавиатура {brand}",
        Core.DeviceKind.Headset => $"Наушники {brand}",
        Core.DeviceKind.Gamepad => $"Геймпад {brand}",
        Core.DeviceKind.Speaker => $"Колонка {brand}",
        _ => $"Устройство {brand}",
    };

    public override string LowBattery => "Заряд 20\u00A0% или меньше.";
    public override string CriticalBattery => "Заряд почти закончился, пора зарядить.";
    public override string NotificationSample => "Так будут выглядеть предупреждения о низком заряде.";
    public override string UpdateAvailable(string version) => $"Доступна версия {version}";
    public override string UpdateAvailableBody => "Установить её можно в окне программы: нажмите на полоску, затем на шестерёнку.";

    public override string InstallQuestion(string version) =>
        $"Установить «{AppName}» {version}?\n\nПрограмма покажет заряд беспроводных устройств на панели задач и будет запускаться вместе с Windows. Права администратора не нужны.";
    public override string UpdateQuestion(string installed, string version) =>
        $"«{AppName}» уже установлена (версия {installed}).\n\nЗаменить её версией {version}? Настройки и сохранённые значения останутся.";
    public override string ReinstallQuestion(string version) =>
        $"«{AppName}» {version} уже установлена.\n\nУстановить её заново? Настройки и сохранённые значения останутся.";
    public override string DowngradeQuestion(string installed, string version) =>
        $"Установлена более новая версия «{AppName}» — {installed}.\n\nЗаменить её более старой версией {version}? Настройки и сохранённые значения останутся.";
    public override string UpdateDownloadBroken =>
        "Скачанный файл повреждён или не той версии, поэтому он не запущен.\n\nПопробуйте позже ещё раз или скачайте программу со страницы проекта на GitHub.";
    public override string UpdateDownloadFailed =>
        "Не удалось скачать новую версию. Проверьте интернет и попробуйте ещё раз.";
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
    public override string WaitingForSilence => "Waiting for silence";
    public override string NoAnswer => "No answer";
    public override string NoData => "no data";
    public override string NoDevices => "No devices found";
    public override string NoDevicesHint => "Gear → “Report a device”";
    public override string DataFrom(string state, string age) => $"{state} · {age}";
    public override string Updated(TimeSpan age) => age < JustNowLimit ? "Updated just now" : $"Updated {Age(age)}";
    public override string Estimated(TimeSpan age) => $"Estimated charge, {Age(age)}";
    public override string Percent(int value) => $"{value}%";

    public override string Age(TimeSpan age)
    {
        if (age < JustNowLimit) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}\u00A0min ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours}\u00A0h ago";
        int days = (int)age.TotalDays;
        return $"{days} day{(days == 1 ? "" : "s")} ago";
    }

    public override string WarnsAt(int low, int critical) => $"Warns at {Percent(low)} and {Percent(critical)}";
    public override string WarningsOff => "Warnings are off";
    public override string MenuAutostart => "Start with Windows";
    public override string MenuWarnings => "Low battery warnings";
    public override string MenuG435 => "Read G435 battery (pauses audio for a second)";
    public override string MenuUpdates => "Check for updates";
    public override string MenuRefresh => "Refresh now";
    public override string MenuOpenLog => "Open log";
    public override string MenuReport => "Report a device…";
    public override string MenuExit => "Exit";

    public override string ReportStarted => "Collecting a device report";
    public override string ReportStartedBody => "This takes under a minute. Keep the device you want to report switched on and connected.";
    public override string ReportReady(string fileName) =>
        $"The report is ready: {fileName} is selected in the window that opened.\n\nSend it to the author, for example on Telegram, or attach it to a device request on GitHub. Say which device it is and what charge its own app shows.\n\nThe report holds device codes and names and what the app could read. Bluetooth device names can include your name, and their Bluetooth addresses are listed next to them: look through the file before sending it. It is not sent anywhere by itself.";
    public override string ReportFailed => "Could not collect the report. See the log for details.";
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
    public override string BrandDevice(string brand, Core.DeviceKind kind) => kind switch
    {
        Core.DeviceKind.Mouse => $"{brand} mouse",
        Core.DeviceKind.Keyboard => $"{brand} keyboard",
        Core.DeviceKind.Headset => $"{brand} headset",
        Core.DeviceKind.Gamepad => $"{brand} controller",
        Core.DeviceKind.Speaker => $"{brand} speaker",
        _ => $"{brand} device",
    };

    public override string LowBattery => "20% or less.";
    public override string CriticalBattery => "Almost empty, time to charge.";
    public override string NotificationSample => "This is how low battery warnings will look.";
    public override string UpdateAvailable(string version) => $"Version {version} is available";
    public override string UpdateAvailableBody => "Install it from the app: click the strip, then the gear.";

    public override string InstallQuestion(string version) =>
        $"Install {AppName} {version}?\n\nIt shows the charge of your wireless devices on the taskbar and starts with Windows. No administrator rights needed.";
    public override string UpdateQuestion(string installed, string version) =>
        $"{AppName} {installed} is already installed.\n\nReplace it with version {version}? Settings and remembered values are kept.";
    public override string ReinstallQuestion(string version) =>
        $"{AppName} {version} is already installed.\n\nInstall it again? Settings and remembered values are kept.";
    public override string DowngradeQuestion(string installed, string version) =>
        $"A newer {AppName}, {installed}, is installed.\n\nReplace it with the older version {version}? Settings and remembered values are kept.";
    public override string UpdateDownloadBroken =>
        "The downloaded file is damaged or not the expected version, so it was not run.\n\nTry again later, or download the app from the project's GitHub page.";
    public override string UpdateDownloadFailed =>
        "The new version could not be downloaded. Check the internet connection and try again.";
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
