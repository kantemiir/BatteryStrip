using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BatteryStrip.Core;
using BatteryStrip.Devices;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.App;

/// <summary>
/// The --diagnose mode: lists every HID collection on the computer, then runs the device sources for a
/// while and records what they read. The report is what one sends along when asking to support a new device.
/// </summary>
internal static class Diagnose
{
    public static int Run(TimeSpan duration, bool openReport)
    {
        string reportPath = Path.Combine(AppInfo.DataDirectory, "diagnose.txt");
        Directory.CreateDirectory(AppInfo.DataDirectory);

        using (var report = new StreamWriter(reportPath, append: false, Encoding.UTF8))
        {
            var output = new TeeWriter(report, Console.Out);
            output.WriteLine($"BatteryStrip {AppInfo.Version} diagnostics, {DateTime.Now:yyyy-MM-dd HH:mm}");
            output.WriteLine($"Windows {Environment.OSVersion.Version}");
            output.WriteLine();

            output.WriteLine("HID collections (VID:PID, usage page/usage, report sizes in/out/feature):");
            foreach (HidCollection c in HidCollection.EnumeratePresent().OrderBy(c => c.VendorId).ThenBy(c => c.ProductId).ThenBy(c => c.UsagePage))
            {
                output.WriteLine($"  {c.VendorId:X4}:{c.ProductId:X4}  {c.UsagePage:X4}/{c.Usage:X2}  {c.InputLength,3}/{c.OutputLength,3}/{c.FeatureLength,3}  \"{c.Product}\"");
            }
            output.WriteLine();

            output.WriteLine($"Reading charge for {duration.TotalSeconds:0} seconds:");
            Log.Mirror = output;
            var store = new DeviceStore(() => DateTime.UtcNow);
            store.Changed += () => WriteDevices(output, store.Snapshot);
            using (var manager = new DeviceManager(DeviceSources.Create(store, DeviceNaming.Default, DeviceOptions.ReadOnly)))
            {
                manager.Start();
                Thread.Sleep(duration);
            }
            Log.Mirror = null;

            output.WriteLine();
            output.WriteLine("Result:");
            WriteDevices(output, store.Snapshot);
            if (store.Snapshot.Count == 0) output.WriteLine("  no devices with a known charge");
        }

        if (openReport) Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
        return 0;
    }

    private static void WriteDevices(TextWriter output, IReadOnlyList<DeviceSnapshot> devices)
    {
        foreach (DeviceSnapshot d in devices)
        {
            string charge = d.Percent is int percent ? $"{percent} %" : "no data";
            output.WriteLine($"  {d.Name} [{d.Id}]: {charge}, {StatusText.For(d, DateTime.UtcNow, Strings.English)}");
        }
    }

    /// <summary>Writes every line to the report file and to the console (when there is one).</summary>
    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        private readonly object _gate = new();

        public override Encoding Encoding => first.Encoding;

        public override void WriteLine(string? value)
        {
            lock (_gate)
            {
                first.WriteLine(value);
                first.Flush();
                second.WriteLine(value);
            }
        }

        public override void WriteLine() => WriteLine(string.Empty);
    }
}
