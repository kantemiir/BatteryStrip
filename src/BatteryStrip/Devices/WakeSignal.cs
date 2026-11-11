using System;
using System.Threading;
using System.Threading.Tasks;

namespace BatteryStrip.Devices;

/// <summary>
/// Lets another thread cut a device session's wait short. Unlike waiting on a semaphore in a loop, an unused
/// wait leaves nothing behind, so a session that waits every second does not pile up waiters.
/// </summary>
internal sealed class WakeSignal
{
    private readonly object _gate = new();
    private TaskCompletionSource<bool> _signal = NewSignal();

    /// <summary>Wakes the current or the next wait. Safe from any thread.</summary>
    public void Set()
    {
        lock (_gate) _signal.TrySetResult(true);
    }

    /// <summary>Waits for <paramref name="delay"/> or until <see cref="Set"/>. Returns true when woken.</summary>
    public async Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellation)
    {
        Task signal;
        lock (_gate) signal = _signal.Task;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task sleep = Task.Delay(delay, timeout.Token);
        Task first = await Task.WhenAny(signal, sleep).ConfigureAwait(false);
        timeout.Cancel(); // stops the timer at once instead of leaving it to run out
        cancellation.ThrowIfCancellationRequested();

        if (first != signal) return false;
        lock (_gate)
        {
            if (_signal.Task == signal) _signal = NewSignal();
        }
        return true;
    }

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
