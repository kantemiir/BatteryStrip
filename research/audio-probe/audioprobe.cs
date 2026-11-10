// Read-only Core Audio probe: lists active audio endpoints with their peak level and active sessions.
// Used to check that "is the headset silent and is its microphone free" can be detected reliably.
// Build: csc /platform:x64 /out:audioprobe.exe audioprobe.cs
// Usage: audioprobe [name-filter] [watch-seconds]
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumerator { }

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
}

[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    int GetCount(out int count);
    int Item(int index, out IMMDevice device);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    int OpenPropertyStore(int access, out IPropertyStore store);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out int state);
}

[Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMEndpoint
{
    int GetDataFlow(out int flow);
}

[StructLayout(LayoutKind.Sequential)]
struct PropertyKey { public Guid FormatId; public int PropertyId; }

[StructLayout(LayoutKind.Explicit)]
struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public IntPtr Pointer;
    [FieldOffset(16)] public IntPtr Extra;
}

[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    int GetCount(out int count);
    int GetAt(int index, out PropertyKey key);
    int GetValue(ref PropertyKey key, out PropVariant value);
}

[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation
{
    int GetPeakValue(out float peak);
}

[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2
{
    int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
    int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
    int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
}

[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator
{
    int GetCount(out int count);
    int GetSession(int index, out IAudioSessionControl2 session);
}

[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    int GetState(out int state);
    int GetDisplayName(out IntPtr name);
    int SetDisplayName(IntPtr name, IntPtr context);
    int GetIconPath(out IntPtr path);
    int SetIconPath(IntPtr path, IntPtr context);
    int GetGroupingParam(out Guid group);
    int SetGroupingParam(ref Guid group, IntPtr context);
    int RegisterAudioSessionNotification(IntPtr client);
    int UnregisterAudioSessionNotification(IntPtr client);
    int GetSessionIdentifier(out IntPtr id);
    int GetSessionInstanceIdentifier(out IntPtr id);
    int GetProcessId(out int pid);
}

static class AudioProbe
{
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);

    static PropertyKey FriendlyName = new PropertyKey { FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
    static PropertyKey ContainerId = new PropertyKey { FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), PropertyId = 2 };

    static string ReadString(IPropertyStore store, PropertyKey key)
    {
        PropVariant value;
        if (store.GetValue(ref key, out value) != 0) return "";
        try
        {
            if (value.Type == 31) return Marshal.PtrToStringUni(value.Pointer);                         // VT_LPWSTR
            if (value.Type == 72) return ((Guid)Marshal.PtrToStructure(value.Pointer, typeof(Guid))).ToString(); // VT_CLSID
            return "";
        }
        finally { PropVariantClear(ref value); }
    }

    static string Snapshot(string filter, bool header)
    {
        var lines = new List<string>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection devices;
        enumerator.EnumAudioEndpoints(2 /* eAll */, 1 /* ACTIVE */, out devices);
        int count;
        devices.GetCount(out count);
        for (int i = 0; i < count; i++)
        {
            IMMDevice device;
            devices.Item(i, out device);
            IPropertyStore store;
            device.OpenPropertyStore(0, out store);
            string name = ReadString(store, FriendlyName);
            if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

            int flow;
            ((IMMEndpoint)device).GetDataFlow(out flow);

            object o;
            var meterId = typeof(IAudioMeterInformation).GUID;
            device.Activate(ref meterId, 23, IntPtr.Zero, out o);
            float peak;
            ((IAudioMeterInformation)o).GetPeakValue(out peak);

            var managerId = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref managerId, 23, IntPtr.Zero, out o);
            IAudioSessionEnumerator sessions;
            ((IAudioSessionManager2)o).GetSessionEnumerator(out sessions);
            int sessionCount, active = 0;
            sessions.GetCount(out sessionCount);
            var pids = new List<string>();
            for (int s = 0; s < sessionCount; s++)
            {
                IAudioSessionControl2 session;
                sessions.GetSession(s, out session);
                int state, pid;
                session.GetState(out state);
                session.GetProcessId(out pid);
                if (state == 1) { active++; pids.Add(pid.ToString()); }
            }

            if (header)
                lines.Add(string.Format("{0} \"{1}\" container={2}", flow == 0 ? "render " : "capture", name, ReadString(store, ContainerId)));
            lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  {0} peak={1:0.000} activeSessions={2}{3}", flow == 0 ? "render " : "capture", peak, active,
                pids.Count > 0 ? " pids=" + string.Join(",", pids.ToArray()) : ""));
        }
        if (lines.Count == 0) lines.Add("  (no matching active endpoints)");
        return string.Join("\n", lines.ToArray());
    }

    [MTAThread]
    static int Main(string[] args)
    {
        string filter = args.Length > 0 && args[0] != "*" ? args[0] : null;
        int watch = args.Length > 1 ? int.Parse(args[1]) : 0;
        Console.WriteLine(Snapshot(filter, true));
        string last = null;
        var until = DateTime.UtcNow.AddSeconds(watch);
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(250);
            string now = Snapshot(filter, false);
            // Print only changes in endpoint presence, session counts, or silent/playing state.
            string key = System.Text.RegularExpressions.Regex.Replace(now, @"peak=(0\.000|\d\.\d+)", m => m.Groups[1].Value == "0.000" ? "silent" : "playing");
            if (key != last) { Console.WriteLine("{0:HH:mm:ss.fff}\n{1}", DateTime.Now, now); last = key; }
        }
        return 0;
    }
}
