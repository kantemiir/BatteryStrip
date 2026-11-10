using System;

namespace BatteryStrip.Taskbar;

/// <summary>Where the strip may go on the primary taskbar right now.</summary>
/// <param name="Taskbar">The Shell_TrayWnd window: the strip's parent.</param>
/// <param name="TrayLeft">Left edge of the notification area, in taskbar client coordinates.</param>
internal sealed record TaskbarPlace(IntPtr Taskbar, IntPtr Tray, User32.RECT TaskbarRect, int TrayLeft, int Height, uint Dpi)
{
    public float Scale => Dpi / 96f;
}

/// <summary>Finds the primary taskbar and its notification area.</summary>
internal static class TaskbarLocator
{
    public const string TaskbarClass = "Shell_TrayWnd";
    public const string TrayClass = "TrayNotifyWnd";

    /// <summary>Returns null when there is no usable taskbar: Explorer is restarting, or the taskbar is vertical.</summary>
    public static TaskbarPlace? Find(out string? problem)
    {
        problem = null;
        IntPtr taskbar = User32.FindWindow(TaskbarClass, null);
        if (taskbar == IntPtr.Zero)
        {
            problem = "taskbar window not found";
            return null;
        }

        IntPtr tray = User32.FindWindowEx(taskbar, IntPtr.Zero, TrayClass, null);
        if (tray == IntPtr.Zero)
        {
            problem = "notification area not found";
            return null;
        }

        if (!User32.GetWindowRect(taskbar, out User32.RECT taskbarRect) || !User32.GetWindowRect(tray, out User32.RECT trayRect))
        {
            problem = "taskbar geometry unavailable";
            return null;
        }

        if (taskbarRect.Width <= taskbarRect.Height)
        {
            problem = $"taskbar is vertical {taskbarRect}";
            return null;
        }

        uint dpi = User32.GetDpiForWindow(taskbar);
        return new TaskbarPlace(taskbar, tray, taskbarRect, trayRect.Left - taskbarRect.Left, taskbarRect.Height, dpi == 0 ? 96 : dpi);
    }
}
