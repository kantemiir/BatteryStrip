using System;
using System.Runtime.InteropServices;
using BatteryStrip.App;

namespace BatteryStrip.Devices.G435;

/// <summary>What Windows audio is doing with one physical device right now.</summary>
/// <param name="HasOpenStreams">Some program is playing to the device or recording from it.</param>
/// <param name="IsAudible">The playback level is above digital silence.</param>
/// <param name="MicrophoneInUse">Some program is recording from the device.</param>
internal readonly record struct AudioState(bool HasOpenStreams, bool IsAudible, bool MicrophoneInUse);

/// <summary>Asks Core Audio whether the headset's speakers or microphone are in use. Read-only.</summary>
internal static class AudioActivity
{
    /// <summary>
    /// State of all audio endpoints that belong to the physical device with this container id.
    /// Returns null when Windows audio cannot be queried; callers must then assume the device is busy.
    /// </summary>
    public static AudioState? Query(Guid containerId)
    {
        object? enumeratorObject = null;
        IMMDeviceCollection? endpoints = null;
        try
        {
            enumeratorObject = new MMDeviceEnumerator();
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            Check(enumerator.EnumAudioEndpoints(DataFlowAll, DeviceStateActive, out endpoints));
            Check(endpoints.GetCount(out int count));

            bool open = false, audible = false, microphone = false;
            for (int i = 0; i < count; i++)
            {
                Check(endpoints.Item(i, out IMMDevice endpoint));
                try
                {
                    if (ReadContainerId(endpoint) != containerId) continue;

                    Check(((IMMEndpoint)endpoint).GetDataFlow(out int flow));
                    bool hasSessions = CountActiveSessions(endpoint) > 0;
                    open |= hasSessions;
                    if (flow == DataFlowCapture) microphone |= hasSessions;
                    else audible |= ReadPeak(endpoint) > 0f;
                }
                finally
                {
                    Marshal.ReleaseComObject(endpoint);
                }
            }
            return new AudioState(open, audible, microphone);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            Log.Warn($"Audio state is unavailable: {exception.Message}");
            return null;
        }
        finally
        {
            if (endpoints != null) Marshal.ReleaseComObject(endpoints);
            if (enumeratorObject != null) Marshal.ReleaseComObject(enumeratorObject);
        }
    }

    private static Guid ReadContainerId(IMMDevice endpoint)
    {
        Check(endpoint.OpenPropertyStore(StorageModeRead, out IPropertyStore store));
        try
        {
            PropertyKey key = ContainerIdKey;
            Check(store.GetValue(ref key, out PropVariant value));
            try
            {
                return value.Type == VariantTypeClsid && value.Pointer != IntPtr.Zero
                    ? (Guid)Marshal.PtrToStructure(value.Pointer, typeof(Guid))
                    : Guid.Empty;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static int CountActiveSessions(IMMDevice endpoint)
    {
        Guid managerId = typeof(IAudioSessionManager2).GUID;
        Check(endpoint.Activate(ref managerId, ClsCtxAll, IntPtr.Zero, out object managerObject));
        IAudioSessionEnumerator? sessions = null;
        try
        {
            Check(((IAudioSessionManager2)managerObject).GetSessionEnumerator(out sessions));
            Check(sessions.GetCount(out int count));
            int active = 0;
            for (int i = 0; i < count; i++)
            {
                Check(sessions.GetSession(i, out IAudioSessionControl session));
                try
                {
                    Check(session.GetState(out int state));
                    if (state == SessionStateActive) active++;
                }
                finally
                {
                    Marshal.ReleaseComObject(session);
                }
            }
            return active;
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(managerObject);
        }
    }

    private static float ReadPeak(IMMDevice endpoint)
    {
        Guid meterId = typeof(IAudioMeterInformation).GUID;
        Check(endpoint.Activate(ref meterId, ClsCtxAll, IntPtr.Zero, out object meterObject));
        try
        {
            Check(((IAudioMeterInformation)meterObject).GetPeakValue(out float peak));
            return peak;
        }
        finally
        {
            Marshal.ReleaseComObject(meterObject);
        }
    }

    private static void Check(int hresult)
    {
        if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
    }

    private const int DataFlowCapture = 1;
    private const int DataFlowAll = 2;
    private const int DeviceStateActive = 1;
    private const int StorageModeRead = 0;
    private const int ClsCtxAll = 23;
    private const int SessionStateActive = 1;
    private const ushort VariantTypeClsid = 72;

    private static readonly PropertyKey ContainerIdKey = new()
    {
        FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        PropertyId = 2,
    };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    // The interfaces below declare only the leading methods that are called; the order must match the Windows headers.

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection endpoints);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid interfaceId, int classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMEndpoint
    {
        [PreserveSig] int GetDataFlow(out int flow);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionId, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionId, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
    }
}
