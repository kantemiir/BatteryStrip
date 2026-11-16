using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace BatteryStrip.Devices;

/// <summary>Awaiting Windows Runtime operations on .NET Framework without the System.Runtime.WindowsRuntime facade.</summary>
internal static class WinRtAsync
{
    /// <summary>Waits for the operation, at most <paramref name="timeout"/>; a late operation is cancelled.</summary>
    /// <exception cref="TimeoutException">The operation did not finish in time.</exception>
    public static async Task<T> WaitAsync<T>(this IAsyncOperation<T> operation, TimeSpan timeout, CancellationToken cancellation)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Completed = (_, _) => done.TrySetResult(true);
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            limit.CancelAfter(timeout);
            using (limit.Token.Register(() => done.TrySetResult(false)))
            {
                if (!await done.Task.ConfigureAwait(false))
                {
                    try { operation.Cancel(); }
                    catch (Exception) { }
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException($"no answer within {timeout.TotalSeconds:0} s");
                }
            }
        }
        return operation.GetResults();
    }
}
