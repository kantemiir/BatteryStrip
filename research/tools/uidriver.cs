// Drives the program's windows with posted messages, without moving the real mouse.
// Usage: uidriver click | rclick | gear | menu <downs> | esc | state
using System;
using System.Runtime.InteropServices;
using System.Threading;

static class UiDriver
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);

    static IntPtr Strip() { return FindWindowEx(FindWindow("Shell_TrayWnd", null), IntPtr.Zero, "BatteryStrip.Strip", null); }
    static IntPtr Flyout() { return FindWindow("BatteryStrip.Flyout", null); }
    static IntPtr Pack(int x, int y) { return new IntPtr((y << 16) | (x & 0xFFFF)); }

    static void Key(IntPtr h, int vk)
    {
        PostMessage(h, 0x0100, new IntPtr(vk), IntPtr.Zero);
        PostMessage(h, 0x0101, new IntPtr(vk), IntPtr.Zero);
        Thread.Sleep(150);
    }

    static int Main(string[] a)
    {
        string cmd = a.Length > 0 ? a[0] : "state";
        IntPtr strip = Strip(), flyout = Flyout();
        switch (cmd)
        {
            case "click": PostMessage(strip, 0x0202, IntPtr.Zero, Pack(30, 24)); break;
            case "rclick": PostMessage(strip, 0x0205, IntPtr.Zero, Pack(30, 24)); break;
            case "gear":
                {
                    RECT r; GetClientRect(flyout, out r);
                    IntPtr at = Pack(r.R - 22, r.B - 22);
                    PostMessage(flyout, 0x0200, IntPtr.Zero, at);
                    PostMessage(flyout, 0x0202, IntPtr.Zero, at);
                    break;
                }
            case "menu":
                {
                    Thread.Sleep(500);
                    IntPtr menu = FindWindow("#32768", null);
                    if (menu == IntPtr.Zero) { Console.WriteLine("no menu"); return 1; }
                    for (int i = 0; i < int.Parse(a[1]); i++) Key(menu, 0x28);
                    Key(menu, 0x0D);
                    break;
                }
            case "esc": Key(flyout, 0x1B); break;
        }
        Thread.Sleep(300);
        RECT s, f;
        GetWindowRect(Strip(), out s);
        GetWindowRect(Flyout(), out f);
        Console.WriteLine("strip={0} [{1},{2},{3},{4}] flyoutVisible={5} [{6},{7},{8},{9}]",
            Strip() != IntPtr.Zero, s.L, s.T, s.R, s.B, IsWindowVisible(Flyout()), f.L, f.T, f.R, f.B);
        return 0;
    }
}
