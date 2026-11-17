using System;
using System.Collections.Generic;
using Microsoft.Win32.SafeHandles;

namespace BatteryStrip.Devices.Hid;

/// <summary>Report ids a collection declares, read from its descriptor without talking to the device.</summary>
internal static class HidReportIds
{
    /// <summary>Ids of the output reports that carry values (vendor data blocks do); empty when Windows does not say.</summary>
    public static IReadOnlyList<byte> Output(HidCollection collection)
    {
        var ids = new List<byte>();
        using SafeFileHandle handle = HidNative.CreateFile(collection.Path, 0, HidNative.ShareReadWrite, IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid || !HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return ids;
        try
        {
            if (HidNative.HidP_GetCaps(preparsed, out HidNative.HIDP_CAPS caps) != HidNative.HidpStatusSuccess) return ids;

            // Rest[] starts after the five fixed fields of HIDP_CAPS: Rest[22] is NumberOutputValueCaps.
            ushort count = caps.Rest[22];
            if (count == 0) return ids;
            byte[] entries = new byte[count * HidNative.ValueCapsSize];
            if (HidNative.HidP_GetValueCaps(HidNative.HidpOutput, entries, ref count, preparsed) != HidNative.HidpStatusSuccess) return ids;
            for (int i = 0; i < count; i++)
            {
                // Byte 2 of a HIDP_VALUE_CAPS entry is its ReportID.
                byte id = entries[i * HidNative.ValueCapsSize + 2];
                if (!ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsed);
        }
    }
}
