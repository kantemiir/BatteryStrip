using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;

// Windows DLLs that P/Invoke loads (hid.dll, setupapi.dll and others) come from System32 only, never from the
// folder of the exe: the installer often runs from Downloads, where another file could be planted next to it.
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]

namespace BatteryStrip;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // The same for DLLs that Windows itself loads later on behalf of the program.
        SetDefaultDllDirectories(LoadLibrarySearchSystem32);
        Log.Start(AppInfo.LogDirectory);
        try
        {
            var settings = new Settings(Settings.DefaultPath);
            int lang = Array.IndexOf(args, "--lang");
            Text.Current = Strings.ForCode(lang >= 0 && lang + 1 < args.Length ? args[lang + 1] : settings.Language);

            if (args.Contains("--diagnose"))
            {
                int seconds = args.SkipWhile(a => a != "--seconds").Skip(1).Select(int.Parse).DefaultIfEmpty(40).First();
                return Diagnose.Run(TimeSpan.FromSeconds(seconds), openReport: !args.Contains("--quiet"));
            }
            int preview = Array.IndexOf(args, "--preview");
            if (preview >= 0 && preview + 1 < args.Length) return Preview.Run(args[preview + 1]);
            int flyoutPreview = Array.IndexOf(args, "--preview-flyout");
            if (flyoutPreview >= 0 && flyoutPreview + 1 < args.Length) return Preview.RunFlyout(args[flyoutPreview + 1]);
            if (args.Contains("--exit")) return SignalExit();
            int icon = Array.IndexOf(args, "--make-icon");
            if (icon >= 0 && icon + 1 < args.Length)
            {
                AppIcon.SaveIco(args[icon + 1]);
                return 0;
            }
            if (args.Contains("--test-notification"))
            {
                Notifier.Register();
                Notifier.Show(AppInfo.DisplayName, Text.Current.NotificationSample);
                Thread.Sleep(1000);
                return 0;
            }
            if (args.Contains("--uninstall")) return Installer.Uninstall(quiet: args.Contains("--quiet"));
            if (args.Contains("--install") && args.Contains("--quiet"))
            {
                Installer.Install();
                if (args.Contains("--start")) Installer.StartInstalled();
                return 0;
            }

            // Started from anywhere but the install folder (a downloaded file): offer to install.
            // --run skips that, for running a development build in place.
            if (!Installer.IsRunningInstalled && !args.Contains("--run")) return Installer.RunInteractive(settings);
            return RunApp(settings, forceTray: args.Contains("--tray"));
        }
        catch (Exception exception)
        {
            Log.Error("Fatal error", exception);
            return 1;
        }
    }

    private static int RunApp(Settings settings, bool forceTray)
    {
        using var single = new Mutex(initiallyOwned: true, @"Local\BatteryStrip.Running", out bool first);
        if (!first)
        {
            Log.Info("Another copy is already running.");
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", (Exception)e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        using var host = new AppHost(settings, forceTray);
        host.Run();
        return 0;
    }

    private const int LoadLibrarySearchSystem32 = 0x800;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetDefaultDllDirectories(int flags);

    /// <summary>Asks a running copy to exit, for example before an update or uninstall.</summary>
    private static int SignalExit()
    {
        if (!EventWaitHandle.TryOpenExisting(AppHost.ExitEventName, out EventWaitHandle? exitEvent)) return 0;
        using (exitEvent) exitEvent.Set();
        return 0;
    }
}
