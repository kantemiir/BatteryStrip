using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.Core;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.Polled;

/// <summary>
/// Report buffers the way hidapi shows them, so offsets from other projects (HeadsetControl, OpenRazer, rivalcfg) carry
/// over unchanged: byte 0 of a request is the report id (0 for a device that does not number its reports); a reply
/// read from the device loses a leading 0 report id, so its byte 0 is the first data byte.
/// </summary>
internal static class Hidapi
{
    /// <summary>A reply as hidapi's hid_read returns it.</summary>
    public static byte[] Payload(byte[] report)
    {
        if (report.Length == 0 || report[0] != 0) return report;
        var payload = new byte[report.Length - 1];
        Array.Copy(report, 1, payload, 0, payload.Length);
        return payload;
    }

    /// <summary>Copies <paramref name="data"/> into a zero-filled buffer of <paramref name="length"/> bytes.</summary>
    public static byte[] Pad(byte[] data, int length)
    {
        var buffer = new byte[Math.Max(length, data.Length)];
        Array.Copy(data, buffer, data.Length);
        return buffer;
    }
}

/// <summary>
/// Request and reply over the interrupt pipes of one collection: an output report out, input reports back.
/// Opened for one read of the device and closed right after, so nothing listens between reads.
/// </summary>
internal sealed class HidExchange : IDisposable
{
    private readonly HidOutputPort _output;
    private readonly HidInputPort _input;

    private HidExchange(HidOutputPort output, HidInputPort input)
    {
        _output = output;
        _input = input;
    }

    /// <summary>Null when the collection has no output or input reports, or Windows refuses to open it.</summary>
    public static HidExchange? Open(HidCollection collection)
    {
        HidOutputPort? output = HidOutputPort.Open(collection);
        HidInputPort? input = output != null ? HidInputPort.Open(collection) : null;
        if (output != null && input != null) return new HidExchange(output, input);
        output?.Dispose();
        input?.Dispose();
        return null;
    }

    /// <summary>
    /// Drops reports already queued, sends <paramref name="request"/> (byte 0 = report id, padded to the report length)
    /// and returns the first reply <paramref name="match"/> accepts, in hidapi form (see <see cref="Hidapi.Payload"/>).
    /// Null when no such reply arrives within <paramref name="timeout"/>. Unrelated reports (input events, answers to
    /// other programs) are skipped.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The request could not be sent.</exception>
    public async Task<byte[]?> AskAsync(byte[] request, Func<byte[], bool> match, TimeSpan timeout, CancellationToken cancellation)
    {
        await _input.DiscardPendingAsync(cancellation).ConfigureAwait(false);
        _output.Write(request);
        DateTime until = DateTime.UtcNow + timeout;
        while (true)
        {
            TimeSpan left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return null;
            byte[]? report = await _input.ReadAsync(left, cancellation).ConfigureAwait(false);
            if (report == null) return null;
            byte[] payload = Hidapi.Payload(report);
            if (match(payload)) return payload;
        }
    }

    public void Dispose()
    {
        _output.Dispose();
        _input.Dispose();
    }
}

/// <summary>Arithmetic shared by the vendor protocols.</summary>
internal static class BatteryMath
{
    public static int Clamp100(int value) => value < 0 ? 0 : value > 100 ? 100 : value;

    /// <summary>Maps <paramref name="low"/>..<paramref name="high"/> linearly onto 0..100, clamped.</summary>
    public static int Map(int value, int low, int high) => high <= low ? Clamp100(value) : Clamp100((value - low) * 100 / (high - low));

    /// <summary>Piecewise-linear lookup in a curve sorted by descending millivolts.</summary>
    public static int Interpolate(int millivolts, IReadOnlyList<(int Millivolts, int Percent)> curve)
    {
        if (millivolts >= curve[0].Millivolts) return curve[0].Percent;
        for (int i = 1; i < curve.Count; i++)
        {
            if (millivolts < curve[i].Millivolts) continue;
            int span = curve[i - 1].Millivolts - curve[i].Millivolts;
            return curve[i].Percent + (curve[i - 1].Percent - curve[i].Percent) * (millivolts - curve[i].Millivolts) / (span == 0 ? 1 : span);
        }
        return curve[curve.Count - 1].Percent;
    }
}

/// <summary>Guesses about a device from what Windows lists, without talking to it.</summary>
internal static class DeviceGuess
{
    /// <summary>Device type from the standard collections a product exposes: a mouse wins over a gamepad over a keyboard.</summary>
    public static DeviceKind FromCollections(IReadOnlyList<HidCollection> all, ushort vendorId, ushort productId)
    {
        bool mouse = false, pad = false, keyboard = false;
        foreach (HidCollection c in all)
        {
            if (c.VendorId != vendorId || c.ProductId != productId || c.UsagePage != 0x01) continue;
            mouse |= c.Usage == 0x02;
            pad |= c.Usage is 0x04 or 0x05;
            keyboard |= c.Usage == 0x06;
        }
        return mouse ? DeviceKind.Mouse : pad ? DeviceKind.Gamepad : keyboard ? DeviceKind.Keyboard : DeviceKind.Other;
    }

    /// <summary>Device type from words in its name.</summary>
    public static DeviceKind FromName(string name)
    {
        string n = name.ToLowerInvariant();
        bool Has(params string[] words) => words.Any(w => n.Contains(w));
        if (Has("mouse", "mice", "trackball")) return DeviceKind.Mouse;
        if (Has("keyboard", "keys")) return DeviceKind.Keyboard;
        if (Has("controller", "gamepad", "xbox", "dualsense", "dualshock")) return DeviceKind.Gamepad;
        if (Has("headset", "headphone", "buds", "arctis", "cloud", "void", "virtuoso", "audio")) return DeviceKind.Headset;
        if (Has("speaker")) return DeviceKind.Speaker;
        return DeviceKind.Other;
    }

    /// <summary>Removes one of the given endings ("Dongle", "Receiver"…) from a product string.</summary>
    public static string TrimSuffixes(string name, params string[] suffixes)
    {
        string trimmed = name.Trim();
        foreach (string suffix in suffixes)
        {
            if (trimmed.Length > suffix.Length && trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring(0, trimmed.Length - suffix.Length).TrimEnd();
        }
        return trimmed;
    }

    /// <summary>A name made safe for a device id: letters and digits, other characters as '-'.</summary>
    public static string Slug(string name)
    {
        var chars = name.Trim().Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray();
        return string.Join("-", new string(chars).Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
