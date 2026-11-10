using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace BatteryStrip.App;

/// <summary>
/// User settings in %LOCALAPPDATA%\BatteryStrip\settings.txt, one "key = value" per line.
/// Device names can be overridden there with lines like "name compx:25A7:FA7C = Мышь Lamzu Atlantis".
/// Thread-safe.
/// </summary>
internal sealed class Settings
{
    private const string WarningsKey = "warnings";
    private const string NamePrefix = "name ";

    private readonly object _gate = new();
    private readonly string _path;
    private bool _warnings = true;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public Settings(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == WarningsKey) _warnings = value != "off";
                else if (key.StartsWith(NamePrefix, StringComparison.Ordinal)) _names[key.Substring(NamePrefix.Length).Trim()] = value;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cannot read settings: {exception.Message}");
        }
    }

    public static string DefaultPath => Path.Combine(AppInfo.DataDirectory, "settings.txt");

    /// <summary>Show a Windows notification when a device falls to 20 % and to 10 %.</summary>
    public bool Warnings
    {
        get { lock (_gate) return _warnings; }
        set
        {
            lock (_gate)
            {
                _warnings = value;
                Save();
            }
        }
    }

    public IReadOnlyDictionary<string, string> DeviceNames
    {
        get { lock (_gate) return new Dictionary<string, string>(_names, StringComparer.Ordinal); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var lines = new List<string>
            {
                "# Настройки программы «Заряд устройств».",
                "# Своё название устройства: name <id устройства> = Название (id есть в журнале и в отчёте --diagnose).",
                $"{WarningsKey} = {(_warnings ? "on" : "off")}",
            };
            lines.AddRange(_names.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{NamePrefix}{p.Key} = {p.Value}"));
            File.WriteAllLines(_path, lines, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cannot save settings: {exception.Message}");
        }
    }
}

/// <summary>Start with Windows: a value in HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(AppInfo.Id) is string command && command.IndexOf(AppInfo.ExeName, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or IOException)
            {
                return false;
            }
        }
    }

    public static void Set(bool enabled, string exePath)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(AppInfo.Id, $"\"{exePath}\"", RegistryValueKind.String);
            else key.DeleteValue(AppInfo.Id, throwOnMissingValue: false);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Cannot change autostart: {exception.Message}");
        }
    }
}
