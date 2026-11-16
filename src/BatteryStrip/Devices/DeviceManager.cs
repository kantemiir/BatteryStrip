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

    /// <summary>A session that stopped by itself (a transient open or read error) is restarted by a rescan this
    /// much later, doubling while it keeps failing, so a device does not stay missing until some other device event.</summary>
    private static readonly TimeSpan RetryFirst = TimeSpan.FromSeconds(30), RetryMax = TimeSpan.FromMinutes(10);

    private readonly Timer _retry;
    private readonly object _retryGate = new();
    private TimeSpan _retryDelay = RetryFirst;
    private DateTime _lastSessionEnd = DateTime.MinValue;
    private bool _retryPending;

    public DeviceManager(IReadOnlyList<IDeviceSource> sources)
    {
        _sources = sources;
        _watcher.Changed += () => RequestRefresh();
        _retry = new Timer(_ => RetryRefresh());
        DeviceSession.EndedOnItsOwn += OnSessionEnded;
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
        DeviceSession.EndedOnItsOwn -= OnSessionEnded;
        _retry.Dispose();
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

    private void OnSessionEnded()
    {
        lock (_retryGate)
        {
            DateTime now = DateTime.UtcNow;
            // Failures close together back off; a quiet spell starts over from the short delay.
            _retryDelay = now - _lastSessionEnd > RetryMax + RetryMax ? RetryFirst
                : TimeSpan.FromTicks(Math.Min(RetryMax.Ticks, _retryDelay.Ticks * 2));
            _lastSessionEnd = now;
            if (_retryPending) return;
            _retryPending = true;
            try { _retry.Change(_retryDelay, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    private void RetryRefresh()
    {
        lock (_retryGate) _retryPending = false;
        RequestRefresh();
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
