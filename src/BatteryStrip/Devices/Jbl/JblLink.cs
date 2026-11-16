using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BatteryStrip.Devices.Jbl;

/// <summary>Result of one battery request.</summary>
internal abstract record JblReading
{
    private JblReading() { }

    public sealed record Charge(int Percent, bool IsCharging) : JblReading;

    /// <summary>The speaker has no such service or rejected the request: a model this protocol does not cover.</summary>
    public sealed record Unsupported(string Reason) : JblReading;

    public sealed record Failed(string Reason) : JblReading;
}

/// <summary>
/// One battery request over Bluetooth Low Energy: connect, subscribe to answers, write AA 13 02 00 44, wait for the
/// answer, disconnect. No pairing is needed. During music the speaker plays on undisturbed (tested 2026-11-17).
/// </summary>
internal static class JblLink
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(3);

    public static async Task<JblReading> ReadBatteryAsync(JblAdvertisement speaker, CancellationToken cancellation)
    {
        try
        {
            using BluetoothLEDevice? device = await BluetoothLEDevice.FromBluetoothAddressAsync(speaker.Address, speaker.AddressType)
                .WaitAsync(StepTimeout, cancellation).ConfigureAwait(false);
            if (device == null) return new JblReading.Failed("the speaker cannot be reached");

            // Windows remembers the services after the first visit; asking the speaker again only when that fails.
            GattDeviceServicesResult services = await device.GetGattServicesForUuidAsync(JblProtocol.Service, BluetoothCacheMode.Cached)
                .WaitAsync(StepTimeout, cancellation).ConfigureAwait(false);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            {
                foreach (GattDeviceService stale in services.Services) stale.Dispose();
                services = await device.GetGattServicesForUuidAsync(JblProtocol.Service, BluetoothCacheMode.Uncached)
                    .WaitAsync(StepTimeout, cancellation).ConfigureAwait(false);
            }
            if (services.Status != GattCommunicationStatus.Success) return new JblReading.Failed($"service lookup: {services.Status}");
            if (services.Services.Count == 0) return new JblReading.Unsupported("no JBL control service");

            using GattDeviceService service = services.Services[0];
            foreach (GattDeviceService extra in services.Services.Skip(1)) extra.Dispose();
            return await ExchangeAsync(service, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new JblReading.Failed(exception.Message.Trim());
        }
    }

    private static async Task<JblReading> ExchangeAsync(GattDeviceService service, CancellationToken cancellation)
    {
        GattCharacteristic? answers = await FindAsync(service, JblProtocol.Answers, cancellation).ConfigureAwait(false);
        GattCharacteristic? requests = await FindAsync(service, JblProtocol.Requests, cancellation).ConfigureAwait(false);
        if (answers == null || requests == null) return new JblReading.Unsupported("the control service lacks its characteristics");

        var reply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> onAnswer = (_, args) =>
        {
            var frame = new byte[args.CharacteristicValue.Length];
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(frame);
            if (JblProtocol.ParseBattery(frame) != null || JblProtocol.IsRejection(frame)) reply.TrySetResult(frame);
        };

        answers.ValueChanged += onAnswer;
        try
        {
            GattCommunicationStatus subscribed = await answers
                .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)
                .WaitAsync(StepTimeout, cancellation).ConfigureAwait(false);
            if (subscribed != GattCommunicationStatus.Success) return new JblReading.Failed($"subscribe: {subscribed}");

            var writer = new DataWriter();
            writer.WriteBytes(JblProtocol.BatteryRequest());
            GattWriteOption option = requests.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
            GattWriteResult written = await requests.WriteValueWithResultAsync(writer.DetachBuffer(), option)
                .WaitAsync(StepTimeout, cancellation).ConfigureAwait(false);
            if (written.Status != GattCommunicationStatus.Success) return new JblReading.Failed($"request: {written.Status}");

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(AnswerTimeout);
            using (limit.Token.Register(() => reply.TrySetCanceled()))
            {
                byte[] frame;
                try
                {
                    frame = await reply.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    return new JblReading.Failed("no answer");
                }

                if (JblProtocol.ParseBattery(frame) is var (percent, charging)) return new JblReading.Charge(percent, charging);
                return new JblReading.Unsupported("the speaker rejected the battery request");
            }
        }
        finally
        {
            answers.ValueChanged -= onAnswer;
        }
    }

    private static async Task<GattCharacteristic?> FindAsync(GattDeviceService service, Guid uuid, CancellationToken cancellation)
    {
        GattCharacteristicsResult result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Cached)
            .WaitAsync(StepTimeout, cancellation).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
        {
            result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached)
                .WaitAsync(StepTimeout, cancellation).ConfigureAwait(false);
        }
        return result.Status == GattCommunicationStatus.Success ? result.Characteristics.FirstOrDefault() : null;
    }
}
