using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace BatteryStrip.Devices.Jbl;

/// <summary>A speaker heard on the air: where to connect to it now (its random address changes every few minutes).</summary>
internal sealed record JblAdvertisement(ulong Address, BluetoothAddressType AddressType, ushort ProductId);

/// <summary>
/// Listens for Harman advertisements, only while somebody is waiting for one. Passive: nothing is transmitted,
/// the radio just listens. One listener serves every waiting speaker.
/// </summary>
internal sealed class HarmanAdvertisements : IDisposable
{
    private sealed class Waiter(ushort addressCrc)
    {
        public ushort AddressCrc { get; } = addressCrc;
        public TaskCompletionSource<JblAdvertisement?> Found { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _gate = new();
    private readonly List<Waiter> _waiting = new();
    private BluetoothLEAdvertisementWatcher? _watcher;
    private bool _disposed;

    /// <summary>Waits for the advertisement of the speaker whose classic address has this CRC. Null if not heard in time.</summary>
    public async Task<JblAdvertisement?> FindAsync(ushort addressCrc, TimeSpan timeout, CancellationToken cancellation)
    {
        var waiter = new Waiter(addressCrc);
        lock (_gate)
        {
            if (_disposed) return null;
            _waiting.Add(waiter);
            if (!EnsureListening())
            {
                _waiting.Remove(waiter);
                return null;
            }
        }

        JblAdvertisement? found;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(timeout);
            using (limit.Token.Register(() => waiter.Found.TrySetResult(null)))
            {
                found = await waiter.Found.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                _waiting.Remove(waiter);
                if (_waiting.Count == 0) StopListening();
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return found;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (Waiter waiter in _waiting) waiter.Found.TrySetResult(null);
            _waiting.Clear();
            StopListening();
        }
    }

    private bool EnsureListening()
    {
        if (_watcher != null) return true;
        try
        {
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
            // Windows drops everything but Harman's advertisements before they reach the program.
            watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData { CompanyId = JblProtocol.HarmanCompanyId });
            watcher.Received += OnReceived;
            watcher.Stopped += OnStopped;
            watcher.Start();
            _watcher = watcher;
            return true;
        }
        catch (Exception exception)
        {
            // No Bluetooth adapter, or it is switched off.
            Log.Info($"JBL: cannot listen for Bluetooth advertisements ({exception.Message}).");
            return false;
        }
    }

    private void StopListening()
    {
        if (_watcher is not BluetoothLEAdvertisementWatcher watcher) return;
        _watcher = null;
        watcher.Received -= OnReceived;
        watcher.Stopped -= OnStopped;
        try { watcher.Stop(); }
        catch (Exception) { }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (BluetoothLEManufacturerData data in args.Advertisement.ManufacturerData)
        {
            if (data.CompanyId != JblProtocol.HarmanCompanyId) continue;
            var bytes = new byte[data.Data.Length];
            DataReader.FromBuffer(data.Data).ReadBytes(bytes);
            if (JblProtocol.ParseAdvertisement(bytes) is not var (productId, addressCrc)) continue;

            var found = new JblAdvertisement(args.BluetoothAddress, args.BluetoothAddressType, productId);
            lock (_gate)
            {
                foreach (Waiter waiter in _waiting)
                {
                    if (waiter.AddressCrc == addressCrc) waiter.Found.TrySetResult(found);
                }
            }
        }
    }

    /// <summary>Windows stopped listening by itself (the radio was switched off): nobody will be heard.</summary>
    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _watcher)) return;
            _watcher = null;
            sender.Received -= OnReceived;
            sender.Stopped -= OnStopped;
            foreach (Waiter waiter in _waiting) waiter.Found.TrySetResult(null);
        }
    }
}
