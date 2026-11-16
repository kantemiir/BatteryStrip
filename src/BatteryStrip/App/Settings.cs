using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace BatteryStrip.App;

/// <summary>
/// User settings in %LOCALAPPDATA%\BatteryStrip\settings.txt, one "key = value" per line.
/// Device names can be overridden there with lines like "name compx:25A7:FA7C = Lamzu Atlantis".
/// Unknown lines are kept as they are. Thread-safe.
/// </summary>
internal sealed class Settings
{
    private const string WarningsKey = "warnings";
    private const string G435Key = "g435";
    private const string UpdatesKey = "updates";
    private const string LanguageKey = "language";
    private const string NamePrefix = "name ";

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    /// <summary>"en   # ru, en, …" → "en". Only for option values: a device name may contain " #" ("Mouse #2").</summary>
    internal static string StripComment(string value)
    {
        int hash = value.IndexOf(" #", StringComparison.Ordinal);
        return hash < 0 ? value : value.Substring(0, hash).TrimEnd();
    }

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
                if (key.StartsWith(NamePrefix, StringComparison.Ordinal)) _names[key.Substring(NamePrefix.Length).Trim()] = value;
                else _values[key] = StripComment(value);
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
        get => GetSwitch(WarningsKey) ?? true;
        set => SetSwitch(WarningsKey, value);
    }

    /// <summary>Read the Logitech G435 through its receiver's service mode. Null: the user has not been asked yet.</summary>
    public bool? G435Reading
    {
        get => GetSwitch(G435Key);
        set => SetSwitch(G435Key, value);
    }

    /// <summary>Check GitHub once a day for a newer version. Null: the user has not been asked yet.</summary>
    public bool? UpdateChecks
    {
        get => GetSwitch(UpdatesKey);
        set => SetSwitch(UpdatesKey, value);
    }

    /// <summary>"ru", "en", or null to follow the language of Windows.</summary>
    public string? Language
    {
        get { lock (_gate) return _values.TryGetValue(LanguageKey, out string? value) && value.Length > 0 ? value : null; }
    }

    public IReadOnlyDictionary<string, string> DeviceNames
    {
        get { lock (_gate) return new Dictionary<string, string>(_names, StringComparer.Ordinal); }
    }

    private bool? GetSwitch(string key)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out string? value)) return null;
            return value switch { "on" => true, "off" => false, _ => null };
        }
    }

    private void SetSwitch(string key, bool? value)
    {
        lock (_gate)
        {
            if (value is bool on) _values[key] = on ? "on" : "off";
            else _values.Remove(key);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var lines = new List<string>
            {
                "# BatteryStrip settings / Настройки программы «Заряд устройств».",
                "# language = ru | en (empty: as Windows / как в Windows).",
                "# name <device id> = Your name for the device / своё название устройства (ids are in the log / id есть в журнале).",
            };
            lines.AddRange(_values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} = {p.Value}"));
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

    /// <summary>Where Task Manager and Settings → Startup apps record that an entry is turned off (odd first byte).</summary>
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
                bool registered = key?.GetValue(AppInfo.Id) is string command && command.IndexOf(AppInfo.ExeName, StringComparison.OrdinalIgnoreCase) >= 0;
                using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                bool turnedOff = approved?.GetValue(AppInfo.Id) is byte[] flags && flags.Length > 0 && (flags[0] & 1) != 0;
                return registered && !turnedOff;
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

            // Turning it on here also clears a "turned off" mark from Task Manager; turning it off leaves no mark behind.
            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(AppInfo.Id, throwOnMissingValue: false);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Cannot change autostart: {exception.Message}");
        }
    }
}
