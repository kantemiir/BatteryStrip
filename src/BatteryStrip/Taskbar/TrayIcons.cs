using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Rendering;
using Microsoft.Win32;

namespace BatteryStrip.Taskbar;

/// <summary>
/// The fallback when the strip cannot live inside the taskbar: one notification-area icon per device,
/// showing its charge as a number. The icons are pinned next to the clock so they are always visible.
/// </summary>
internal sealed class TrayIcons : NativeWindow
{
    private const int WM_TRAY = User32.WM_APP + 10;
    private const int WM_DEVICES_CHANGED = User32.WM_APP + 11;

    private readonly DeviceStore _store;
    private readonly GlyphCache _glyphs = new();
    private readonly int _taskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");
    private readonly Dictionary<string, (Guid Guid, IntPtr Icon, string Signature)> _shown = new(StringComparer.Ordinal);
    private bool _enabled;

    /// <summary>An icon was clicked; the argument is the device id.</summary>
    public event Action<string>? Clicked;

    /// <summary>Right click on an icon, with the cursor position on screen.</summary>
    public event Action<int, int>? ContextMenuRequested;

    public TrayIcons(DeviceStore store)
    {
        _store = store;
        Create("BatteryStrip.TrayIcons", User32.WS_EX_TOOLWINDOW, User32.WS_POPUP, IntPtr.Zero);
        _store.Changed += () => User32.PostMessage(Handle, WM_DEVICES_CHANGED, IntPtr.Zero, IntPtr.Zero);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value) Update();
            else RemoveAll();
        }
    }

    public override void Dispose()
    {
        RemoveAll();
        _glyphs.Dispose();
        base.Dispose();
    }

    protected override IntPtr? OnMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            // Explorer forgot every icon; add them again.
            foreach (var entry in _shown.Values) DestroyIcon(entry.Icon);
            _shown.Clear();
            if (_enabled) Update();
            return IntPtr.Zero;
        }

        switch (message)
        {
            case WM_DEVICES_CHANGED:
                if (_enabled) Update();
                return IntPtr.Zero;

            case WM_TRAY:
                // NOTIFYICON_VERSION_4: the event is in the low word of lParam, the icon in the high word.
                int trayEvent = User32.LowWord(lParam);
                string? id = _shown.FirstOrDefault(p => IconId(p.Value.Guid) == User32.HighWord(lParam)).Key;
                if (trayEvent == NIN_SELECT || trayEvent == User32.WM_LBUTTONUP)
                {
                    if (id != null) Clicked?.Invoke(id);
                }
                else if (trayEvent == WM_CONTEXTMENU)
                {
                    ContextMenuRequested?.Invoke(User32.LowWord(wParam), User32.HighWord(wParam));
                }
                return IntPtr.Zero;

            default:
                return null;
        }
    }

    private void Update()
    {
        IReadOnlyList<DeviceSnapshot> devices = _store.Snapshot;
        foreach (string gone in _shown.Keys.Where(id => devices.All(d => d.Id != id)).ToArray()) Remove(gone);

        Palette palette = Theme.Current();
        int size = GetSystemMetrics(SM_CXSMICON);
        foreach (DeviceSnapshot device in devices)
        {
            string signature = $"{device.Percent}|{device.IsCharging}|{device.IsAsleep}|{device.Level}|{palette.IsLight}|{device.Name}";
            bool exists = _shown.TryGetValue(device.Id, out var current);
            if (exists && current.Signature == signature) continue;

            IntPtr icon = PaintIcon(device, palette, size);
            Guid guid = exists ? current.Guid : GuidFor(device.Id);
            var data = Data(guid);
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID | NIF_SHOWTIP;
            data.hIcon = icon;
            data.szTip = Tooltip(device);

            bool ok = exists ? Shell_NotifyIcon(NIM_MODIFY, ref data) : Add(ref data);
            if (exists) DestroyIcon(current.Icon);
            if (!ok)
            {
                DestroyIcon(icon);
                _shown.Remove(device.Id);
                Log.Warn($"Tray icon for {device.Id} could not be shown.");
                continue;
            }
            _shown[device.Id] = (guid, icon, signature);
        }

        // Explorer creates the settings entry a moment after an icon first appears, so pinning is retried on every update.
        foreach (var entry in _shown.Values) Pin(entry.Guid);
    }

    private bool Add(ref NOTIFYICONDATA data)
    {
        // A leftover icon with the same GUID (from a crash) blocks NIM_ADD.
        var stale = Data(data.guidItem);
        Shell_NotifyIcon(NIM_DELETE, ref stale);
        if (!Shell_NotifyIcon(NIM_ADD, ref data)) return false;
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        return Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    private void Remove(string id)
    {
        if (!_shown.TryGetValue(id, out var entry)) return;
        var data = Data(entry.Guid);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        DestroyIcon(entry.Icon);
        _shown.Remove(id);
    }

    private void RemoveAll()
    {
        foreach (string id in _shown.Keys.ToArray()) Remove(id);
    }

    private NOTIFYICONDATA Data(Guid guid) => new()
    {
        cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
        hWnd = Handle,
        uID = IconId(guid),
        uFlags = NIF_GUID,
        uCallbackMessage = WM_TRAY,
        guidItem = guid,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    private static int IconId(Guid guid) => BitConverter.ToUInt16(guid.ToByteArray(), 0);

    private static string Tooltip(DeviceSnapshot device)
    {
        string charge = device.Percent is int percent ? $"{percent} %" : "нет данных";
        string text = $"{device.Name}: {charge}\n{StatusText.For(device, DateTime.UtcNow)}";
        return text.Length > 127 ? text.Substring(0, 127) : text;
    }

    /// <summary>Stable per device, so Windows keeps the icon's "always show" setting between runs.</summary>
    private static Guid GuidFor(string deviceId)
    {
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("BatteryStrip.Tray." + deviceId));
        hash[7] = (byte)((hash[7] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash);
    }

    /// <summary>
    /// Marks the icon "always shown" next to the clock. Windows 11 keeps this per icon in
    /// HKCU\Control Panel\NotifyIconSettings; the key is created by Explorer when an icon first appears.
    /// Undocumented, so any failure is ignored.
    /// </summary>
    private static void Pin(Guid guid)
    {
        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (root == null) return;
            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? entry = root.OpenSubKey(name, writable: true);
                if (entry == null) continue;
                if (!string.Equals(entry.GetValue("ExecutablePath") as string, AppInfo.ExePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.GetValue("IconGuid") is string iconGuid && Guid.TryParse(iconGuid, out Guid stored) && stored != guid) continue;
                if (entry.GetValue("IsPromoted") is int promoted && promoted == 1) continue;
                entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Warn($"Cannot pin tray icon: {exception.Message}");
        }
    }

    /// <summary>The number in the device's colour; a dash when the device does not report its charge.</summary>
    private IntPtr PaintIcon(DeviceSnapshot device, Palette palette, int size)
    {
        var surface = new Surface(size, size);
        string text = device.Percent is int percent ? (percent >= 100 ? "99" : percent.ToString()) : "–";
        Rgba colour = device.Percent == null || device.IsAsleep ? palette.Dimmed
            : device.IsCharging ? palette.Charging
            : device.Level switch
            {
                ChargeLevel.Critical => palette.Critical,
                ChargeLevel.Low => palette.Low,
                _ => palette.Text,
            };

        // Two digits fill a small icon; the font is as large as fits its width.
        GlyphMask mask = _glyphs.Get(GlyphFont.NumberSemibold, size * 0.8f, text);
        for (float font = size * 0.8f; mask.Width > size && font > 6; font -= 0.5f) mask = _glyphs.Get(GlyphFont.NumberSemibold, font, text);
        surface.DrawMask(mask, (size - mask.Width) / 2, (size - mask.Height) / 2, colour);
        return IconFromSurface(surface);
    }

    private static IntPtr IconFromSurface(Surface surface)
    {
        var header = new User32.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf(typeof(User32.BITMAPINFOHEADER)),
            biWidth = surface.Width,
            biHeight = -surface.Height,
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr screen = User32.GetDC(IntPtr.Zero);
        IntPtr colour = User32.CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        User32.ReleaseDC(IntPtr.Zero, screen);
        Marshal.Copy((int[])(object)surface.Pixels, 0, bits, surface.Pixels.Length);
        IntPtr mask = CreateBitmap(surface.Width, surface.Height, 1, 1, IntPtr.Zero);
        try
        {
            var info = new ICONINFO { fIcon = true, hbmColor = colour, hbmMask = mask };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            User32.DeleteObject(colour);
            User32.DeleteObject(mask);
        }
    }

    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const int NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_GUID = 0x20, NIF_SHOWTIP = 0x80;
    private const int NOTIFYICON_VERSION_4 = 4;
    private const int NIN_SELECT = 0x0400, WM_CONTEXTMENU = 0x007B;
    private const int SM_CXSMICON = 49;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, int planes, int bitsPerPixel, IntPtr bits);
}
