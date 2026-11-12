using System;
using System.Runtime.InteropServices;

namespace BatteryStrip.App;

/// <summary>
/// Tells whether a full-screen program (a game, a full-screen video, a presentation) is in front, the same way
/// Windows decides to hold back notifications. While one is, the program sends nothing to devices on its own and
/// leaves the taskbar alone: the taskbar is hidden anyway, and a game should get the computer to itself.
/// Listening to what devices send by themselves goes on.
/// </summary>
internal static class FullScreen
{
    private const int Busy = 2, Direct3DFullScreen = 3, PresentationMode = 4;

    /// <summary>How long a device source waits before looking again while a full-screen program runs.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(30);

    /// <summary>For tests: replaces the Windows query.</summary>
    internal static Func<bool>? Override { get; set; }

    public static bool IsActive()
    {
        if (Override is Func<bool> fake) return fake();
        try
        {
            return SHQueryUserNotificationState(out int state) == 0 && state is Busy or Direct3DFullScreen or PresentationMode;
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
