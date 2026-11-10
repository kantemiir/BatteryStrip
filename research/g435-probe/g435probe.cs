// Test of the only known way to read the Logitech G435 battery through the 046D:0ACB receiver.
// Protocol source: github.com/deddroid/logitech-g435-battery (MIT) and the endes article on dev.to.
//
// Without --run the tool sends NOTHING: it finds the receiver and prints the frames it would send.
// With --run it sends this fixed sequence, and always leaves the service mode at the end:
//   enter service mode -> headset connected? -> remote mode? -> battery voltage -> exit service mode
// With --set-mode, if the remote mode is not 1, it is switched to 1 for the read and then restored.
// The audio link of the headset stops while the receiver is in service mode (about a second).
//
// Build: csc /platform:x64 /out:g435probe.exe g435probe.cs
// Usage: g435probe                      dry run
//        g435probe --run                one read
//        g435probe --run --set-mode     also allow the "set remote mode 1" command if the mode is not 1
//        g435probe --run --repeat 10 --interval 20
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class G435Probe
{
    const ushort Vid = 0x046D, Pid = 0x0ACB, VendorPage = 0xFFC0;
    const byte ReportId = 0x06;
    const byte AckOk = 0x5A, AckAlready = 0xFE, NoData = 0xFF;
    const byte CatOta = 0x06, CatWireless = 0x0E;
    const byte GetMode = 0xE0, SetMode = 0xE1, GetConnectStatus = 0xE3;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)] public ushort[] Counts;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);

    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, SHARE_RW = 3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static SafeFileHandle control;      // feature requests
    static InputReader input;           // acknowledgements
    static int featureLength;
    static byte sequence;

    sealed class InputReader : IDisposable
    {
        readonly FileStream stream;
        readonly byte[] buffer;
        IAsyncResult pending;

        public InputReader(SafeFileHandle handle, int reportLength)
        {
            stream = new FileStream(handle, FileAccess.Read, reportLength, true);
            buffer = new byte[reportLength];
        }

        public byte[] Read(int timeoutMs)
        {
            if (pending == null) pending = stream.BeginRead(buffer, 0, buffer.Length, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(timeoutMs)) return null;
            int n = stream.EndRead(pending);
            pending = null;
            var copy = new byte[n];
            Array.Copy(buffer, copy, n);
            return copy;
        }

        public void Dispose() { stream.Dispose(); }
    }

    static void Log(string format, params object[] args)
    {
        Console.WriteLine("{0,6} ms  {1}", Clock.ElapsedMilliseconds, string.Format(format, args));
    }

    static string Hex(byte[] data, int count)
    {
        return BitConverter.ToString(data, 0, Math.Min(count, data.Length)).Replace('-', ' ');
    }

    static string FindVendorCollection(out int inputLength, out int featureLen)
    {
        inputLength = featureLen = 0;
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
                    if (!HidD_GetAttributes(h, ref a) || a.VendorID != Vid || a.ProductID != Pid) continue;
                    IntPtr pp;
                    if (!HidD_GetPreparsedData(h, out pp)) continue;
                    try
                    {
                        HIDP_CAPS caps;
                        HidP_GetCaps(pp, out caps);
                        if (caps.UsagePage != VendorPage) continue;
                        inputLength = caps.InputReportByteLength;
                        featureLen = caps.FeatureReportByteLength;
                        return path;
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    // ---- frames -------------------------------------------------------------------------

    static byte[] BridgeFrame(byte op0, byte op1, byte[] body, int length)
    {
        var report = new byte[length];
        report[0] = ReportId;
        report[1] = op0;
        report[2] = op1;
        Array.Copy(body, 0, report, 3, body.Length);
        return report;
    }

    static byte[] PaCommand(byte category, byte seq, byte len, byte[] payload)
    {
        var cmd = new List<byte> { 0x50, 0x41, category, seq, len };
        cmd.AddRange(payload);
        var msg = new List<byte> { (byte)cmd.Count };
        msg.AddRange(cmd);
        return msg.ToArray();
    }

    // ---- parsing --------------------------------------------------------------------------

    static int? ParseRemoteValue(byte[] buf, byte cmd)
    {
        for (int i = 0; i + 4 <= buf.Length; i++)
            if (buf[i] == 0x02 && buf[i + 1] == 0x00 && buf[i + 2] == cmd) return buf[i + 3];
        return null;
    }

    static int? ParseBatteryMillivolts(byte[] buf)
    {
        for (int i = 0; i + 7 <= buf.Length; i++)
        {
            if (buf[i] != 0x07 || buf[i + 1] != 0x00 || buf[i + 2] != CatOta || (buf[i + 3] & 0x80) == 0 || buf[i + 4] != 0x00) continue;
            int mv = buf[i + 5] | (buf[i + 6] << 8);
            if (mv >= 2500 && mv <= 4600) return mv;
        }
        return null;
    }

    static readonly int[,] Curve =
    {
        { 4200, 100 }, { 4150, 95 }, { 4100, 90 }, { 4050, 85 }, { 4000, 80 }, { 3950, 75 }, { 3900, 70 }, { 3850, 62 },
        { 3800, 55 }, { 3750, 45 }, { 3700, 35 }, { 3650, 25 }, { 3600, 15 }, { 3550, 10 }, { 3500, 5 }, { 3400, 0 },
    };

    static int MillivoltsToPercent(int mv)
    {
        if (mv >= Curve[0, 0]) return 100;
        for (int i = 0; i + 1 < Curve.GetLength(0); i++)
        {
            int hiMv = Curve[i, 0], hiPct = Curve[i, 1], loMv = Curve[i + 1, 0], loPct = Curve[i + 1, 1];
            if (mv >= loMv) return (int)Math.Round(loPct + (double)(mv - loMv) / (hiMv - loMv) * (hiPct - loPct));
        }
        return 0;
    }

    // ---- transport ------------------------------------------------------------------------

    static byte Bridge(string name, byte op0, byte op1, byte[] body)
    {
        var report = BridgeFrame(op0, op1, body, featureLength);
        Log("SET  {0,-22} {1}", name, Hex(report, 3 + body.Length));
        if (!HidD_SetFeature(control, report, report.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetFeature failed");

        long deadline = Clock.ElapsedMilliseconds + 800;
        while (Clock.ElapsedMilliseconds < deadline)
        {
            var ack = input.Read(100);
            if (ack == null) continue;
            Log("ACK  {0}", Hex(ack, ack.Length));
            if (ack.Length >= 2 && ack[0] == ReportId) return ack[1];
        }
        throw new TimeoutException("no acknowledgement for " + name);
    }

    static byte[] GetFeature()
    {
        var buf = new byte[featureLength];
        buf[0] = ReportId;
        if (!HidD_GetFeature(control, buf, buf.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFeature failed");
        return buf;
    }

    static void Drain()
    {
        for (int i = 0; i < 16; i++)
        {
            var buf = GetFeature();
            if (buf[1] == NoData) return;
            Log("DRAIN {0}", Hex(buf, 24));
        }
    }

    static int? Query(string name, byte category, byte len, byte[] payload, Func<byte[], int?> parse)
    {
        var msg = PaCommand(category, sequence, len, payload);
        sequence = (byte)(sequence % 127 + 1);
        Bridge(name, 0x04, 0x03, msg);

        long deadline = Clock.ElapsedMilliseconds + 1500;
        while (Clock.ElapsedMilliseconds < deadline)
        {
            var buf = GetFeature();
            if (buf[1] == NoData) { Thread.Sleep(30); continue; }
            Log("GET  {0}", Hex(buf, 24));
            var value = parse(buf);
            if (value != null) return value;
        }
        Log("     no reply for {0}", name);
        return null;
    }

    // ---- scenario -------------------------------------------------------------------------

    static string ReadOnce(bool allowSetMode)
    {
        sequence = 1;
        long enteredAt = Clock.ElapsedMilliseconds;
        int? originalMode = null;
        bool modeChanged = false;
        try
        {
            byte ack = Bridge("enter service mode", 0x01, 0x03, new byte[0]);
            if (ack != AckOk && ack != AckAlready) Log("     unexpected enter ack 0x{0:X2}", ack);
            Drain();

            int? connected = Query("connect status", CatWireless, 1, new[] { GetConnectStatus }, b => ParseRemoteValue(b, GetConnectStatus));
            if (connected == 0) return "HEADSET OFF";

            originalMode = Query("remote mode", CatWireless, 1, new[] { GetMode }, b => ParseRemoteValue(b, GetMode));
            if (originalMode != 1)
            {
                if (originalMode == null) return "STOPPED: remote mode is unknown";
                if (!allowSetMode) return string.Format("STOPPED: remote mode is {0}, not 1 (rerun with --set-mode)", originalMode);
                modeChanged = true;
                Query("set remote mode 1", CatWireless, 2, new byte[] { SetMode, 0x01 }, b => 0);
            }

            int? mv = Query("battery voltage", CatOta, 1, new byte[] { 0x00, 0x31 }, ParseBatteryMillivolts);
            if (mv == null) return "NO BATTERY REPLY";
            return string.Format("OK {0} mV = {1}%", mv, MillivoltsToPercent(mv.Value));
        }
        finally
        {
            // Leave the receiver as we found it: original remote mode first, then out of service mode.
            if (modeChanged)
            {
                try
                {
                    Query("restore remote mode", CatWireless, 2, new byte[] { SetMode, (byte)originalMode.Value }, b => 0);
                    int? now = Query("remote mode check", CatWireless, 1, new[] { GetMode }, b => ParseRemoteValue(b, GetMode));
                    Log("     remote mode after restore: {0} (was {1})", now == null ? "unknown" : now.ToString(), originalMode);
                }
                catch (Exception ex) { Log("     RESTORE FAILED: {0}", ex.Message); }
            }
            try
            {
                byte ack = Bridge("exit service mode", 0x08, 0x03, new byte[0]);
                Log("     service mode lasted {0} ms, exit ack 0x{1:X2}", Clock.ElapsedMilliseconds - enteredAt, ack);
            }
            catch (Exception ex) { Log("     EXIT FAILED: {0}", ex.Message); }
        }
    }

    static void DryRun()
    {
        Console.WriteLine("Dry run: nothing is sent. Frames that --run would send (feature report 0x06, {0} bytes, zero padded):", featureLength);
        Console.WriteLine("  enter service mode   {0}", Hex(BridgeFrame(0x01, 0x03, new byte[0], 64), 3));
        Console.WriteLine("  connect status       {0}", Hex(BridgeFrame(0x04, 0x03, PaCommand(CatWireless, 1, 1, new[] { GetConnectStatus }), 64), 10));
        Console.WriteLine("  remote mode          {0}", Hex(BridgeFrame(0x04, 0x03, PaCommand(CatWireless, 2, 1, new[] { GetMode }), 64), 10));
        Console.WriteLine("  (only with --set-mode and mode != 1; the original mode is restored afterwards)");
        Console.WriteLine("  set remote mode 1    {0}", Hex(BridgeFrame(0x04, 0x03, PaCommand(CatWireless, 3, 2, new byte[] { SetMode, 0x01 }), 64), 11));
        Console.WriteLine("  battery voltage      {0}", Hex(BridgeFrame(0x04, 0x03, PaCommand(CatOta, 4, 1, new byte[] { 0x00, 0x31 }), 64), 11));
        Console.WriteLine("  restore remote mode  {0}", Hex(BridgeFrame(0x04, 0x03, PaCommand(CatWireless, 5, 2, new byte[] { SetMode, 0x00 }), 64), 11));
        Console.WriteLine("  remote mode check    {0}", Hex(BridgeFrame(0x04, 0x03, PaCommand(CatWireless, 6, 1, new[] { GetMode }), 64), 10));
        Console.WriteLine("  exit service mode    {0}", Hex(BridgeFrame(0x08, 0x03, new byte[0], 64), 3));
        Console.WriteLine("Parser self-test: {0} mV, mode {1}, connected {2}",
            ParseBatteryMillivolts(FromHex("06504901c875480d00070006820032100000ffffff")),
            ParseRemoteValue(FromHex("0650490ec7000000000200e001ffff"), GetMode),
            ParseRemoteValue(FromHex("0650490ec2000000000200e301ffff"), GetConnectStatus));
        Console.WriteLine("Curve self-test: 4300->{0} 4146->{1} 3725->{2} 3300->{3} (expected 100 95 40 0)",
            MillivoltsToPercent(4300), MillivoltsToPercent(4146), MillivoltsToPercent(3725), MillivoltsToPercent(3300));
    }

    static byte[] FromHex(string s)
    {
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }

    static int Main(string[] args)
    {
        bool run = false, setMode = false;
        int repeat = 1, interval = 20;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--run") run = true;
            else if (args[i] == "--set-mode") setMode = true;
            else if (args[i] == "--repeat") repeat = int.Parse(args[++i]);
            else if (args[i] == "--interval") interval = int.Parse(args[++i]);
            else { Console.WriteLine("unknown argument " + args[i]); return 1; }
        }

        int inputLength;
        string path = FindVendorCollection(out inputLength, out featureLength);
        if (path == null) { Console.WriteLine("G435 receiver 046D:0ACB (usage page 0xFFC0) not found"); return 2; }
        Console.WriteLine("Receiver: {0}", path);
        Console.WriteLine("Input report {0} bytes, feature report {1} bytes", inputLength, featureLength);
        if (featureLength != 64) { Console.WriteLine("Unexpected feature report length, stopping"); return 2; }

        if (!run) { DryRun(); return 0; }

        using (control = CreateFile(path, GENERIC_READ | GENERIC_WRITE, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
        using (var readHandle = CreateFile(path, GENERIC_READ, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero))
        {
            if (control.IsInvalid || readHandle.IsInvalid) { Console.WriteLine("Cannot open the receiver: " + new Win32Exception().Message); return 2; }
            using (input = new InputReader(readHandle, inputLength))
            {
                int ok = 0;
                for (int n = 1; n <= repeat; n++)
                {
                    string result;
                    try { result = ReadOnce(setMode); }
                    catch (Exception ex) { result = "ERROR " + ex.Message; }
                    if (result.StartsWith("OK")) ok++;
                    Log("RESULT {0}/{1}: {2}", n, repeat, result);
                    if (n < repeat) Thread.Sleep(interval * 1000);
                }
                Console.WriteLine("Successful reads: {0} of {1}", ok, repeat);
                return ok == repeat ? 0 : 3;
            }
        }
    }
}
