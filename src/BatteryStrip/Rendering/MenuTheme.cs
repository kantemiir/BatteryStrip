using System;
using System.Runtime.InteropServices;
using BatteryStrip.App;
using Microsoft.Win32;

namespace BatteryStrip.Rendering;

/// <summary>
/// Makes the program's pop-up menus dark or light to match the flyout and the taskbar. Windows has no public
/// switch for this; uxtheme's SetPreferredAppMode and FlushMenuThemes (exported by ordinal since Windows 10
/// 1903, used by Explorer and many desktop programs) do it. On older builds the menus simply keep the default look.
/// </summary>
internal static class MenuTheme
{
    private const int MinimumBuild = 18362;
    private const int ForceDark = 2, ForceLight = 3;

    private delegate int SetPreferredAppModeFn(int mode);
    private delegate void FlushMenuThemesFn();

    private static readonly Lazy<(SetPreferredAppModeFn Set, FlushMenuThemesFn Flush)?> Api = new(Load);
    private static int _applied = -1;

    /// <summary>Call on the thread that shows the menu, right before showing it. Cheap when nothing changes.</summary>
    public static void Apply(bool dark)
    {
        if (Api.Value is not { } api) return;
        var (set, flush) = api;
        int mode = dark ? ForceDark : ForceLight;
        if (System.Threading.Interlocked.Exchange(ref _applied, mode) == mode) return;
        try
        {
            set(mode);
            flush();
        }
        catch (Exception exception) when (exception is SEHException or EntryPointNotFoundException)
        {
            Log.Warn($"Menu theme unavailable: {exception.Message}");
        }
    }

    private static (SetPreferredAppModeFn, FlushMenuThemesFn)? Load()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (!int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out int build) || build < MinimumBuild) return null;

            IntPtr module = LoadLibraryEx("uxtheme.dll", IntPtr.Zero, LoadLibrarySearchSystem32);
            if (module == IntPtr.Zero) return null;
            IntPtr setMode = GetProcAddress(module, new IntPtr(135));
            IntPtr flushMenus = GetProcAddress(module, new IntPtr(136));
            if (setMode == IntPtr.Zero || flushMenus == IntPtr.Zero) return null;
            return (Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeFn>(setMode),
                    Marshal.GetDelegateForFunctionPointer<FlushMenuThemesFn>(flushMenus));
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return null;
        }
    }

    private const int LoadLibrarySearchSystem32 = 0x800;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string name, IntPtr reserved, int flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);
}
