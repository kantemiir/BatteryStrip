using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BatteryStrip.Rendering;

/// <summary>
/// Which font families Windows has, asked of DirectWrite (which draws the text anyway). Asking GDI+ instead
/// would load it into the program only for this one question.
/// </summary>
internal static class SystemFonts
{
    /// <summary>The families from <paramref name="families"/> that are installed; empty if DirectWrite cannot be asked.</summary>
    public static HashSet<string> Present(IEnumerable<string> families)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object? factoryObject = null;
        IDWriteFontCollection? collection = null;
        try
        {
            Guid id = typeof(IDWriteFactory).GUID;
            if (DWriteCreateFactory(DWRITE_FACTORY_TYPE_ISOLATED, ref id, out factoryObject) < 0) return present;
            if (((IDWriteFactory)factoryObject).GetSystemFontCollection(out collection, checkForUpdates: false) < 0 || collection == null) return present;
            foreach (string family in families)
            {
                if (collection.FindFamilyName(family, out _, out bool exists) >= 0 && exists) present.Add(family);
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            App.Log.Warn($"Cannot list the installed fonts: {exception.Message}");
        }
        finally
        {
            if (collection != null) Marshal.ReleaseComObject(collection);
            if (factoryObject != null) Marshal.ReleaseComObject(factoryObject);
        }
        return present;
    }

    private const int DWRITE_FACTORY_TYPE_ISOLATED = 1;

    [DllImport("dwrite.dll")]
    private static extern int DWriteCreateFactory(int factoryType, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object factory);

    // Declared in dwrite.h order; methods after the last one used are left out.
    [ComImport, Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFactory
    {
        [PreserveSig]
        int GetSystemFontCollection(out IDWriteFontCollection collection, [MarshalAs(UnmanagedType.Bool)] bool checkForUpdates);
    }

    [ComImport, Guid("a84cee02-3eea-4eee-a827-87c1a02a0fcc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFontCollection
    {
        [PreserveSig] uint GetFontFamilyCount();
        void GetFontFamily();
        [PreserveSig]
        int FindFamilyName([MarshalAs(UnmanagedType.LPWStr)] string familyName, out uint index, [MarshalAs(UnmanagedType.Bool)] out bool exists);
    }
}
