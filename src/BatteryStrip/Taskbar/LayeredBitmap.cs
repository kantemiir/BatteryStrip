using System;
using System.Runtime.InteropServices;
using BatteryStrip.Rendering;

namespace BatteryStrip.Taskbar;

/// <summary>Puts a <see cref="Surface"/> on a layered window with per-pixel transparency.</summary>
internal static class LayeredBitmap
{
    /// <summary>Replaces the window's image and size. The position is left unchanged.</summary>
    /// <returns>False when Windows refused; the caller treats that as a broken attachment.</returns>
    public static bool Show(IntPtr hwnd, Surface surface)
    {
        IntPtr screen = User32.GetDC(IntPtr.Zero);
        IntPtr memory = User32.CreateCompatibleDC(screen);
        var header = new User32.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf(typeof(User32.BITMAPINFOHEADER)),
            biWidth = surface.Width,
            biHeight = -surface.Height, // negative: rows from the top, like Surface
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr bitmap = User32.CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr previous = IntPtr.Zero;
        try
        {
            if (bitmap == IntPtr.Zero) return false;
            Marshal.Copy((int[])(object)surface.Pixels, 0, bits, surface.Pixels.Length);
            previous = User32.SelectObject(memory, bitmap);

            var size = new User32.SIZE { Cx = surface.Width, Cy = surface.Height };
            var origin = new User32.POINT();
            var blend = new User32.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = User32.AC_SRC_ALPHA };
            return User32.UpdateLayeredWindow(hwnd, screen, IntPtr.Zero, ref size, memory, ref origin, 0, ref blend, User32.ULW_ALPHA);
        }
        finally
        {
            if (previous != IntPtr.Zero) User32.SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) User32.DeleteObject(bitmap);
            User32.DeleteDC(memory);
            User32.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
