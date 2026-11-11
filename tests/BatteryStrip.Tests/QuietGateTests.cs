using System;
using BatteryStrip.Devices.G435;
using Xunit;

namespace BatteryStrip.Tests;

public class QuietGateTests
{
    private static readonly DateTime Start = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);

    private static readonly AudioState Idle = new(HasOpenStreams: false, IsAudible: false, MicrophoneInUse: false);
    private static readonly AudioState Playing = new(HasOpenStreams: true, IsAudible: true, MicrophoneInUse: false);
    private static readonly AudioState PausedVideo = new(HasOpenStreams: true, IsAudible: false, MicrophoneInUse: false);
    private static readonly AudioState Call = new(HasOpenStreams: true, IsAudible: false, MicrophoneInUse: true);

    /// <summary>Feeds the same state once a second and returns how many seconds passed before a read was allowed.</summary>
    private static int? SecondsUntilAllowed(QuietGate gate, AudioState? state, int limit = 120)
    {
        for (int second = 0; second <= limit; second++)
        {
            if (gate.Observe(state, Start.AddSeconds(second))) return second;
        }
        return null;
    }

    [Fact]
    public void Unused_headset_is_read_after_a_short_idle_time()
    {
        Assert.Equal((int)QuietGate.IdleTime.TotalSeconds, SecondsUntilAllowed(new QuietGate(), Idle));
    }

    [Fact]
    public void A_pause_in_playback_is_enough()
    {
        Assert.Equal((int)QuietGate.SilentTime.TotalSeconds, SecondsUntilAllowed(new QuietGate(), PausedVideo));
    }

    [Fact]
    public void Never_while_sound_plays_or_the_microphone_is_open()
    {
        Assert.Null(SecondsUntilAllowed(new QuietGate(), Playing));
        Assert.Null(SecondsUntilAllowed(new QuietGate(), Call));
        Assert.Null(SecondsUntilAllowed(new QuietGate(), null));
    }

    [Fact]
    public void Any_sound_restarts_the_silence_count()
    {
        var gate = new QuietGate();
        for (int second = 0; second < 15; second++) Assert.False(gate.Observe(PausedVideo, Start.AddSeconds(second)));
        Assert.False(gate.Observe(Playing, Start.AddSeconds(15)));
        for (int second = 16; second < 36; second++) Assert.False(gate.Observe(PausedVideo, Start.AddSeconds(second)));
        Assert.True(gate.Observe(PausedVideo, Start.AddSeconds(36)));
    }
}
