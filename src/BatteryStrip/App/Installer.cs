using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace BatteryStrip.App;

/// <summary>
/// The program installs itself: for the current user only, without administrator rights, into
/// %LOCALAPPDATA%\Programs\BatteryStrip. It adds a Start menu shortcut, starts with Windows and appears in
/// "Settings → Apps" with an Uninstall button. Running a newer file over an installed copy updates it.
/// </summary>
internal static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.Id;

    public static string InstalledExe => Path.Combine(AppInfo.InstallDirectory, AppInfo.ExeName);

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.DisplayName + ".lnk");

    /// <summary>True when this process runs from the install folder.</summary>
    public static bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(AppInfo.ExePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled => File.Exists(InstalledExe);

    /// <summary>Asks, installs or updates, and starts the installed copy.</summary>
    public static int RunInteractive(Settings settings)
    {
        string? installedVersion = IsInstalled ? FileVersionInfo.GetVersionInfo(InstalledExe).ProductVersion : null;
        string question = installedVersion == null
            ? Text.Current.InstallQuestion(AppInfo.Version)
            : Text.Current.UpdateQuestion(installedVersion, AppInfo.Version);

        if (!Dialogs.Confirm(question)) return 1;

        try
        {
            Install();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Install failed", exception);
            Dialogs.Error(Text.Current.InstallFailed(exception.Message));
            return 2;
        }

        if (AppInfo.HasRepository && settings.UpdateChecks == null) settings.UpdateChecks = Dialogs.Ask(Text.Current.AskUpdates);

        StartInstalled();
        Dialogs.Info(Text.Current.Installed);
        return 0;
    }

    public static void StartInstalled() => Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = false })?.Dispose();

    /// <summary>Copies the exe and registers it. Stops a running copy first.</summary>
    public static void Install()
    {
        StopRunningCopy();
        Directory.CreateDirectory(AppInfo.InstallDirectory);

        // Copy to a temporary name first, so a failure never leaves a half-written exe.
        string temporary = InstalledExe + ".new";
        File.Copy(AppInfo.ExePath, temporary, overwrite: true);
        if (File.Exists(InstalledExe)) File.Replace(temporary, InstalledExe, null);
        else File.Move(temporary, InstalledExe);

        Shortcut.Create(ShortcutPath, InstalledExe, AppInfo.DisplayName, AppInfo.Id);
        Autostart.Set(true, InstalledExe);
        RegisterUninstall();
        Log.Info($"Installed {AppInfo.Version} to {AppInfo.InstallDirectory}.");
    }

    /// <summary>Called from "Settings → Apps → Uninstall". Removes everything the program created.</summary>
    public static int Uninstall(bool quiet)
    {
        if (!quiet && !Dialogs.Confirm(Text.Current.UninstallQuestion)) return 1;

        StopRunningCopy();
        Autostart.Set(false, InstalledExe);
        Notifier.Unregister();
        TryDelete(() => File.Delete(ShortcutPath));
        TryDelete(() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false));
        RemoveTrayIconSettings();

        // Logging stops here: its folder is about to go away.
        Log.Start(Path.GetTempPath());
        TryDelete(() => Directory.Delete(AppInfo.DataDirectory, recursive: true));

        // A running exe cannot delete itself; a short-lived command does it once this process has exited.
        if (IsRunningInstalled) ScheduleFolderRemoval(AppInfo.InstallDirectory);
        else TryDelete(() => Directory.Delete(AppInfo.InstallDirectory, recursive: true));

        if (!quiet) Dialogs.Info(Text.Current.Uninstalled);
        return 0;
    }

    /// <summary>Asks a running copy to exit and waits a few seconds for it.</summary>
    private static void StopRunningCopy()
    {
        if (!EventWaitHandle.TryOpenExisting(AppHost.ExitEventName, out EventWaitHandle? exitEvent)) return;
        using (exitEvent) exitEvent.Set();

        int self = Process.GetCurrentProcess().Id;
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            bool running = false;
            foreach (Process process in Process.GetProcessesByName(AppInfo.Id))
            {
                using (process) running |= process.Id != self;
            }
            if (!running) return;
            Thread.Sleep(200);
        }
        Log.Warn("The running copy did not exit in time.");
    }

    private static void RegisterUninstall()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppInfo.DisplayName);
        key.SetValue("DisplayVersion", AppInfo.Version);
        key.SetValue("Publisher", AppInfo.DisplayName);
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("InstallLocation", AppInfo.InstallDirectory);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }

    /// <summary>Forgets the "always show" settings Windows keeps for the fallback tray icons.</summary>
    private static void RemoveTrayIconSettings()
    {
        TryDelete(() =>
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", writable: true);
            if (root == null) return;
            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? entry = root.OpenSubKey(name);
                bool ours = entry?.GetValue("ExecutablePath") is string path && path.EndsWith(@"\" + AppInfo.ExeName, StringComparison.OrdinalIgnoreCase);
                if (ours) root.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
        });
    }

    private static void ScheduleFolderRemoval(string folder)
    {
        var command = new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rd /s /q \"{folder}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        };
        Process.Start(command)?.Dispose();
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($"Cleanup step failed: {exception.Message}");
        }
    }
}

/// <summary>Start menu shortcut with the application id, so Windows ties notifications to it.</summary>
internal static class Shortcut
{
    public static void Create(string path, string target, string description, string appUserModelId)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);

            var store = (IPropertyStore)link;
            var key = new PropertyKey { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };
            var value = new PropVariant { Type = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(appUserModelId) };
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally
            {
                Marshal.FreeCoTaskMem(value.Pointer);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int max, IntPtr findData, int flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription(IntPtr name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(IntPtr args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation(IntPtr iconPath, int max, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, int mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile(out IntPtr file);
    }
}

/// <summary>Plain Windows message boxes in the program's name.</summary>
internal static class Dialogs
{
    public static bool Confirm(string text) =>
        MessageBox(IntPtr.Zero, text, AppInfo.DisplayName, MB_OKCANCEL | MB_ICONQUESTION | MB_SETFOREGROUND) == IDOK;

    /// <summary>A yes/no question; true for "Yes".</summary>
    public static bool Ask(string text) =>
        MessageBox(IntPtr.Zero, text, AppInfo.DisplayName, MB_YESNO | MB_ICONQUESTION | MB_SETFOREGROUND | MB_TOPMOST) == IDYES;

    public static void Info(string text) => MessageBox(IntPtr.Zero, text, AppInfo.DisplayName, MB_ICONINFORMATION | MB_SETFOREGROUND);

    public static void Error(string text) => MessageBox(IntPtr.Zero, text, AppInfo.DisplayName, MB_ICONERROR | MB_SETFOREGROUND);

    private const int MB_OKCANCEL = 0x1, MB_YESNO = 0x4, MB_ICONERROR = 0x10, MB_ICONQUESTION = 0x20, MB_ICONINFORMATION = 0x40;
    private const int MB_SETFOREGROUND = 0x10000, MB_TOPMOST = 0x40000, IDOK = 1, IDYES = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, int type);
}
