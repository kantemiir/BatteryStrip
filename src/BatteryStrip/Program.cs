using System;
using System.Linq;
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
            return 0;
        }
        catch (Exception exception)
        {
            Log.Error("Fatal error", exception);
            return 1;
        }
    }
}
