using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.HeadsetControl;

/// <summary>
/// Headsets that the separate HeadsetControl command-line tool (github.com/Sapd/HeadsetControl) can read. The tool is
/// not bundled: when the user has headsetcontrol.exe next to this program or in the PATH, it is run with "-o json"
/// now and then, and the headsets it reports are shown. Without the tool this source does nothing at all.
/// </summary>
internal sealed class HeadsetControlSource : IDeviceSource
{
    public const string Owner = "headsetcontrol";

    private const string ToolName = "headsetcontrol.exe";
    private const string Available = "BATTERY_AVAILABLE";
    private const string Charging = "BATTERY_CHARGING";

    /// <summary>How often the tool is run on its own.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>Device-list refreshes run the tool at most this often. A wake-up is not held back by it.</summary>
    private static readonly TimeSpan MinGap = TimeSpan.FromMinutes(1);

    /// <summary>A run that takes longer is killed.</summary>
    private static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(8);

    /// <summary>Time allowed to collect the output after the tool has stopped.</summary>
    private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(2);

    private readonly DeviceStore _store;
    private readonly DeviceNaming _naming;
    private readonly ContainerClaims _claims;
    private readonly Timer _poll;
    private readonly object _gate = new();

    // Guarded by _gate: the tool's path, the last device listing, whether a run is going, and whether disposed.
    private string? _tool;
    private IReadOnlyList<HidCollection> _present = Array.Empty<HidCollection>();
    private bool _running;
    private bool _disposed;
    private DateTime _lastRunUtc = DateTime.MinValue;

    // Refresh and Wake arrive one at a time from the device worker, so this needs no lock.
    private bool _looked;

    // Used only by the one run at a time (see _running), so no lock is needed.
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private string? _lastFailure;

    public HeadsetControlSource(DeviceStore store, DeviceNaming naming, ContainerClaims claims)
    {
        _store = store;
        _naming = naming;
        _claims = claims;
        _poll = new Timer(_ => Consider(debounced: false), null, Interval, Interval);
    }

    /// <summary>Called on the device worker after every change of the connected devices. The first call, at startup, looks for the tool.</summary>
    public void Refresh(IReadOnlyList<HidCollection> present)
    {
        if (!_looked) Look();
        lock (_gate) _present = present;
        Consider(debounced: true);
    }

    /// <summary>Called on the device worker when the computer wakes up or the user asks for a refresh. Looks for the tool again.</summary>
    public void Wake(WakeReason reason)
    {
        Look();
        Consider(debounced: false);
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _poll.Dispose();
    }

    /// <summary>Finds the tool and notes when it turns up or goes away. Logs only those changes, never a missing tool.</summary>
    private void Look()
    {
        _looked = true;
        string? found = FindTool();
        string? previous;
        lock (_gate)
        {
            previous = _tool;
            _tool = found;
        }

        if (found == previous) return;
        if (found != null) Log.Info($"headsetcontrol found: {found}.");
        else Log.Info($"headsetcontrol is no longer found (it was at {previous}).");
    }

    /// <summary>The tool next to this program, else in the first absolute PATH directory that holds it. Null when there is none.</summary>
    private static string? FindTool()
    {
        string? beside = Path.GetDirectoryName(AppInfo.ExePath);
        if (!string.IsNullOrEmpty(beside))
        {
            string candidate = Path.Combine(beside, ToolName);
            if (File.Exists(candidate)) return candidate;
        }

        // Only absolute directories: a relative PATH entry means the current directory, which can be anywhere.
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (string entry in path.Split(';'))
        {
            string directory = entry.Trim().Trim('"');
            if (!IsAbsoluteDirectory(directory)) continue;
            string candidate = Path.Combine(directory, ToolName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsAbsoluteDirectory(string directory)
    {
        bool drive = directory.Length >= 3 && char.IsLetter(directory[0]) && directory[1] == ':' && (directory[2] == '\\' || directory[2] == '/');
        bool network = directory.StartsWith(@"\\", StringComparison.Ordinal);
        return (drive || network) && Directory.Exists(directory);
    }

    /// <summary>
    /// Starts a run on a pool thread, unless the tool is not there, a run is already going, a full-screen program is
    /// in front, or (when <paramref name="debounced"/>) the tool ran less than <see cref="MinGap"/> ago.
    /// </summary>
    private void Consider(bool debounced)
    {
        if (FullScreen.IsActive()) return;
        if (!TryStartRun(debounced, out string tool)) return;
        Task.Run(() => RunAndApply(tool));
    }

    /// <summary>Marks a run as started, unless one is going, the tool is not there, or the last run was too recent.</summary>
    private bool TryStartRun(bool debounced, out string tool)
    {
        lock (_gate)
        {
            string? found = _tool;
            if (_disposed || _running || found is null)
            {
                tool = "";
                return false;
            }
            tool = found;
            if (debounced && DateTime.UtcNow - _lastRunUtc < MinGap) return false;
            _running = true;
            _lastRunUtc = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>Runs the tool once and shows what it reports. Clears the running flag whatever happens.</summary>
    private void RunAndApply(string tool)
    {
        try
        {
            string? failure = Run(tool, out string output);
            if (!HeadsetControlJson.TryParse(output, out IReadOnlyList<HeadsetControlDevice> devices))
            {
                ReportFailure(failure ?? "its output is not the JSON the program expects");
                return;
            }

            // Usable output counts even when the tool exited with an error: the error is noted, the headsets still shown.
            if (failure == null) _lastFailure = null;
            else ReportFailure(failure);
            Apply(devices, Present());
        }
        catch (Exception exception)
        {
            Log.Error("headsetcontrol: reading its output failed", exception);
        }
        finally
        {
            lock (_gate) _running = false;
        }
    }

    /// <summary>Logs a failure once. The same failure again, run after run, is not logged again.</summary>
    private void ReportFailure(string message)
    {
        if (message == _lastFailure) return;
        _lastFailure = message;
        Log.Info($"headsetcontrol: {message}.");
    }

    private IReadOnlyList<HidCollection> Present()
    {
        lock (_gate) return _present;
    }

    /// <summary>
    /// Runs "tool -o json" without a window and collects what it printed. Returns null when it exited with code 0 on its
    /// own; otherwise the reason. The output is empty after a timeout, and otherwise whatever was printed.
    /// </summary>
    private static string? Run(string tool, out string output)
    {
        output = "";
        var start = new ProcessStartInfo
        {
            FileName = tool,
            Arguments = "-o json",
            WorkingDirectory = Path.GetDirectoryName(tool) ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception exception)
        {
            // Reported through ReportFailure like any other failure, so a broken install is logged once, not every run.
            return "it could not be started (" + exception.Message + ")";
        }
        if (process is null) return "it could not be started";

        using (process)
        {
            // Both pipes are read while the tool runs, so a full pipe cannot hold it up.
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit((int)RunLimit.TotalMilliseconds);
            if (!exited) Kill(process);
            bool drained = Task.WaitAll(new Task[] { stdout, stderr }, (int)DrainLimit.TotalMilliseconds);

            if (!exited) return $"it did not finish within {RunLimit.TotalSeconds:0} seconds";
            if (!drained) return "its output did not end";

            output = stdout.Result;
            if (process.ExitCode == 0) return null;
            string detail = FirstLine(stderr.Result);
            return $"it exited with code {process.ExitCode}" + (detail.Length > 0 ? $": {detail}" : "");
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // It exited on its own a moment ago.
        }
    }

    /// <summary>The first non-empty line of <paramref name="text"/>, cut short, for a log line.</summary>
    private static string FirstLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed.Length > 120 ? trimmed.Substring(0, 120) : trimmed;
        }
        return "";
    }

    /// <summary>
    /// Shows what the tool reported. Headsets listed earlier and not listed now are disconnected. Only called from the
    /// one run at a time, so <see cref="_shown"/> needs no lock.
    /// </summary>
    private void Apply(IReadOnlyList<HeadsetControlDevice> devices, IReadOnlyList<HidCollection> present)
    {
        var listed = new Dictionary<string, HeadsetControlDevice>(StringComparer.Ordinal);
        foreach (HeadsetControlDevice device in devices)
        {
            if (IsClaimedByOther(device, present)) continue;
            string id = IdOf(device);
            if (!listed.ContainsKey(id)) listed.Add(id, device);
        }

        foreach (KeyValuePair<string, HeadsetControlDevice> pair in listed) ShowReading(pair.Key, pair.Value);

        foreach (string gone in _shown.Where(id => !listed.ContainsKey(id)).ToArray())
        {
            _shown.Remove(gone);
            _store.Disconnect(gone);
            Log.Info($"headsetcontrol device gone: {gone}.");
        }

        // Claimed so the general sources listed later (standard HID battery, Bluetooth) do not show the same headset twice.
        var shownDevices = listed.Where(p => _shown.Contains(p.Key)).Select(p => p.Value).ToList();
        _claims.Set(Owner, present
            .Where(c => shownDevices.Any(d => d.VendorId == c.VendorId && d.ProductId == c.ProductId))
            .Select(c => c.ContainerId)
            .Distinct()
            .ToArray());
        _claims.SetNames(Owner, shownDevices.Select(d => d.Name));
    }

    private void ShowReading(string id, HeadsetControlDevice device)
    {
        DeviceReport? report = ReportOf(device);
        if (!_shown.Contains(id))
        {
            // A headset appears on the strip with its first charge, never with a bare "connected".
            if (report is not DeviceReport.Charge) return;
            string name = _naming.Resolve(id, string.IsNullOrWhiteSpace(device.Name) ? Text.Current.Headset : device.Name.Trim());
            _store.Connect(new DeviceDescriptor(id, name, DeviceKind.Headset));
            _shown.Add(id);
            Log.Info($"headsetcontrol device found: {name} [{id}].");
        }

        if (report != null) _store.Report(id, report);
    }

    /// <summary>
    /// What a battery reading means for the strip. Null when the headset answers without a usable value: it stays
    /// as it is, with nothing new to show.
    /// </summary>
    private static DeviceReport? ReportOf(HeadsetControlDevice device)
    {
        bool charging = string.Equals(device.Status, Charging, StringComparison.OrdinalIgnoreCase);
        bool answers = charging || string.Equals(device.Status, Available, StringComparison.OrdinalIgnoreCase);
        if (!answers) return new DeviceReport.Asleep();
        if (device.Level < 0 || device.Level > 100) return null;
        return new DeviceReport.Charge(device.Level, charging);
    }

    /// <summary>
    /// True when another source already shows this headset: by its name, or by the container of a HID collection
    /// with the same vendor and product id.
    /// </summary>
    private bool IsClaimedByOther(HeadsetControlDevice device, IReadOnlyList<HidCollection> present)
    {
        if (_claims.IsClaimedByOther(Owner, Guid.Empty, device.Name)) return true;
        foreach (HidCollection collection in present)
        {
            if (collection.VendorId != device.VendorId || collection.ProductId != device.ProductId) continue;
            if (_claims.IsClaimedByOther(Owner, collection.ContainerId, null)) return true;
        }
        return false;
    }

    private static string IdOf(HeadsetControlDevice device) => $"headsetcontrol:{device.VendorId:X4}:{device.ProductId:X4}";
}
