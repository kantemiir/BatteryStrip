using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Spike
{
    const int GWL_STYLE = -16;
    const int WS_CHILD = 0x40000000;
    const int WS_POPUP = unchecked((int)0x80000000);
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_SHOWWINDOW = 0x0040;
    const int W = 170, H = 32;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string t);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetParent(IntPtr c, IntPtr p);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int cy, uint f);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr d);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, IntPtr ptDst, ref SIZE s, IntPtr src, ref POINT ptSrc, int key, ref BLEND b, int flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr d);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr d);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int op);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr d, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

    static string Cls(IntPtr h)
    {
        var s = new StringBuilder(256);
        GetClassName(h, s, 256);
        return s.ToString();
    }

    static bool Paint(IntPtr h)
    {
        using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Segoe UI Emoji", 14f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.Clear(Color.FromArgb(1, 0, 0, 0));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString("\U0001F5B1 82     \U0001F3A7 47", font, Brushes.White, new RectangleF(0, 0, W, H), fmt);

            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hb = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(mem, hb);
            var size = new SIZE { CX = W, CY = H };
            var src = new POINT();
            var blend = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
            bool ok = UpdateLayeredWindow(h, screen, IntPtr.Zero, ref size, mem, ref src, 0, ref blend, 2);
            int err = Marshal.GetLastWin32Error();
            SelectObject(mem, old);
            DeleteObject(hb);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            Console.WriteLine("UpdateLayeredWindow ok={0} err={1}", ok, err);
            return ok;
        }
    }

    static void Report(string tag, IntPtr h, IntPtr tb)
    {
        RECT r;
        GetWindowRect(h, out r);
        var c = new POINT { X = (r.L + r.R) / 2, Y = (r.T + r.B) / 2 };
        var hit = WindowFromPoint(c);
        Console.WriteLine("{0}: parentIsTaskbar={1} visible={2} rect=[{3},{4},{5},{6}] hitIsOurs={7} hitClass={8}",
            tag, GetParent(h) == tb, IsWindowVisible(h), r.L, r.T, r.R, r.B, hit == h, Cls(hit));
    }

    static void Capture(IntPtr tb, string path)
    {
        RECT r;
        GetWindowRect(tb, out r);
        int w = r.R - r.L, h = r.B - r.T;
        using (var b = new Bitmap(w, h))
        using (var g = Graphics.FromImage(b))
        {
            IntPtr screen = GetDC(IntPtr.Zero), dst = g.GetHdc();
            bool ok = BitBlt(dst, 0, 0, w, h, screen, r.L, r.T, 0x00CC0020 | 0x40000000);
            g.ReleaseHdc(dst);
            ReleaseDC(IntPtr.Zero, screen);
            Console.WriteLine("BitBlt ok={0}", ok);
            b.Save(path, ImageFormat.Png);
        }
        Console.WriteLine("Saved capture: " + path);
    }

    [STAThread]
    static int Main(string[] args)
    {
        var watchdog = new Thread(() => { Thread.Sleep(20000); Environment.Exit(2); });
        watchdog.IsBackground = true;
        watchdog.Start();

        try
        {
            IntPtr tb = FindWindow("Shell_TrayWnd", null);
            IntPtr tray = FindWindowEx(tb, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tb == IntPtr.Zero || tray == IntPtr.Zero) { Console.WriteLine("Taskbar windows not found"); return 1; }

            RECT tbR, trayR;
            GetWindowRect(tb, out tbR);
            GetWindowRect(tray, out trayR);
            int x = trayR.L - tbR.L - W - 8;
            int y = (tbR.B - tbR.T - H) / 2;

            var win = new NativeWindow();
            win.CreateHandle(new CreateParams
            {
                Caption = "BatteryStripSpike",
                Style = WS_POPUP,
                ExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                Width = W,
                Height = H
            });
            IntPtr h = win.Handle;

            SetWindowLong(h, GWL_STYLE, (GetWindowLong(h, GWL_STYLE) & ~WS_POPUP) | WS_CHILD);
            IntPtr prev = SetParent(h, tb);
            Console.WriteLine("SetParent err={0} prev={1}", Marshal.GetLastWin32Error(), prev);
            bool moved = SetWindowPos(h, IntPtr.Zero, x, y, W, H, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            Console.WriteLine("SetWindowPos ok={0} x={1} y={2}", moved, x, y);
            Paint(h);

            int elapsed = 0;
            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (s, e) =>
            {
                elapsed++;
                try
                {
                    if (elapsed == 2) { Report("t=2s", h, tb); Capture(tb, args[0]); }
                    if (elapsed == 9) Report("t=9s", h, tb);
                }
                catch (Exception ex) { Console.WriteLine("Tick failed: " + ex.Message); }
                if (elapsed >= 10) { timer.Stop(); win.DestroyHandle(); Application.ExitThread(); }
            };
            timer.Start();
            Application.Run();
            Console.WriteLine("Closed cleanly");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAILED: " + ex);
            return 1;
        }
    }
}
