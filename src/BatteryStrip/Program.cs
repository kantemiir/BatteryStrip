using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;

namespace BatteryStrip;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Log.Start(AppInfo.LogDirectory);
        try
        {
            if (args.Contains("--diagnose"))
            {
                int seconds = args.SkipWhile(a => a != "--seconds").Skip(1).Select(int.Parse).DefaultIfEmpty(40).First();
                return Diagnose.Run(TimeSpan.FromSeconds(seconds), openReport: !args.Contains("--quiet"));
            }
            int preview = Array.IndexOf(args, "--preview");
            if (preview >= 0 && preview + 1 < args.Length) return Preview.Run(args[preview + 1]);
            if (args.Contains("--exit")) return SignalExit();
            return RunApp();
        }
        catch (Exception exception)
        {
            Log.Error("Fatal error", exception);
            return 1;
        }
    }

    private static int RunApp()
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

        using var host = new AppHost();
        host.Run();
        return 0;
    }

    /// <summary>Asks a running copy to exit, for example before an update or uninstall.</summary>
    private static int SignalExit()
    {
        if (!EventWaitHandle.TryOpenExisting(AppHost.ExitEventName, out EventWaitHandle? exitEvent)) return 0;
        using (exitEvent) exitEvent.Set();
        return 0;
    }
}
