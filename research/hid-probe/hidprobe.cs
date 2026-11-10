// Passive HID probe: lists HID collections and listens to input reports.
// It never writes to a device: no output reports, no SetFeature, no GetFeature.
// Build: csc /out:hidprobe.exe hidprobe.cs
// Usage: hidprobe list
//        hidprobe listen <seconds> <vid-hex> [<vid-hex> ...]
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class HidProbe
{
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    // HIDP_VALUE_CAPS and HIDP_BUTTON_CAPS are both 72 bytes; ReportID is byte 2, UsagePage bytes 0-1.
    const int CapsSize = 72;

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, byte[] caps, ref ushort len, IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, byte[] caps, ref ushort len, IntPtr p);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder b, int len);
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);

    const uint GENERIC_READ = 0x80000000, SHARE_RW = 3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

    static List<string> HidPaths()
    {
        var result = new List<string>();
        Guid g;
        HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref data); i++)
            {
                int size;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, buf, size, out size, IntPtr.Zero))
                        result.Add(Marshal.PtrToStringUni(buf + 4));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    static string ReportIds(IntPtr pp, int type, ushort nValue, ushort nButton)
    {
        var ids = new SortedSet<int>();
        if (nValue > 0)
        {
            var b = new byte[nValue * CapsSize]; ushort n = nValue;
            if (HidP_GetValueCaps(type, b, ref n, pp) == 0x00110000)
                for (int i = 0; i < n; i++) ids.Add(b[i * CapsSize + 2]);
        }
        if (nButton > 0)
        {
            var b = new byte[nButton * CapsSize]; ushort n = nButton;
            if (HidP_GetButtonCaps(type, b, ref n, pp) == 0x00110000)
                for (int i = 0; i < n; i++) ids.Add(b[i * CapsSize + 2]);
        }
        var parts = new List<string>();
        foreach (int id in ids) parts.Add("0x" + id.ToString("X2"));
        return parts.Count == 0 ? "-" : string.Join(",", parts.ToArray());
    }

    class Info { public string Path; public ushort Vid, Pid, Ver; public HIDP_CAPS Caps; public string Product, In, Out, Feat; }

    static Info Describe(string path)
    {
        using (var h = CreateFile(path, 0, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
        {
            if (h.IsInvalid) return null;
            var info = new Info { Path = path };
            var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
            if (HidD_GetAttributes(h, ref a)) { info.Vid = a.VendorID; info.Pid = a.ProductID; info.Ver = a.VersionNumber; }
            var sb = new StringBuilder(128);
            info.Product = HidD_GetProductString(h, sb, 256) ? sb.ToString() : "";
            IntPtr pp;
            if (HidD_GetPreparsedData(h, out pp))
            {
                try
                {
                    HidP_GetCaps(pp, out info.Caps);
                    var c = info.Caps;
                    info.In = ReportIds(pp, 0, c.NumberInputValueCaps, c.NumberInputButtonCaps);
                    info.Out = ReportIds(pp, 1, c.NumberOutputValueCaps, c.NumberOutputButtonCaps);
                    info.Feat = ReportIds(pp, 2, c.NumberFeatureValueCaps, c.NumberFeatureButtonCaps);
                }
                finally { HidD_FreePreparsedData(pp); }
            }
            return info;
        }
    }

    static void List()
    {
        foreach (var p in HidPaths())
        {
            var i = Describe(p);
            if (i == null) { Console.WriteLine("(cannot open) " + p); continue; }
            Console.WriteLine("{0:X4}:{1:X4} v{2:X4} page=0x{3:X4} usage=0x{4:X2} in={5}[{6}] out={7}[{8}] feat={9}[{10}] \"{11}\"",
                i.Vid, i.Pid, i.Ver, i.Caps.UsagePage, i.Caps.Usage,
                i.Caps.InputReportByteLength, i.In, i.Caps.OutputReportByteLength, i.Out,
                i.Caps.FeatureReportByteLength, i.Feat, i.Product);
            Console.WriteLine("    " + p);
        }
    }

    static void Listen(int seconds, HashSet<ushort> vids)
    {
        var threads = new List<Thread>();
        var stop = DateTime.UtcNow.AddSeconds(seconds);
        object gate = new object();
        foreach (var p in HidPaths())
        {
            var i = Describe(p);
            if (i == null || !vids.Contains(i.Vid) || i.Caps.InputReportByteLength == 0) continue;
            // Skip mouse and keyboard collections: Windows opens them exclusively.
            if (i.Caps.UsagePage == 1 && (i.Caps.Usage == 2 || i.Caps.Usage == 6)) continue;
            var info = i;
            var t = new Thread(() =>
            {
                var tag = string.Format("{0:X4}:{1:X4} page=0x{2:X4}/0x{3:X2}", info.Vid, info.Pid, info.Caps.UsagePage, info.Caps.Usage);
                using (var h = CreateFile(info.Path, GENERIC_READ, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero))
                {
                    if (h.IsInvalid) { lock (gate) Console.WriteLine("{0}: open failed ({1})", tag, new Win32Exception().Message); return; }
                    using (var fs = new FileStream(h, FileAccess.Read, info.Caps.InputReportByteLength, true))
                    {
                        lock (gate) Console.WriteLine("{0}: listening", tag);
                        var buf = new byte[info.Caps.InputReportByteLength];
                        while (DateTime.UtcNow < stop)
                        {
                            var ar = fs.BeginRead(buf, 0, buf.Length, null, null);
                            int left = (int)Math.Max(0, (stop - DateTime.UtcNow).TotalMilliseconds);
                            if (!ar.AsyncWaitHandle.WaitOne(left)) break;
                            int n;
                            try { n = fs.EndRead(ar); } catch (IOException ex) { lock (gate) Console.WriteLine("{0}: {1}", tag, ex.Message); break; }
                            lock (gate) Console.WriteLine("{0:HH:mm:ss.fff} {1}: {2}", DateTime.Now, tag, BitConverter.ToString(buf, 0, n));
                        }
                    }
                }
            });
            t.IsBackground = true;
            t.Start();
            threads.Add(t);
        }
        Thread.Sleep(seconds * 1000 + 200);
        Console.WriteLine("done");
    }

    static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "list") { List(); return 0; }
        if (args.Length >= 3 && args[0] == "listen")
        {
            var vids = new HashSet<ushort>();
            for (int k = 2; k < args.Length; k++) vids.Add(Convert.ToUInt16(args[k], 16));
            Listen(int.Parse(args[1]), vids);
            return 0;
        }
        Console.WriteLine("usage: hidprobe list | hidprobe listen <seconds> <vid-hex>...");
        return 1;
    }
}
