using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BatteryStrip.Devices.Hid;

/// <summary>
/// One HID top-level collection as Windows exposes it: a separate openable path with its own report sizes.
/// A USB receiver usually has several.
/// </summary>
/// <param name="ContainerId">Identifies the physical device; the same for all collections and for its audio endpoints.</param>
internal sealed record HidCollection(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputLength,
    int OutputLength,
    int FeatureLength,
    string Product,
    Guid ContainerId)
{
    public bool IsVendorDefined => UsagePage >= 0xFF00;

    /// <summary>Lists the HID collections present right now. Opens each one without access rights, so nothing is sent to a device.</summary>
    public static IReadOnlyList<HidCollection> EnumeratePresent()
    {
        var found = new List<HidCollection>();
        HidNative.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = HidNative.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, HidNative.DigcfPresent | HidNative.DigcfDeviceInterface);
        if (set == new IntPtr(-1)) return found;

        try
        {
            var interfaceData = new HidNative.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(HidNative.SP_DEVICE_INTERFACE_DATA)) };
            for (int index = 0; HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref interfaceData); index++)
            {
                if (ReadPath(set, ref interfaceData, out Guid containerId) is string path && Describe(path, containerId) is HidCollection collection)
                    found.Add(collection);
            }
        }
        finally
        {
            HidNative.SetupDiDestroyDeviceInfoList(set);
        }
        return found;
    }

    private static string? ReadPath(IntPtr set, ref HidNative.SP_DEVICE_INTERFACE_DATA interfaceData, out Guid containerId)
    {
        containerId = Guid.Empty;
        var deviceInfo = new HidNative.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(HidNative.SP_DEVINFO_DATA)) };
        HidNative.SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, IntPtr.Zero, 0, out int size, ref deviceInfo);
        if (size <= 0) return null;

        IntPtr detail = Marshal.AllocHGlobal(size);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W: a DWORD size (8 with 64-bit packing, 6 with 32-bit) followed by the path.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, detail, size, out _, ref deviceInfo)) return null;

            var key = HidNative.ContainerIdKey;
            HidNative.SetupDiGetDeviceProperty(set, ref deviceInfo, ref key, out _, out containerId, Marshal.SizeOf(typeof(Guid)), out _, 0);
            return Marshal.PtrToStringUni(detail + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    private static HidCollection? Describe(string path, Guid containerId)
    {
        using SafeFileHandle handle = HidNative.CreateFile(path, 0, HidNative.ShareReadWrite, IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var attributes = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HidNative.HIDD_ATTRIBUTES)) };
        if (!HidNative.HidD_GetAttributes(handle, ref attributes)) return null;
        if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;

        try
        {
            if (HidNative.HidP_GetCaps(preparsed, out HidNative.HIDP_CAPS caps) != HidNative.HidpStatusSuccess) return null;

            var product = new StringBuilder(128);
            string name = HidNative.HidD_GetProductString(handle, product, product.Capacity * 2) ? product.ToString() : "";

            return new HidCollection(path, attributes.VendorID, attributes.ProductID, caps.UsagePage, caps.Usage,
                caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength, name, containerId);
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsed);
        }
    }
}
