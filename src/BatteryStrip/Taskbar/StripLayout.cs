namespace BatteryStrip.Taskbar;

internal enum StripFit
{
    /// <summary>Icons and batteries.</summary>
    Full,

    /// <summary>Batteries only.</summary>
    Compact,

    /// <summary>Not even the batteries fit; the tray icons stand in.</summary>
    None,
}

/// <summary>How much of the strip fits between the taskbar's app buttons and the notification area.</summary>
internal static class StripLayout
{
    /// <param name="right">Right edge of the strip, in taskbar coordinates.</param>
    /// <param name="appsRight">Right edge of the last app button; null when unknown, which means "assume there is room".</param>
    /// <param name="margin">Space to keep free after the last app button.</param>
    public static StripFit Choose(int right, int? appsRight, int margin, float fullWidth, float compactWidth)
    {
        if (appsRight is not int apps) return StripFit.Full;
        int room = right - apps - margin;
        if (fullWidth <= room) return StripFit.Full;
        return compactWidth <= room ? StripFit.Compact : StripFit.None;
    }
}
