using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices;

/// <summary>
/// Owns the device sources and tells them when the set of connected devices changed. All refreshes run
/// one after another on a single background task, never on a user-interface thread.
/// </summary>
internal sealed class DeviceManager : IDisposable
{
    private readonly IReadOnlyList<IDeviceSource> _sources;
    private readonly DeviceWatcher _watcher = new();
    private readonly SemaphoreSlim _refreshRequested = new(0);
    private readonly CancellationTokenSource _cancellation = new();
    private Task _worker = Task.CompletedTask;
    private int _pendingWake = -1;

    public DeviceManager(IReadOnlyList<IDeviceSource> sources)
    {
        _sources = sources;
        _watcher.Changed += () => RequestRefresh();
    }

    public void Start()
    {
        _worker = Task.Run(WorkAsync);
        RequestRefresh();
    }

    /// <summary>Asks for a rescan of the connected devices. Safe to call from any thread.</summary>
    /// <param name="reason">Also asks the sources to read their devices soon; a user refresh wins over a resume.</param>
    public void RequestRefresh(WakeReason? reason = null)
    {
        if (reason is WakeReason wake)
        {
            int current;
            do
            {
                current = Volatile.Read(ref _pendingWake);
                if (current >= (int)wake) break;
            }
            while (Interlocked.CompareExchange(ref _pendingWake, (int)wake, current) != current);
        }
        try { _refreshRequested.Release(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _cancellation.Cancel();
        _worker.Wait(TimeSpan.FromSeconds(5));
        foreach (IDeviceSource source in _sources) source.Dispose();
        _refreshRequested.Dispose();
    }

    private async Task WorkAsync()
    {
        try
        {
            while (true)
            {
                await _refreshRequested.WaitAsync(_cancellation.Token).ConfigureAwait(false);
                while (_refreshRequested.CurrentCount > 0) _refreshRequested.Wait(0);
                RefreshAll();

                int wake = Interlocked.Exchange(ref _pendingWake, -1);
                if (wake >= 0) WakeAll((WakeReason)wake);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void WakeAll(WakeReason reason)
    {
        Log.Info($"Devices woken: {reason}.");
        foreach (IDeviceSource source in _sources)
        {
            try
            {
                source.Wake(reason);
            }
            catch (Exception exception)
            {
                Log.Error($"{source.GetType().Name} failed to wake", exception);
            }
        }
    }

    private void RefreshAll()
    {
        IReadOnlyList<HidCollection> present;
        try
        {
            present = HidCollection.EnumeratePresent();
        }
        catch (Exception exception)
        {
            Log.Error("Listing HID devices failed", exception);
            return;
        }

        foreach (IDeviceSource source in _sources)
        {
            try
            {
                source.Refresh(present);
            }
            catch (Exception exception)
            {
                Log.Error($"{source.GetType().Name} failed to refresh", exception);
            }
        }
    }
}
