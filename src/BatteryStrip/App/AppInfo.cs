using System;
using System.IO;
using System.Reflection;

namespace BatteryStrip.App;

/// <summary>Names, identifiers and folders of the program.</summary>
internal static class AppInfo
{
    /// <summary>Name shown to the user: Start menu, installed apps list, notifications.</summary>
    public const string DisplayName = "Заряд устройств";

    /// <summary>Technical name: file, folders, registry keys, application user model id.</summary>
    public const string Id = "BatteryStrip";

    public const string ExeName = Id + ".exe";

    public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

    /// <summary>Settings, remembered charges and logs: %LOCALAPPDATA%\BatteryStrip.</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Id);

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Where the installer puts the program: %LOCALAPPDATA%\Programs\BatteryStrip.</summary>
    public static string InstallDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Id);

    public static string ExePath { get; } = Assembly.GetExecutingAssembly().Location;
}
