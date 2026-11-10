using System;

namespace BatteryStrip.Devices.G435;

/// <summary>
/// Decides when the headset may be read. A read pauses its audio link for about two seconds, so it is
/// allowed only when nobody would hear that: no program has a stream open for a short while, or streams
/// are open but have been digitally silent for a long while. A microphone in use always blocks it.
/// </summary>
internal sealed class QuietGate
{
    public static readonly TimeSpan IdleTime = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SilentTime = TimeSpan.FromMinutes(10);

    private DateTime? _idleSince;
    private DateTime? _silentSince;

    /// <summary>Takes one sample of the audio state; returns true when a read may start now.</summary>
    /// <param name="state">Null when the state is unknown, which counts as busy.</param>
    public bool Observe(AudioState? state, DateTime now)
    {
        if (state is not AudioState audio || audio.MicrophoneInUse)
        {
            Reset();
            return false;
        }

        _idleSince = audio.HasOpenStreams ? null : _idleSince ?? now;
        _silentSince = audio.IsAudible ? null : _silentSince ?? now;

        return Elapsed(_idleSince, now) >= IdleTime || Elapsed(_silentSince, now) >= SilentTime;
    }

    public void Reset()
    {
        _idleSince = null;
        _silentSince = null;
    }

    private static TimeSpan Elapsed(DateTime? since, DateTime now) => since is DateTime start ? now - start : TimeSpan.Zero;
}
