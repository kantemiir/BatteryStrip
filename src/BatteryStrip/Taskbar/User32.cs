using System;
using System.Runtime.InteropServices;

namespace BatteryStrip.Taskbar;

/// <summary>Window functions of user32, gdi32 and dwmapi used by the program's windows.</summary>
internal static class User32
{
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int GWL_STYLE = -16;

    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040, SWP_HIDEWINDOW = 0x0080;
    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;

    public const int WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_SETTINGCHANGE = 0x001A, WM_TIMER = 0x0113;
    public const int WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0, WM_POWERBROADCAST = 0x0218;
    public const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_MOUSELEAVE = 0x02A3;
    public const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    public const int WM_NCHITTEST = 0x0084, HTCLIENT = 1;
    public const int WM_ACTIVATE = 0x0006, WM_KEYDOWN = 0x0100, WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014;
    public const int WM_APP = 0x8000;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;

    public const uint ULW_ALPHA = 0x02;
    public const byte AC_SRC_ALPHA = 0x01;

    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public override string ToString() => $"[{Left},{Top},{Right},{Bottom}]";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Cx, Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr Hwnd;
        public int Message;
        public IntPtr WParam, LParam;
        public int Time;
        public POINT Point;
        public int Private;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public WndProc lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    public delegate IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG message, IntPtr hwnd, int min, int max);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr screenDc, IntPtr position, ref SIZE size,
        IntPtr sourceDc, ref POINT sourcePosition, int colorKey, ref BLENDFUNCTION blend, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("user32.dll")]
    public static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, int milliseconds, IntPtr callback);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(IntPtr hwnd, IntPtr id);

    [DllImport("user32.dll")]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT track);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, int usage, out IntPtr bits, IntPtr section, int offset);

    public static short LowWord(IntPtr value) => unchecked((short)(long)value);

    public static short HighWord(IntPtr value) => unchecked((short)((long)value >> 16));
}
