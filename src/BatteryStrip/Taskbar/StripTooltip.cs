using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BatteryStrip.Rendering;

namespace BatteryStrip.Taskbar;

/// <summary>
/// The standard Windows tooltip over the strip: one tool per device slot, so hovering a battery shows the
/// device's name, charge and state, like a tray icon does. Lives on the strip's thread.
/// </summary>
internal sealed class StripTooltip : IDisposable
{
    private const int TTS_ALWAYSTIP = 0x01, TTS_NOPREFIX = 0x02;
    private const int TTF_SUBCLASS = 0x0010;
    private const int WM_USER = 0x0400;
    private const int TTM_SETMAXTIPWIDTH = WM_USER + 24, TTM_ADDTOOLW = WM_USER + 50, TTM_DELTOOLW = WM_USER + 51;
    private const int TTM_NEWTOOLRECTW = WM_USER + 52, TTM_UPDATETIPTEXTW = WM_USER + 57;
    private const int WS_EX_TOPMOST = 0x0008, WS_EX_TRANSPARENT = 0x0020;

    private readonly IntPtr _tip;
    private IntPtr _window;
    private readonly List<(string Id, User32.RECT Rect)> _tools = new();

    /// <param name="owner">A top-level window of this thread; the tooltip is a pop-up owned by it.</param>
    public StripTooltip(IntPtr owner)
    {
        _tip = User32.CreateWindowEx(WS_EX_TOPMOST | WS_EX_TRANSPARENT, "tooltips_class32", "", User32.WS_POPUP | TTS_ALWAYSTIP | TTS_NOPREFIX,
            0, 0, 0, 0, owner, IntPtr.Zero, User32.GetModuleHandle(null), IntPtr.Zero);
        if (_tip == IntPtr.Zero) return;
        SendMessage(_tip, TTM_SETMAXTIPWIDTH, IntPtr.Zero, new IntPtr(480)); // allows the second line
    }

    /// <summary>Follows the taskbar's theme; Windows 11 has a dark tooltip style.</summary>
    public void SetDark(bool dark)
    {
        if (_tip != IntPtr.Zero) SetWindowTheme(_tip, dark ? "DarkMode_Explorer" : null, null);
    }

    /// <summary>One tool per slot of the painted strip. Unchanged slots are left alone, so a shown tooltip stays.</summary>
    public void SetSlots(IntPtr window, IReadOnlyList<DeviceSlot> slots)
    {
        if (_tip == IntPtr.Zero) return;
        bool sameSet = window == _window && slots.Count == _tools.Count;
        for (int i = 0; sameSet && i < slots.Count; i++) sameSet = slots[i].DeviceId == _tools[i].Id;

        if (!sameSet)
        {
            Clear();
            _window = window;
            for (int i = 0; i < slots.Count; i++)
            {
                User32.RECT rect = ToRect(slots[i].Bounds);
                var info = Info(i, rect, " ");
                SendMessage(_tip, TTM_ADDTOOLW, IntPtr.Zero, ref info);
                _tools.Add((slots[i].DeviceId, rect));
            }
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            User32.RECT rect = ToRect(slots[i].Bounds);
            if (rect.Equals(_tools[i].Rect)) continue;
            var info = Info(i, rect, null);
            SendMessage(_tip, TTM_NEWTOOLRECTW, IntPtr.Zero, ref info);
            _tools[i] = (_tools[i].Id, rect);
        }
    }

    /// <summary>Sets the text of a device's tooltip; called when the mouse reaches it, so the age in it is fresh.</summary>
    public void SetText(string deviceId, string text)
    {
        if (_tip == IntPtr.Zero) return;
        int index = _tools.FindIndex(t => t.Id == deviceId);
        if (index < 0) return;
        var info = Info(index, _tools[index].Rect, text);
        SendMessage(_tip, TTM_UPDATETIPTEXTW, IntPtr.Zero, ref info);
    }

    public void Dispose()
    {
        Clear();
        if (_tip != IntPtr.Zero) User32.DestroyWindow(_tip);
    }

    private void Clear()
    {
        for (int i = 0; i < _tools.Count && _window != IntPtr.Zero && User32.IsWindow(_window); i++)
        {
            var info = Info(i, default, null);
            SendMessage(_tip, TTM_DELTOOLW, IntPtr.Zero, ref info);
        }
        _tools.Clear();
        _window = IntPtr.Zero;
    }

    private TOOLINFO Info(int index, User32.RECT rect, string? text) => new()
    {
        cbSize = Marshal.SizeOf(typeof(TOOLINFO)),
        uFlags = TTF_SUBCLASS,
        hwnd = _window,
        uId = new IntPtr(index + 1),
        rect = rect,
        lpszText = text,
    };

    private static User32.RECT ToRect(RectF bounds) => new()
    {
        Left = (int)Math.Floor(bounds.X),
        Top = (int)Math.Floor(bounds.Y),
        Right = (int)Math.Ceiling(bounds.Right),
        Bottom = (int)Math.Ceiling(bounds.Bottom),
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TOOLINFO
    {
        public int cbSize;
        public int uFlags;
        public IntPtr hwnd;
        public IntPtr uId;
        public User32.RECT rect;
        public IntPtr hinst;
        public string? lpszText;
        public IntPtr lParam;
        public IntPtr lpReserved;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, ref TOOLINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
}
