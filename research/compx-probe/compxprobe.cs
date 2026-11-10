// Test of the battery read request for COMPX mice (Lamzu, Ajazz, Attack Shark...), VID 25A7.
// Without --run it sends NOTHING and only listens to what the vendor software's polling returns.
// With --run it sends one GetBatteryLevel request (command 0x04), the same one the vendor software sends.
//
// Build: csc /platform:x64 /out:compxprobe.exe compxprobe.cs
// Usage: compxprobe [--run]
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

static class CompxProbe
{
    const ushort Vid = 0x25A7;
    const byte RequestReport = 0x08, ReplyReport = 0x09, GetBatteryLevel = 0x04;
    const int FrameLength = 17;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)] public ushort[] Rest;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);

    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, SHARE_RW = 3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

    static readonly Stopwatch Clock = Stopwatch.StartNew();

    static void Log(string format, params object[] args)
    {
        Console.WriteLine("{0:HH:mm:ss.fff} {1,6} ms  {2}", DateTime.Now, Clock.ElapsedMilliseconds, string.Format(format, args));
    }

    static byte Checksum(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < FrameLength - 1; i++) sum += frame[i];
        return (byte)(0x55 - sum);
    }

    static byte[] BatteryRequest()
    {
        var frame = new byte[FrameLength];
        frame[0] = RequestReport;
        frame[1] = GetBatteryLevel;
        frame[FrameLength - 1] = Checksum(frame);
        return frame;
    }

    static string Describe(byte[] r)
    {
        if (r.Length != FrameLength || r[0] != ReplyReport) return "not a COMPX reply";
        if (Checksum(r) != r[FrameLength - 1]) return "BAD CHECKSUM";
        if (r[1] != GetBatteryLevel) return string.Format("reply to command 0x{0:X2}", r[1]);
        if (r[5] == 0 && r[6] == 0 && r[7] == 0) return "battery reply: mouse asleep or off";
        return string.Format("battery reply: status={0} len={1} percent={2} charging={3}", r[2], r[5], r[6], r[7]);
    }

    static void FindCollections(out string requestPath, out string replyPath)
    {
        requestPath = replyPath = null;
        Guid g;
        HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref data); i++)
            {
                int size;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(size);
                string path = null;
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, buf, size, out size, IntPtr.Zero))
                        path = Marshal.PtrToStringUni(buf + 4);
                }
                finally { Marshal.FreeHGlobal(buf); }
                if (path == null) continue;

                using (var h = CreateFile(path, 0, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) continue;
                    var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                    if (!HidD_GetAttributes(h, ref a) || a.VendorID != Vid) continue;
                    IntPtr pp;
                    if (!HidD_GetPreparsedData(h, out pp)) continue;
                    try
                    {
                        HIDP_CAPS caps;
                        HidP_GetCaps(pp, out caps);
                        if (caps.UsagePage < 0xFF00) continue;
                        if (caps.FeatureReportByteLength == FrameLength) requestPath = path;
                        if (caps.InputReportByteLength == FrameLength) replyPath = path;
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    static int Main(string[] args)
    {
        bool run = args.Length > 0 && args[0] == "--run";
        string requestPath, replyPath;
        FindCollections(out requestPath, out replyPath);
        if (requestPath == null || replyPath == null) { Console.WriteLine("COMPX receiver (VID 25A7) not found"); return 2; }
        Console.WriteLine("Request collection: " + requestPath);
        Console.WriteLine("Reply collection:   " + replyPath);
        var request = BatteryRequest();
        Console.WriteLine("Request frame:      " + BitConverter.ToString(request).Replace('-', ' '));

        using (var readHandle = CreateFile(replyPath, GENERIC_READ, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero))
        {
            if (readHandle.IsInvalid) { Console.WriteLine("Cannot open reply collection: " + new Win32Exception().Message); return 2; }
            using (var stream = new FileStream(readHandle, FileAccess.Read, FrameLength, true))
            {
                var buffer = new byte[FrameLength];
                IAsyncResult pending = null;
                Func<int, byte[]> read = timeout =>
                {
                    if (pending == null) pending = stream.BeginRead(buffer, 0, buffer.Length, null, null);
                    if (!pending.AsyncWaitHandle.WaitOne(timeout)) return null;
                    int n = stream.EndRead(pending);
                    pending = null;
                    var copy = new byte[n];
                    Array.Copy(buffer, copy, n);
                    return copy;
                };

                // Step 1: learn when the vendor software polls, so our request lands between its polls.
                Log("listening for the vendor software's poll (up to 35 s)");
                var first = read(35000);
                bool vendorPolls = first != null;
                if (vendorPolls) Log("vendor poll reply: {0}  -> {1}", BitConverter.ToString(first).Replace('-', ' '), Describe(first));
                else Log("no reply seen: the vendor software is not polling");

                if (!run) { Console.WriteLine("Dry run: nothing was sent."); return 0; }

                // Step 2: stay quiet for 12 s (the vendor polls every 30 s), then send one request.
                long quietUntil = Clock.ElapsedMilliseconds + 12000;
                while (Clock.ElapsedMilliseconds < quietUntil)
                {
                    var stray = read((int)Math.Max(1, quietUntil - Clock.ElapsedMilliseconds));
                    if (stray != null) Log("unexpected report while waiting: {0}", BitConverter.ToString(stray).Replace('-', ' '));
                }

                using (var control = CreateFile(requestPath, GENERIC_READ | GENERIC_WRITE, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (control.IsInvalid) { Console.WriteLine("Cannot open request collection: " + new Win32Exception().Message); return 2; }
                    Log("SET  {0}", BitConverter.ToString(request).Replace('-', ' '));
                    if (!HidD_SetFeature(control, request, request.Length))
                    {
                        Log("SetFeature failed: {0}", new Win32Exception(Marshal.GetLastWin32Error()).Message);
                        return 3;
                    }
                    long sentAt = Clock.ElapsedMilliseconds;
                    var reply = read(1000);
                    if (reply == null) { Log("NO REPLY within 1 s"); return 3; }
                    Log("reply after {0} ms: {1}  -> {2}", Clock.ElapsedMilliseconds - sentAt, BitConverter.ToString(reply).Replace('-', ' '), Describe(reply));
                }

                // Step 3: make sure the vendor software's polling still works afterwards.
                if (vendorPolls)
                {
                    var next = read(32000);
                    if (next != null) Log("vendor poll after our request: {0}  -> {1}", BitConverter.ToString(next).Replace('-', ' '), Describe(next));
                    else Log("vendor poll did not arrive within 32 s");
                }
                return 0;
            }
        }
    }
}
