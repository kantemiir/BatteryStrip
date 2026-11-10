using System;
using System.Runtime.InteropServices;
using System.Threading;
using BatteryStrip.App;

namespace BatteryStrip.Devices;

/// <summary>
/// Tells when a device was plugged in, unplugged, connected or disconnected, using Windows device
/// notifications instead of polling. A burst of events (one receiver brings a dozen interfaces)
/// is folded into a single <see cref="Changed"/> call.
/// </summary>
internal sealed class DeviceWatcher : IDisposable
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(800);

    private readonly Timer _settle;
    private readonly NotifyCallback _callback; // kept in a field so the delegate outlives the native registration
    private IntPtr _registration;

    /// <summary>Raised on a thread-pool thread shortly after the set of devices changed.</summary>
    public event Action? Changed;

    public DeviceWatcher()
    {
        _settle = new Timer(_ => Changed?.Invoke());
        _callback = OnNotification;

        var filter = new CM_NOTIFY_FILTER
        {
            cbSize = Marshal.SizeOf(typeof(CM_NOTIFY_FILTER)),
            Flags = FilterAllInterfaceClasses,
            FilterType = FilterTypeDeviceInterface,
        };
        int result = CM_Register_Notification(ref filter, IntPtr.Zero, _callback, out _registration);
        if (result != 0) Log.Warn($"Device notifications are unavailable (CM_Register_Notification returned {result}).");
    }

    public void Dispose()
    {
        if (_registration != IntPtr.Zero)
        {
            CM_Unregister_Notification(_registration);
            _registration = IntPtr.Zero;
        }
        _settle.Dispose();
    }

    private int OnNotification(IntPtr registration, IntPtr context, int action, IntPtr eventData, int eventDataSize)
    {
        if (action is ActionInterfaceArrival or ActionInterfaceRemoval)
        {
            try { _settle.Change(SettleTime, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
        return 0;
    }

    private const int FilterAllInterfaceClasses = 0x1;
    private const int FilterTypeDeviceInterface = 0;
    private const int ActionInterfaceArrival = 0;
    private const int ActionInterfaceRemoval = 1;

    /// <summary>CM_NOTIFY_FILTER: a 16-byte header and a 400-byte union that stays empty when all classes are requested.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 416)]
    private struct CM_NOTIFY_FILTER
    {
        public int cbSize;
        public int Flags;
        public int FilterType;
        public int Reserved;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NotifyCallback(IntPtr registration, IntPtr context, int action, IntPtr eventData, int eventDataSize);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Register_Notification(ref CM_NOTIFY_FILTER filter, IntPtr context, NotifyCallback callback, out IntPtr registration);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Unregister_Notification(IntPtr registration);
}
