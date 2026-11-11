using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BatteryStrip.App;

namespace BatteryStrip.Devices.G435;

/// <summary>What Windows audio is doing with one physical device right now.</summary>
/// <param name="HasOpenStreams">Some program has a playback or recording stream open on the device.</param>
/// <param name="IsAudible">The playback level is above digital silence.</param>
/// <param name="MicrophoneInUse">Some program is recording from the device.</param>
internal readonly record struct AudioState(bool HasOpenStreams, bool IsAudible, bool MicrophoneInUse);

/// <summary>
/// Watches the speakers and microphone of one physical device through Core Audio. Read-only. The endpoints
/// are looked up once and kept, so a sample costs a few cheap calls; they are looked up again after any
/// error or once a minute. Use from one thread at a time.
/// </summary>
internal sealed class AudioActivity(Guid containerId) : IDisposable
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(1);

    private readonly List<Endpoint> _endpoints = new();
    private DateTime _resolvedAt = DateTime.MinValue;

    private sealed class Endpoint(bool isCapture, IAudioSessionManager2 sessions, IAudioMeterInformation? meter)
    {
        public bool IsCapture { get; } = isCapture;
        public IAudioSessionManager2 Sessions { get; } = sessions;
        public IAudioMeterInformation? Meter { get; } = meter;
    }

    /// <summary>Current state; null when Windows audio cannot be queried, which callers treat as busy.</summary>
    public AudioState? Sample()
    {
        try
        {
            if (DateTime.UtcNow - _resolvedAt > RefreshEvery) Resolve();

            bool open = false, audible = false, microphone = false;
            foreach (Endpoint endpoint in _endpoints)
            {
                bool hasSessions = CountActiveSessions(endpoint.Sessions) > 0;
                open |= hasSessions;
                if (endpoint.IsCapture) microphone |= hasSessions;
                else if (endpoint.Meter != null)
                {
                    Check(endpoint.Meter.GetPeakValue(out float peak));
                    audible |= peak > 0f;
                }
            }
            return new AudioState(open, audible, microphone);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            Log.Warn($"Audio state is unavailable: {exception.Message}");
            Release();
            return null;
        }
    }

    public void Dispose() => Release();

    private void Resolve()
    {
        Release();
        object enumeratorObject = new MMDeviceEnumerator();
        IMMDeviceCollection? collection = null;
        try
        {
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            Check(enumerator.EnumAudioEndpoints(DataFlowAll, DeviceStateActive, out collection));
            Check(collection.GetCount(out int count));
            for (int i = 0; i < count; i++)
            {
                Check(collection.Item(i, out IMMDevice device));
                try
                {
                    if (ReadContainerId(device) != containerId) continue;
                    Check(((IMMEndpoint)device).GetDataFlow(out int flow));
                    bool isCapture = flow == DataFlowCapture;

                    Guid managerId = typeof(IAudioSessionManager2).GUID;
                    Check(device.Activate(ref managerId, ClsCtxAll, IntPtr.Zero, out object manager));
                    IAudioMeterInformation? meter = null;
                    if (!isCapture)
                    {
                        Guid meterId = typeof(IAudioMeterInformation).GUID;
                        Check(device.Activate(ref meterId, ClsCtxAll, IntPtr.Zero, out object meterObject));
                        meter = (IAudioMeterInformation)meterObject;
                    }
                    _endpoints.Add(new Endpoint(isCapture, (IAudioSessionManager2)manager, meter));
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            _resolvedAt = DateTime.UtcNow;
        }
        finally
        {
            if (collection != null) Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(enumeratorObject);
        }
    }

    private void Release()
    {
        foreach (Endpoint endpoint in _endpoints)
        {
            Marshal.ReleaseComObject(endpoint.Sessions);
            if (endpoint.Meter != null) Marshal.ReleaseComObject(endpoint.Meter);
        }
        _endpoints.Clear();
        _resolvedAt = DateTime.MinValue;
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

    private static int CountActiveSessions(IAudioSessionManager2 manager)
    {
        Check(manager.GetSessionEnumerator(out IAudioSessionEnumerator sessions));
        try
        {
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
            Marshal.ReleaseComObject(sessions);
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
