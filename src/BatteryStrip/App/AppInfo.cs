using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BatteryStrip.App;

/// <summary>Names, identifiers and folders of the program.</summary>
internal static class AppInfo
{
    /// <summary>Name shown to the user: Start menu, installed apps list, notifications. Depends on the language.</summary>
    public static string DisplayName => Text.Current.AppName;

    /// <summary>Technical name: file, folders, registry keys, application user model id.</summary>
    public const string Id = "BatteryStrip";

    public const string ExeName = Id + ".exe";

    /// <summary>The release version, with its pre-release tag: "0.3.0-beta.1".</summary>
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

    /// <summary>
    /// GitHub repository of the project ("owner/name"). Release builds get it from the build server;
    /// local builds have none, so they never look for updates.
    /// </summary>
    public static string Repository { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "Repository")?.Value ?? "";

    public static bool HasRepository => Repository.Length > 0;

    /// <summary>
    /// The public feed of releases. Unlike the GitHub API, which allows 60 anonymous requests an hour per IP
    /// address and so fails for everyone behind a busy VPN or NAT, the feed has no such limit.
    /// </summary>
    public static string ReleasesFeed => $"https://github.com/{Repository}/releases.atom";

    /// <summary>Settings, remembered charges and logs: %LOCALAPPDATA%\BatteryStrip.</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Id);

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Where the installer puts the program: %LOCALAPPDATA%\Programs\BatteryStrip.</summary>
    public static string InstallDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Id);

    public static string ExePath { get; } = Assembly.GetExecutingAssembly().Location;
}
