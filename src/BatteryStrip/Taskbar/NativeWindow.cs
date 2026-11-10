using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using BatteryStrip.App;

namespace BatteryStrip.Taskbar;

/// <summary>
/// Base for a plain Win32 window owned by the calling thread. Exceptions in message handling are logged
/// and swallowed: the strip shares its input queue with the taskbar and must never take it down.
/// </summary>
internal abstract class NativeWindow : IDisposable
{
    // Windows of several threads (the strip, the flyout) share these, hence the concurrent map and the lock.
    private static readonly Dictionary<string, User32.WndProc> RegisteredClasses = new();
    private static readonly ConcurrentDictionary<IntPtr, NativeWindow> Live = new();

    [ThreadStatic]
    private static NativeWindow? _creating;

    public IntPtr Handle { get; private set; }

    protected void Create(string className, int exStyle, int style, IntPtr parent, int x = 0, int y = 0, int width = 1, int height = 1, int classStyle = 0)
    {
        RegisterClass(className, classStyle);
        _creating = this;
        try
        {
            IntPtr hwnd = User32.CreateWindowEx(exStyle, className, AppInfo.DisplayName, style, x, y, width, height,
                parent, IntPtr.Zero, User32.GetModuleHandle(null), IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            Handle = hwnd;
        }
        finally
        {
            _creating = null;
        }
    }

    public virtual void Dispose()
    {
        if (Handle != IntPtr.Zero && User32.IsWindow(Handle)) User32.DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }

    /// <summary>Handles a message; return null to fall back to the default handling.</summary>
    protected abstract IntPtr? OnMessage(int message, IntPtr wParam, IntPtr lParam);

    /// <summary>Called when Windows destroyed the window, including when its parent went away.</summary>
    protected virtual void OnDestroyed()
    {
    }

    private static void RegisterClass(string className, int classStyle)
    {
        lock (RegisteredClasses) RegisterClassLocked(className, classStyle);
    }

    private static void RegisterClassLocked(string className, int classStyle)
    {
        if (RegisteredClasses.ContainsKey(className)) return;
        User32.WndProc proc = Dispatch;
        var windowClass = new User32.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf(typeof(User32.WNDCLASSEX)),
            style = classStyle,
            lpfnWndProc = proc,
            hInstance = User32.GetModuleHandle(null),
            hCursor = User32.LoadCursor(IntPtr.Zero, new IntPtr(32512)), // IDC_ARROW
            lpszClassName = className,
        };
        if (User32.RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        RegisteredClasses.Add(className, proc); // keeps the delegate alive for as long as the class exists
    }

    private static IntPtr Dispatch(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        if (!Live.TryGetValue(hwnd, out NativeWindow? window) && _creating != null)
        {
            window = _creating;
            window.Handle = hwnd;
            Live[hwnd] = window;
        }

        if (window != null)
        {
            try
            {
                if (message == User32.WM_DESTROY)
                {
                    Live.TryRemove(hwnd, out _);
                    window.Handle = IntPtr.Zero;
                    window.OnDestroyed();
                }
                else if (window.OnMessage(message, wParam, lParam) is IntPtr result)
                {
                    return result;
                }
            }
            catch (Exception exception)
            {
                Log.Error($"{window.GetType().Name} failed to handle message 0x{message:X4}", exception);
            }
        }
        return User32.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
