using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace BatteryStrip.App;

/// <summary>A version like "0.3.0" or "0.3.0-beta.1".</summary>
internal readonly record struct AppVersion(Version Number, string? Prerelease) : IComparable<AppVersion>
{
    public static AppVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        Match match = Regex.Match(text!.Trim(), @"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.\-]+))?(?:\+.*)?$");
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, out int major) || !int.TryParse(match.Groups[2].Value, out int minor)
            || !int.TryParse(match.Groups[3].Value, out int patch)) return null; // a number too large for a version
        return new AppVersion(new Version(major, minor, patch), match.Groups[4].Success ? match.Groups[4].Value : null);
    }

    public bool IsPrerelease => Prerelease != null;

    /// <summary>Semantic-versioning order: 0.3.0-beta.1 &lt; 0.3.0-beta.2 &lt; 0.3.0 &lt; 0.3.1.</summary>
    public int CompareTo(AppVersion other)
    {
        int byNumber = Number.CompareTo(other.Number);
        if (byNumber != 0) return byNumber;
        if (Prerelease == null || other.Prerelease == null) return (Prerelease == null ? 1 : 0) - (other.Prerelease == null ? 1 : 0);

        string[] mine = Prerelease.Split('.'), theirs = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            bool myNumber = int.TryParse(mine[i], out int a), theirNumber = int.TryParse(theirs[i], out int b);
            int part = myNumber && theirNumber ? a.CompareTo(b)
                : myNumber ? -1 : theirNumber ? 1
                : string.CompareOrdinal(mine[i], theirs[i]);
            if (part != 0) return part;
        }
        return mine.Length.CompareTo(theirs.Length);
    }

    public override string ToString() => Prerelease == null ? Number.ToString(3) : $"{Number.ToString(3)}-{Prerelease}";
}

/// <summary>A release that is newer than the running version, with its download.</summary>
internal sealed record AvailableUpdate(AppVersion Version, string ExeUrl, string ChecksumUrl);

/// <summary>
/// Looks for a newer release on GitHub once a day, only when the user agreed. Sends one anonymous request
/// for the feed of releases; nothing about the computer or its devices leaves it.
/// </summary>
internal sealed class UpdateChecker : IDisposable
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(2);

    private readonly Func<bool> _enabled;
    private readonly Timer _timer;
    private AvailableUpdate? _available;
    private AppVersion? _announced;

    /// <summary>Raised on a worker thread when a newer version is found for the first time.</summary>
    public event Action<AvailableUpdate>? Found;

    public UpdateChecker(Func<bool> enabled)
    {
        _enabled = enabled;
        _timer = new Timer(_ => Check(), null, FirstCheck, Interval);
    }

    public AvailableUpdate? Available => Volatile.Read(ref _available);

    /// <summary>Checks within a minute, for example right after the user turned update checks on.</summary>
    public void CheckSoon()
    {
        try { _timer.Change(FirstCheck, Interval); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose() => _timer.Dispose();

    private void Check()
    {
        if (!_enabled() || !AppInfo.HasRepository || AppVersion.Parse(AppInfo.Version) is not AppVersion current) return;
        try
        {
            AvailableUpdate? update = Newest(DownloadText(AppInfo.ReleasesFeed), current, AppInfo.Repository);
            Volatile.Write(ref _available, update);
            if (update != null && !Equals(_announced, update.Version))
            {
                _announced = update.Version;
                Log.Info($"Update available: {update.Version}.");
                Found?.Invoke(update);
            }
        }
        catch (Exception exception)
        {
            // No network right now (the computer just woke up, a VPN is connecting), or an odd answer: try again
            // later today. This runs on a timer thread, where an unhandled exception would end the program.
            Log.Warn($"Update check failed: {exception.Message}");
            try { _timer.Change(RetryAfterFailure, Interval); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Picks the newest release above <paramref name="current"/> from the releases feed. Pre-releases are offered
    /// only to people already running one; drafts never appear in the feed. The files of a release follow the names
    /// the release workflow gives them, so their addresses are built from the tag rather than read from the feed.
    /// </summary>
    internal static AvailableUpdate? Newest(string releasesFeed, AppVersion current, string repository)
    {
        AvailableUpdate? best = null;
        string prefix = "https://github.com/" + repository + "/releases/tag/";
        foreach (Match link in Regex.Matches(releasesFeed, "href=\"" + Regex.Escape(prefix) + "(?<tag>[0-9A-Za-z.\\-]+)\""))
        {
            string tag = link.Groups["tag"].Value;
            if (AppVersion.Parse(tag) is not AppVersion version) continue;
            if (version.IsPrerelease && !current.IsPrerelease) continue;
            if (version.CompareTo(current) <= 0 || (best != null && version.CompareTo(best.Version) <= 0)) continue;

            string files = $"https://github.com/{repository}/releases/download/{tag}/BatteryStrip-{version}";
            best = new AvailableUpdate(version, files + ".exe", files + ".sha256.txt");
        }
        return best;
    }

    private static int _installing;

    /// <summary>
    /// Downloads the update, checks it against its published SHA-256 and its own version, and starts its silent
    /// install, which closes this copy and starts the new one. Returns an error message for the user, or null on
    /// success or when an install is already under way.
    /// </summary>
    public static string? Install(AvailableUpdate update)
    {
        if (Interlocked.Exchange(ref _installing, 1) != 0) return null; // a second click while downloading
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "BatteryStrip-update");
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, $"BatteryStrip-{update.Version}.exe");

            string expected = DownloadText(update.ChecksumUrl).Trim().Split(' ', '\t')[0].ToUpperInvariant();
            using (var client = NewClient()) client.DownloadFile(update.ExeUrl, exe);
            string actual = Sha256(exe);
            if (actual != expected)
            {
                Log.Warn($"Update {update.Version}: checksum mismatch ({actual} vs {expected}).");
                File.Delete(exe);
                return Text.Current.UpdateDownloadBroken;
            }
            if (AppVersion.Parse(FileVersionInfo.GetVersionInfo(exe).ProductVersion) is not AppVersion downloaded || !downloaded.Equals(update.Version))
            {
                Log.Warn($"Update {update.Version}: the downloaded file is version {FileVersionInfo.GetVersionInfo(exe).ProductVersion}.");
                File.Delete(exe);
                return Text.Current.UpdateDownloadBroken;
            }

            Log.Info($"Installing update {update.Version}.");
            Process.Start(new ProcessStartInfo(exe, "--install --quiet --start") { UseShellExecute = false })?.Dispose();
            return null;
        }
        catch (Exception exception) when (exception is WebException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Update failed", exception);
            return exception is WebException ? Text.Current.UpdateDownloadFailed : exception.Message;
        }
        finally
        {
            // After a success this copy is about to be closed by the new one; after a failure the user may try again.
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    internal static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using FileStream file = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
    }

    private static string DownloadText(string url)
    {
        using WebClient client = NewClient();
        return client.DownloadString(url);
    }

    private static WebClient NewClient()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var client = new WebClient { Encoding = System.Text.Encoding.UTF8 };
        client.Headers[HttpRequestHeader.UserAgent] = $"BatteryStrip/{AppInfo.Version}";
        return client;
    }
}
