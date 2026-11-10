using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BatteryStrip.Devices.Hid;

/// <summary>
/// Receives input reports from one HID collection. Purely passive: Windows hands every open handle its own
/// copy of each report, so listening does not disturb the device or other programs.
/// </summary>
internal sealed class HidInputPort : IDisposable
{
    private readonly FileStream _stream;
    private readonly int _reportLength;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private Task<int>? _pendingRead;
    private byte[] _pendingBuffer = Array.Empty<byte>();

    private HidInputPort(FileStream stream, int reportLength)
    {
        _stream = stream;
        _reportLength = reportLength;
    }

    /// <summary>Opens the collection for listening; null when Windows refuses (for example, the device was just unplugged).</summary>
    public static HidInputPort? Open(HidCollection collection)
    {
        if (collection.InputLength <= 0) return null;
        SafeFileHandle handle = HidNative.CreateFile(collection.Path, HidNative.GenericRead, HidNative.ShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, HidNative.FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return new HidInputPort(new FileStream(handle, FileAccess.Read, collection.InputLength, isAsync: true), collection.InputLength);
    }

    /// <summary>
    /// Waits for the next input report. Returns null on timeout; the unfinished read stays pending and is
    /// picked up by the next call, so no report is lost.
    /// </summary>
    /// <exception cref="IOException">The device was removed.</exception>
    /// <exception cref="ObjectDisposedException">The port was closed.</exception>
    public async Task<byte[]?> ReadAsync(TimeSpan timeout, CancellationToken cancellation)
    {
        await _readGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (_pendingRead == null)
            {
                _pendingBuffer = new byte[_reportLength];
                _pendingRead = _stream.ReadAsync(_pendingBuffer, 0, _reportLength);
            }

            Task finished = await Task.WhenAny(_pendingRead, Task.Delay(timeout, cancellation)).ConfigureAwait(false);
            if (finished != _pendingRead)
            {
                cancellation.ThrowIfCancellationRequested();
                return null;
            }

            Task<int> read = _pendingRead;
            _pendingRead = null;
            int count = await read.ConfigureAwait(false);
            if (count <= 0) throw new IOException("The HID device closed the input stream.");

            byte[] report = _pendingBuffer;
            if (count != report.Length) Array.Resize(ref report, count);
            return report;
        }
        finally
        {
            _readGate.Release();
        }
    }

    /// <summary>Drops reports that arrived before now, so the next read returns only a fresh one.</summary>
    public async Task DiscardPendingAsync(CancellationToken cancellation)
    {
        // Reports already queued by Windows complete within a few milliseconds.
        while (await ReadAsync(TimeSpan.FromMilliseconds(15), cancellation).ConfigureAwait(false) != null)
        {
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        // A read still pending fails once the handle closes; observe it so it is not reported as unhandled.
        _pendingRead?.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
    }
}

/// <summary>Sends and reads feature reports of one HID collection. Calls block for the duration of a USB control transfer.</summary>
internal sealed class HidFeaturePort : IDisposable
{
    private readonly SafeFileHandle _handle;

    private HidFeaturePort(SafeFileHandle handle) => _handle = handle;

    public static HidFeaturePort? Open(HidCollection collection)
    {
        if (collection.FeatureLength <= 0) return null;
        SafeFileHandle handle = HidNative.CreateFile(collection.Path, HidNative.GenericRead | HidNative.GenericWrite, HidNative.ShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return new HidFeaturePort(handle);
    }

    /// <param name="report">Full report: byte 0 is the report id, the length equals the collection's feature report length.</param>
    /// <exception cref="Win32Exception">The transfer failed.</exception>
    public void Set(byte[] report)
    {
        if (!HidNative.HidD_SetFeature(_handle, report, report.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>Reads the feature report with the given id.</summary>
    /// <exception cref="Win32Exception">The transfer failed.</exception>
    public byte[] Get(byte reportId, int length)
    {
        var report = new byte[length];
        report[0] = reportId;
        if (!HidNative.HidD_GetFeature(_handle, report, report.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return report;
    }

    public void Dispose() => _handle.Dispose();
}
