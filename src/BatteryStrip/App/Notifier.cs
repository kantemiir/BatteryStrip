using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using BatteryStrip.Core;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BatteryStrip.App;

/// <summary>
/// Windows notifications about a low battery. The program is not packaged, so it registers its
/// application id under HKCU\Software\Classes\AppUserModelId; Windows then shows the program's name and icon.
/// </summary>
internal static class Notifier
{
    private const string RegistryPath = @"Software\Classes\AppUserModelId\" + AppInfo.Id;

    /// <summary>Registers the application id. Cheap and idempotent; called at every start.</summary>
    public static void Register()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppInfo.Id);

            string iconPath = Path.Combine(AppInfo.DataDirectory, "icon.png");
            if (!File.Exists(iconPath))
            {
                Directory.CreateDirectory(AppInfo.DataDirectory);
                AppIcon.SavePng(iconPath, 96);
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue("DisplayName", AppInfo.DisplayName);
            key.SetValue("IconUri", iconPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ExternalException)
        {
            Log.Warn($"Cannot register for notifications: {exception.Message}");
        }
    }

    /// <summary>Removes the registration (used by the uninstaller).</summary>
    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            Log.Warn($"Cannot remove the notification registration: {exception.Message}");
        }
    }

    public static void LowBattery(DeviceSnapshot device, int threshold)
    {
        string title = device.Percent is int percent ? $"{device.Name}: {Text.Current.Percent(percent)}" : device.Name;
        string body = threshold <= ChargeLevels.CriticalThreshold ? Text.Current.CriticalBattery : Text.Current.LowBattery;
        Show(title, body, TagFor(device.Id), TimeSpan.FromHours(2));
    }

    /// <summary>Shows a toast. With a tag, a newer toast replaces the older one of the same device in Action Center.</summary>
    /// <param name="lifetime">After this long the toast leaves Action Center: an old battery warning is only noise.</param>
    public static void Show(string title, string body, string? tag = null, TimeSpan? lifetime = null)
    {
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml("<toast><visual><binding template=\"ToastGeneric\"><text></text><text></text></binding></visual></toast>");
            XmlNodeList texts = xml.GetElementsByTagName("text");
            texts[0].AppendChild(xml.CreateTextNode(title));
            texts[1].AppendChild(xml.CreateTextNode(body));
            var toast = new ToastNotification(xml);
            if (tag != null)
            {
                toast.Tag = tag;
                toast.Group = "battery";
            }
            if (lifetime is TimeSpan life) toast.ExpirationTime = DateTimeOffset.Now + life;
            ToastNotificationManager.CreateToastNotifier(AppInfo.Id).Show(toast);
            Log.Info($"Notification: {title} — {body}");
        }
        catch (Exception exception)
        {
            Log.Error("Notification failed", exception);
        }
    }

    /// <summary>A short stable tag per device (toast tags are limited in length).</summary>
    private static string TagFor(string deviceId)
    {
        uint hash = 2166136261;
        foreach (char c in deviceId) hash = (hash ^ c) * 16777619;
        return hash.ToString("X8");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);
}
