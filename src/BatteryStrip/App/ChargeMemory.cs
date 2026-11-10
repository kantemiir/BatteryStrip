using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BatteryStrip.Core;

namespace BatteryStrip.App;

/// <summary>
/// Saves the last known charge of every device between program runs, so a device that wakes up later
/// is shown at once with its last value. One device per line: id, percent, estimate flag, time, warning state.
/// </summary>
internal static class ChargeMemory
{
    public static string DefaultPath => Path.Combine(AppInfo.DataDirectory, "charges.txt");

    public static IReadOnlyDictionary<string, RememberedCharge> Load(string path)
    {
        var result = new Dictionary<string, RememberedCharge>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path)) return result;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (TryParse(line, out string id, out RememberedCharge? charge)) result[id] = charge!;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cannot read remembered charges: {exception.Message}");
        }
        return result;
    }

    public static void Save(string path, IReadOnlyDictionary<string, RememberedCharge> charges)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllLines(temporary, charges.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Format(p.Key, p.Value)), Encoding.UTF8);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cannot save remembered charges: {exception.Message}");
        }
    }

    internal static string Format(string id, RememberedCharge charge) => string.Join("\t",
        id,
        charge.Percent.ToString(CultureInfo.InvariantCulture),
        charge.IsEstimate ? "estimate" : "exact",
        charge.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture),
        charge.WarnedAt.ToString(CultureInfo.InvariantCulture));

    internal static bool TryParse(string line, out string id, out RememberedCharge? charge)
    {
        id = "";
        charge = null;
        string[] parts = line.Split('\t');
        if (parts.Length != 5 || parts[0].Length == 0) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent) || percent is < 0 or > 100) return false;
        if (!DateTime.TryParse(parts[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime updated)) return false;
        if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int warnedAt)) return false;

        id = parts[0];
        charge = new RememberedCharge(percent, parts[2] == "estimate", updated.ToUniversalTime(), warnedAt);
        return true;
    }
}
