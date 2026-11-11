using System;
using System.Collections.Generic;
using System.Linq;

namespace BatteryStrip.Devices;

/// <summary>
/// Which physical devices a source already shows. One device can be visible to several sources, for example a
/// Logitech mouse on Bluetooth to both the Logitech and the Bluetooth source; the more precise one claims it
/// and the general one leaves it alone. A device is identified by its Windows container id or, where a source
/// has no container id (game controllers), by its name. Thread-safe.
/// </summary>
internal sealed class ContainerClaims
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<string>> _byOwner = new(StringComparer.Ordinal);

    /// <summary>Replaces everything <paramref name="owner"/> claims by container id.</summary>
    public void Set(string owner, IEnumerable<Guid> containers) =>
        Replace(owner, "container", containers.Where(c => c != Guid.Empty).Select(c => c.ToString("N")));

    /// <summary>Replaces everything <paramref name="owner"/> claims by device name.</summary>
    public void SetNames(string owner, IEnumerable<string> names) =>
        Replace(owner, "name", names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim().ToUpperInvariant()));

    /// <summary>True when a source other than <paramref name="asker"/> shows this device.</summary>
    public bool IsClaimedByOther(string asker, Guid container, string? name)
    {
        var keys = new List<string>();
        if (container != Guid.Empty) keys.Add("container:" + container.ToString("N"));
        if (!string.IsNullOrWhiteSpace(name)) keys.Add("name:" + name!.Trim().ToUpperInvariant());
        lock (_gate) return _byOwner.Any(p => !p.Key.StartsWith(asker + "/", StringComparison.Ordinal) && keys.Any(p.Value.Contains));
    }

    private void Replace(string owner, string kind, IEnumerable<string> values)
    {
        var set = new HashSet<string>(values.Select(v => kind + ":" + v), StringComparer.Ordinal);
        lock (_gate) _byOwner[owner + "/" + kind] = set;
    }
}
