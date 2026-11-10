// Saves a screenshot of a screen region with BitBlt + CAPTUREBLT (layered windows included).
// Usage: capture <out.png> [left top width height]   default: the primary taskbar
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

static class Capture
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr d);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int op);

    static int Main(string[] a)
    {
        SetProcessDPIAware();
        int l, t, w, h;
        if (a.Length >= 5) { l = int.Parse(a[1]); t = int.Parse(a[2]); w = int.Parse(a[3]); h = int.Parse(a[4]); }
        else { RECT r; GetWindowRect(FindWindow("Shell_TrayWnd", null), out r); l = r.L; t = r.T; w = r.R - r.L; h = r.B - r.T; }
        using (var b = new Bitmap(w, h))
        using (var g = Graphics.FromImage(b))
        {
            IntPtr screen = GetDC(IntPtr.Zero), dst = g.GetHdc();
            BitBlt(dst, 0, 0, w, h, screen, l, t, 0x00CC0020 | 0x40000000);
            g.ReleaseHdc(dst);
            ReleaseDC(IntPtr.Zero, screen);
            b.Save(a[0], ImageFormat.Png);
        }
        return 0;
    }
}
