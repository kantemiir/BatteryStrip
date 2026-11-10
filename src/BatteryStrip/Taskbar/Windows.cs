using System;

namespace BatteryStrip.Taskbar;

/// <summary>
/// An invisible top-level window. Child windows do not receive broadcasts, so this one listens for
/// them on the strip's behalf: Explorer restarts, theme and display changes, waking from sleep.
/// </summary>
internal sealed class BroadcastWindow : NativeWindow
{
    /// <summary>Handler returns true when it handled the message.</summary>
    public event Func<int, IntPtr, IntPtr, bool>? Message;

    public BroadcastWindow()
    {
        Create("BatteryStrip.Broadcasts", User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE, User32.WS_POPUP, IntPtr.Zero);
    }

    protected override IntPtr? OnMessage(int message, IntPtr wParam, IntPtr lParam) =>
        Message?.Invoke(message, wParam, lParam) == true ? IntPtr.Zero : null;
}

/// <summary>The strip itself: a transparent layered window that lives inside the taskbar.</summary>
internal sealed class StripWindow : NativeWindow
{
    private bool _tracking;

    public event Action<int, int>? LeftClick;
    public event Action<int, int>? RightClick;
    public event Action<int, int>? Hover;
    public event Action? HoverEnd;
    public event Action? Destroyed;

    public StripWindow()
    {
        // Created as a hidden pop-up and turned into a child of the taskbar by TaskbarStrip.Attach.
        Create("BatteryStrip.Strip", User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE, User32.WS_POPUP, IntPtr.Zero);
    }

    protected override IntPtr? OnMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        int x = User32.LowWord(lParam), y = User32.HighWord(lParam);
        switch (message)
        {
            case User32.WM_MOUSEACTIVATE:
                return new IntPtr(User32.MA_NOACTIVATE);
            case User32.WM_LBUTTONUP:
                LeftClick?.Invoke(x, y);
                return IntPtr.Zero;
            case User32.WM_RBUTTONUP:
                RightClick?.Invoke(x, y);
                return IntPtr.Zero;
            case User32.WM_MOUSEMOVE:
                if (!_tracking)
                {
                    var track = new User32.TRACKMOUSEEVENT
                    {
                        cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(User32.TRACKMOUSEEVENT)),
                        dwFlags = 0x2, // TME_LEAVE
                        hwndTrack = Handle,
                    };
                    _tracking = User32.TrackMouseEvent(ref track);
                }
                Hover?.Invoke(x, y);
                return IntPtr.Zero;
            case User32.WM_MOUSELEAVE:
                _tracking = false;
                HoverEnd?.Invoke();
                return IntPtr.Zero;
            default:
                return null;
        }
    }

    protected override void OnDestroyed() => Destroyed?.Invoke();
}
