using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BatteryStrip.Devices.Hid;

/// <summary>Windows HID and device-installation functions used by the HID layer.</summary>
internal static class HidNative
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint ShareReadWrite = 0x00000003;
    public const uint OpenExisting = 3;
    public const uint FileFlagOverlapped = 0x40000000;

    public const int DigcfPresent = 0x02;
    public const int DigcfDeviceInterface = 0x10;

    public const int HidpStatusSuccess = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    /// <summary>HIDP_CAPS: 64 bytes, of which only the first five fields are used here.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)]
        public ushort[] Rest;
    }

    public static readonly DEVPROPKEY ContainerIdKey = new()
    {
        FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        PropertyId = 2,
    };

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS caps);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetProductString(SafeFileHandle device, StringBuilder buffer, int bufferBytes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_SetFeature(SafeFileHandle device, byte[] report, int length);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetFeature(SafeFileHandle device, byte[] report, int length);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo, ref Guid interfaceClass, int index, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize, out int requiredSize, ref SP_DEVINFO_DATA deviceInfo);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA deviceInfo, ref DEVPROPKEY key, out int propertyType, out Guid value, int size, out int requiredSize, int flags);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
