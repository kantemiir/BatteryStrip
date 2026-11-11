using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices;

/// <summary>
/// A family of devices the program can read: one vendor protocol or one Windows interface.
/// A source recognises its devices among those present and keeps the device store up to date.
/// </summary>
/// <summary>Why the device list is being looked at again outside the usual schedule.</summary>
internal enum WakeReason
{
    /// <summary>The computer woke from sleep or Explorer restarted.</summary>
    Resumed,

    /// <summary>The user chose "Обновить устройства": read everything now.</summary>
    UserRefresh,
}

internal interface IDeviceSource : IDisposable
{
    /// <summary>
    /// Called on a worker thread at startup and whenever the set of connected devices changed.
    /// Calls never overlap.
    /// </summary>
    void Refresh(IReadOnlyList<HidCollection> present);

    /// <summary>Called right after <see cref="Refresh"/> when there is a reason to read devices sooner than scheduled.</summary>
    void Wake(WakeReason reason);
}

/// <summary>Background work bound to one connected device. Ends on its own when the device is removed.</summary>
internal abstract class DeviceSession : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private Task _run = Task.CompletedTask;

    /// <summary>True once the session stopped, by itself or through <see cref="Dispose"/>.</summary>
    public bool HasEnded => _run.IsCompleted;

    protected abstract string Name { get; }

    protected abstract Task RunAsync(CancellationToken cancellation);

    public void Start()
    {
        _run = Task.Run(async () =>
        {
            try
            {
                await RunAsync(_cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (System.IO.IOException exception)
            {
                Log.Info($"{Name}: the device went away ({exception.Message}).");
            }
            catch (Exception exception)
            {
                Log.Error($"{Name}: session failed", exception);
            }
        });
    }

    /// <summary>Asks the session to stop. It may still be finishing an exchange with its device; see <see cref="WaitForEnd"/>.</summary>
    public virtual void Dispose()
    {
        _cancellation.Cancel();
    }

    /// <summary>Blocks until the session has stopped, so the program never exits in the middle of a device exchange.</summary>
    public bool WaitForEnd(TimeSpan timeout) => _run.Wait(timeout);
}

/// <summary>Keeps one session per recognised device: starts new ones, stops those whose device is gone, restarts failed ones.</summary>
internal sealed class SessionSet<TSession> : IDisposable where TSession : DeviceSession
{
    private readonly Dictionary<string, TSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="wanted">Key of every device present now, with a factory for its session.</param>
    public void Reconcile(IReadOnlyDictionary<string, Func<TSession>> wanted)
    {
        foreach (string key in _sessions.Keys.ToArray())
        {
            if (wanted.ContainsKey(key) && !_sessions[key].HasEnded) continue;
            _sessions[key].Dispose();
            _sessions.Remove(key);
        }

        foreach (var pair in wanted)
        {
            if (_sessions.ContainsKey(pair.Key)) continue;
            TSession session = pair.Value();
            _sessions.Add(pair.Key, session);
            session.Start();
        }
    }

    public void ForEach(Action<TSession> action)
    {
        foreach (TSession session in _sessions.Values) action(session);
    }

    public void Dispose()
    {
        foreach (TSession session in _sessions.Values) session.Dispose();
        foreach (TSession session in _sessions.Values) session.WaitForEnd(TimeSpan.FromSeconds(6));
        _sessions.Clear();
    }
}
