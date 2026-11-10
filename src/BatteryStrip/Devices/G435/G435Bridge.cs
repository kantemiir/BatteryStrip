using System;
using System.Threading;
using System.Threading.Tasks;
using BatteryStrip.App;
using BatteryStrip.Devices.Hid;

namespace BatteryStrip.Devices.G435;

/// <summary>Result of one visit to the receiver's service mode.</summary>
internal abstract record G435Reading
{
    private G435Reading() { }

    /// <summary>The headset is switched off or out of range.</summary>
    public sealed record HeadsetOff : G435Reading;

    public sealed record Voltage(int Millivolts) : G435Reading;

    /// <summary>The receiver did not answer as expected; nothing is known.</summary>
    public sealed record Failed(string Reason) : G435Reading;
}

/// <summary>
/// Runs the battery read on a G435 receiver: enter service mode, ask, and always leave service mode
/// with the receiver's remote mode put back the way it was. Sends only the commands verified on real
/// hardware; see <see cref="G435Protocol"/>.
/// </summary>
internal sealed class G435Bridge(HidFeaturePort feature, HidInputPort input)
{
    private static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan AckPoll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ReplyPoll = TimeSpan.FromMilliseconds(30);
    private const int MaxStaleReplies = 16;

    private byte _sequence;

    /// <summary>
    /// Reads the battery voltage. Takes about two seconds, during which the headset's audio is paused.
    /// Not cancellable on purpose: an interrupted visit would leave the receiver in service mode.
    /// </summary>
    public async Task<G435Reading> ReadBatteryAsync()
    {
        _sequence = 1;
        int? originalMode = null;
        bool modeChanged = false;

        try
        {
            await input.DiscardPendingAsync(CancellationToken.None).ConfigureAwait(false);

            byte ack = await SendAsync(G435Protocol.EnterServiceMode()).ConfigureAwait(false);
            if (ack is not (G435Protocol.AckOk or G435Protocol.AckAlreadyInServiceMode))
                return new G435Reading.Failed($"enter acknowledged with 0x{ack:X2}");
            DropStaleReplies();

            int? connected = await QueryAsync(G435Protocol.QueryConnectStatus, G435Protocol.ParseConnectStatus).ConfigureAwait(false);
            if (connected == null) return new G435Reading.Failed("no connect status");
            if (connected == 0) return new G435Reading.HeadsetOff();

            originalMode = await QueryAsync(G435Protocol.QueryRemoteMode, G435Protocol.ParseRemoteMode).ConfigureAwait(false);
            if (originalMode == null) return new G435Reading.Failed("no remote mode");
            if (originalMode != G435Protocol.RemoteModeHeadset)
            {
                modeChanged = true;
                await QueryAsync(seq => G435Protocol.ChangeRemoteMode(seq, G435Protocol.RemoteModeHeadset), AnyReply).ConfigureAwait(false);
            }

            int? millivolts = await QueryAsync(G435Protocol.QueryBatteryVoltage, G435Protocol.ParseBatteryMillivolts).ConfigureAwait(false);
            return millivolts is int mv ? new G435Reading.Voltage(mv) : new G435Reading.Failed("no battery reply");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or TimeoutException)
        {
            return new G435Reading.Failed(exception.Message);
        }
        finally
        {
            await LeaveAsync(modeChanged ? originalMode : null).ConfigureAwait(false);
        }
    }

    private async Task LeaveAsync(int? modeToRestore)
    {
        try
        {
            if (modeToRestore is int mode)
                await QueryAsync(seq => G435Protocol.ChangeRemoteMode(seq, mode), AnyReply).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or TimeoutException)
        {
            Log.Warn($"G435: could not restore remote mode ({exception.Message}).");
        }

        try
        {
            await SendAsync(G435Protocol.ExitServiceMode()).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or TimeoutException)
        {
            Log.Warn($"G435: could not leave service mode ({exception.Message}). Replugging the receiver resets it.");
        }
    }

    private static int? AnyReply(byte[] reply) => 0;

    /// <summary>Sends one report and waits for the receiver's acknowledgement byte.</summary>
    private async Task<byte> SendAsync(byte[] frame)
    {
        feature.Set(frame);
        DateTime deadline = DateTime.UtcNow + AckTimeout;
        while (DateTime.UtcNow < deadline)
        {
            byte[]? report = await input.ReadAsync(AckPoll, CancellationToken.None).ConfigureAwait(false);
            if (report is { Length: >= 2 } && report[0] == G435Protocol.ReportId) return report[1];
        }
        throw new TimeoutException("the receiver did not acknowledge a command");
    }

    /// <summary>Sends a command and polls for a reply the parser accepts. Returns null when none arrives in time.</summary>
    private async Task<int?> QueryAsync(Func<byte, byte[]> buildFrame, Func<byte[], int?> parse)
    {
        byte[] frame = buildFrame(_sequence);
        _sequence = G435Protocol.NextSequence(_sequence);
        await SendAsync(frame).ConfigureAwait(false);

        DateTime deadline = DateTime.UtcNow + ReplyTimeout;
        while (DateTime.UtcNow < deadline)
        {
            byte[] reply = feature.Get(G435Protocol.ReportId, G435Protocol.FeatureLength);
            if (reply[1] == G435Protocol.NoReplyYet)
            {
                await Task.Delay(ReplyPoll).ConfigureAwait(false);
                continue;
            }
            if (parse(reply) is int value) return value;
        }
        return null;
    }

    /// <summary>Empties replies left over from an earlier visit.</summary>
    private void DropStaleReplies()
    {
        for (int i = 0; i < MaxStaleReplies; i++)
        {
            if (feature.Get(G435Protocol.ReportId, G435Protocol.FeatureLength)[1] == G435Protocol.NoReplyYet) return;
        }
    }
}
