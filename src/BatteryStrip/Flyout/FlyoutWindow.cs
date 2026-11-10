using System;
using System.Runtime.InteropServices;
using System.Threading;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Rendering;
using BatteryStrip.Taskbar;

namespace BatteryStrip.Flyout;

/// <summary>What the flyout's settings button offers. Every action must be safe to call from the flyout thread.</summary>
internal sealed record FlyoutActions(
    Func<bool> GetWarnings,
    Action<bool> SetWarnings,
    Action RefreshDevices,
    Action OpenLog,
    Action Exit);

/// <summary>
/// The window that opens above the strip: every device with its name, state and battery. It runs on its own
/// thread, so nothing it does can slow down the taskbar, and closes as soon as it loses focus.
/// </summary>
internal sealed class FlyoutWindow : NativeWindow
{
    private const int WM_APP_TOGGLE = User32.WM_APP + 20;
    private const int WM_APP_DEVICES = User32.WM_APP + 21;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int VK_ESCAPE = 0x1B;
    private const int WA_INACTIVE = 0;
    private const float GapAboveTaskbar = 12;

    /// <summary>A click on the strip that closed the flyout should not reopen it at once.</summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);

    private static readonly IntPtr RefreshTimer = new(1);

    private readonly DeviceStore _store;
    private readonly FlyoutActions _actions;
    private readonly GlyphCache _glyphs = new();
    private readonly FlyoutPainter _painter;
    private readonly object _anchorGate = new();
    private User32.RECT _anchor;
    private FlyoutPainter.Image? _image;
    private bool _gearHovered, _tracking, _menuOpen;
    private float _scale = 1;
    private DateTime _hiddenAt = DateTime.MinValue;

    private FlyoutWindow(DeviceStore store, FlyoutActions actions)
    {
        _store = store;
        _actions = actions;
        _painter = new FlyoutPainter(_glyphs, new StripPainter(_glyphs));
        Create("BatteryStrip.Flyout", User32.WS_EX_TOOLWINDOW | User32.WS_EX_TOPMOST, User32.WS_POPUP, IntPtr.Zero, classStyle: CS_DROPSHADOW);

        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        _store.Changed += () => User32.PostMessage(Handle, WM_APP_DEVICES, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Starts the flyout's own thread and returns once the window exists.</summary>
    public static FlyoutWindow StartThread(DeviceStore store, FlyoutActions actions)
    {
        FlyoutWindow? window = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                window = new FlyoutWindow(store, actions);
            }
            finally
            {
                ready.Set();
            }
            while (User32.GetMessage(out User32.MSG message, IntPtr.Zero, 0, 0) > 0)
            {
                User32.TranslateMessage(ref message);
                User32.DispatchMessage(ref message);
            }
        })
        {
            IsBackground = true,
            Name = "Flyout",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return window ?? throw new InvalidOperationException("The flyout window could not be created.");
    }

    /// <summary>Opens the flyout above <paramref name="anchor"/> (screen pixels), or closes it if open. Any thread.</summary>
    public void Toggle(User32.RECT anchor)
    {
        lock (_anchorGate) _anchor = anchor;
        User32.PostMessage(Handle, WM_APP_TOGGLE, IntPtr.Zero, IntPtr.Zero);
    }

    protected override IntPtr? OnMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WM_APP_TOGGLE:
                if (User32.IsWindowVisible(Handle)) Hide();
                else if (DateTime.UtcNow - _hiddenAt > ReopenGuard) Open();
                return IntPtr.Zero;

            case WM_APP_DEVICES:
                if (User32.IsWindowVisible(Handle)) Open();
                return IntPtr.Zero;

            case User32.WM_TIMER:
                if (User32.IsWindowVisible(Handle)) Open();
                return IntPtr.Zero;

            case User32.WM_ACTIVATE:
                if ((User32.LowWord(wParam) & 0xFFFF) == WA_INACTIVE && !_menuOpen) Hide();
                return IntPtr.Zero;

            case User32.WM_KEYDOWN:
                if ((int)wParam == VK_ESCAPE) Hide();
                return IntPtr.Zero;

            case User32.WM_MOUSEMOVE:
                TrackLeave();
                SetGearHover(IsOnGear(lParam));
                return IntPtr.Zero;

            case User32.WM_MOUSELEAVE:
                _tracking = false;
                SetGearHover(false);
                return IntPtr.Zero;

            case WM_LBUTTONDOWN:
                return IntPtr.Zero;

            case User32.WM_LBUTTONUP:
                if (IsOnGear(lParam)) ShowSettingsMenu();
                return IntPtr.Zero;

            case User32.WM_PAINT:
                Paint();
                return IntPtr.Zero;

            case User32.WM_ERASEBKGND:
                return new IntPtr(1);

            case User32.WM_DPICHANGED:
                if (User32.IsWindowVisible(Handle)) Open();
                return IntPtr.Zero;

            default:
                return null;
        }
    }

    /// <summary>Lays out, paints and shows the flyout; also used to refresh it while open.</summary>
    private void Open()
    {
        User32.RECT anchor;
        lock (_anchorGate) anchor = _anchor;

        IntPtr monitor = MonitorFromPoint(new User32.POINT { X = (anchor.Left + anchor.Right) / 2, Y = (anchor.Top + anchor.Bottom) / 2 }, User32.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        GetMonitorInfo(monitor, ref info);
        _scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi / 96f : 1f;

        Palette palette = Theme.Current();
        string footer = _actions.GetWarnings()
            ? $"Предупреждать при {ChargeLevels.LowThreshold}% и {ChargeLevels.CriticalThreshold}%"
            : "Предупреждения выключены";
        _image = _painter.Paint(_store.Snapshot, palette, _scale, _gearHovered, DateTime.UtcNow, footer);

        int dark = palette.IsLight ? 0 : 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        // Above the strip, right edges aligned, kept inside the screen's work area.
        int width = _image.Surface.Width, height = _image.Surface.Height;
        int gap = (int)Math.Round(GapAboveTaskbar * _scale);
        int x = Math.Max(info.rcWork.Left + gap, Math.Min(anchor.Right - width + gap, info.rcWork.Right - width - gap));
        int y = info.rcWork.Bottom - height - gap;

        bool wasVisible = User32.IsWindowVisible(Handle);
        User32.SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, x, y, width, height, User32.SWP_SHOWWINDOW);
        InvalidateRect(Handle, IntPtr.Zero, false);
        if (!wasVisible)
        {
            User32.SetForegroundWindow(Handle);
            User32.SetTimer(Handle, RefreshTimer, 30_000, IntPtr.Zero);
        }
    }

    private void Hide()
    {
        if (!User32.IsWindowVisible(Handle)) return;
        User32.KillTimer(Handle, RefreshTimer);
        User32.ShowWindow(Handle, User32.SW_HIDE);
        _hiddenAt = DateTime.UtcNow;
        _gearHovered = false;
    }

    private void Paint()
    {
        var ps = new PAINTSTRUCT();
        IntPtr dc = BeginPaint(Handle, ref ps);
        try
        {
            if (_image == null) return;
            Surface surface = _image.Surface;
            var header = new BITMAPINFO
            {
                biSize = Marshal.SizeOf(typeof(User32.BITMAPINFOHEADER)),
                biWidth = surface.Width,
                biHeight = -surface.Height,
                biPlanes = 1,
                biBitCount = 32,
            };
            SetDIBitsToDevice(dc, 0, 0, surface.Width, surface.Height, 0, 0, 0, surface.Height, surface.Pixels, ref header, 0);
        }
        finally
        {
            EndPaint(Handle, ref ps);
        }
    }

    private bool IsOnGear(IntPtr lParam)
    {
        if (_image == null) return false;
        int x = User32.LowWord(lParam), y = User32.HighWord(lParam);
        RectF gear = _image.Gear;
        return x >= gear.X && x < gear.Right && y >= gear.Y && y < gear.Bottom;
    }

    private void SetGearHover(bool hovered)
    {
        if (hovered == _gearHovered) return;
        _gearHovered = hovered;
        Open();
    }

    private void TrackLeave()
    {
        if (_tracking) return;
        var track = new User32.TRACKMOUSEEVENT
        {
            cbSize = Marshal.SizeOf(typeof(User32.TRACKMOUSEEVENT)),
            dwFlags = 0x2, // TME_LEAVE
            hwndTrack = Handle,
        };
        _tracking = User32.TrackMouseEvent(ref track);
    }

    private void ShowSettingsMenu()
    {
        const int Autostart = 1, Warnings = 2, Refresh = 3, OpenLog = 4, Exit = 5;
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, App.Autostart.IsEnabled ? MF_CHECKED : 0, new IntPtr(Autostart), "Запускать вместе с Windows");
            AppendMenu(menu, _actions.GetWarnings() ? MF_CHECKED : 0, new IntPtr(Warnings), "Предупреждать о низком заряде");
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenu(menu, 0, new IntPtr(Refresh), "Обновить устройства");
            AppendMenu(menu, 0, new IntPtr(OpenLog), "Открыть журнал");
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenu(menu, 0, new IntPtr(Exit), "Выход");

            if (_image == null || !User32.GetWindowRect(Handle, out User32.RECT window)) return;
            int x = window.Left + (int)_image.Gear.X, y = window.Top + (int)_image.Gear.Bottom;

            _menuOpen = true;
            int choice;
            try
            {
                choice = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, Handle, IntPtr.Zero);
            }
            finally
            {
                _menuOpen = false;
            }

            switch (choice)
            {
                case Autostart:
                    App.Autostart.Set(!App.Autostart.IsEnabled, AppInfo.ExePath);
                    break;
                case Warnings:
                    _actions.SetWarnings(!_actions.GetWarnings());
                    Open();
                    break;
                case Refresh:
                    _actions.RefreshDevices();
                    break;
                case OpenLog:
                    Hide();
                    _actions.OpenLog();
                    break;
                case Exit:
                    Hide();
                    _actions.Exit();
                    break;
            }

            // The menu took the focus; give it back so the flyout still closes on the next outside click.
            if (User32.IsWindowVisible(Handle)) User32.SetForegroundWindow(Handle);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private const int CS_DROPSHADOW = 0x00020000;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;
    private const int MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    private const int TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public User32.RECT rcMonitor, rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public User32.RECT rcPaint;
        public bool fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public int bmiColors;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(User32.POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("gdi32.dll")]
    private static extern int SetDIBitsToDevice(IntPtr dc, int x, int y, int width, int height, int sourceX, int sourceY,
        int startScan, int scanLines, uint[] bits, ref BITMAPINFO info, int colorUse);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, int flags, IntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, int flags, int x, int y, IntPtr owner, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);
}
