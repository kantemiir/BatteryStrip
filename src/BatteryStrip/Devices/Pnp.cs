using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryStrip.Devices;

/// <summary>A device-property key, as in devpkey.h.</summary>
internal readonly record struct DevicePropertyKey(Guid FormatId, int PropertyId)
{
    public static readonly DevicePropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    public static readonly DevicePropertyKey ContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    public static readonly DevicePropertyKey ContainerCategory = new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 90);

    /// <summary>Charge in percent that Windows keeps for Bluetooth devices which report it (what Settings shows).</summary>
    public static readonly DevicePropertyKey BluetoothBattery = new(new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2);

    /// <summary>True while a paired Bluetooth device is connected.</summary>
    public static readonly DevicePropertyKey IsConnected = new(new Guid("83da6326-97a6-4088-9453-a1923f573b29"), 15);
}

/// <summary>One device node and the properties read from it.</summary>
internal sealed class PnpDevice
{
    private readonly Dictionary<DevicePropertyKey, (int Type, byte[] Data)> _properties = new();

    public PnpDevice(string instanceId) => InstanceId = instanceId;

    public string InstanceId { get; }

    internal void Set(DevicePropertyKey key, int type, byte[] data) => _properties[key] = (type, data);

    public byte? GetByte(DevicePropertyKey key) =>
        _properties.TryGetValue(key, out var p) && p.Type == TypeByte && p.Data.Length >= 1 ? p.Data[0] : null;

    public bool? GetBool(DevicePropertyKey key) =>
        _properties.TryGetValue(key, out var p) && p.Type == TypeBoolean && p.Data.Length >= 1 ? p.Data[0] != 0 : null;

    public Guid? GetGuid(DevicePropertyKey key) =>
        _properties.TryGetValue(key, out var p) && p.Type == TypeGuid && p.Data.Length >= 16 ? new Guid(p.Data) : null;

    public string? GetString(DevicePropertyKey key) =>
        _properties.TryGetValue(key, out var p) && p.Type == TypeString ? Encoding.Unicode.GetString(p.Data).TrimEnd('\0') : null;

    public IReadOnlyList<string> GetStrings(DevicePropertyKey key)
    {
        if (!_properties.TryGetValue(key, out var p) || p.Type != TypeStringList) return Array.Empty<string>();
        return Encoding.Unicode.GetString(p.Data).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private const int TypeByte = 0x03, TypeBoolean = 0x11, TypeGuid = 0x0D, TypeString = 0x12, TypeStringList = 0x2012;
}

/// <summary>Reads device nodes and their properties through SetupAPI. Read-only; nothing reaches the devices.</summary>
internal static class Pnp
{
    /// <summary>Present device nodes created by one bus enumerator (for example "BTHENUM"), with the requested properties.</summary>
    public static IReadOnlyList<PnpDevice> List(string enumerator, IReadOnlyList<DevicePropertyKey> keys)
    {
        var devices = new List<PnpDevice>();
        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, DigcfAllClasses | DigcfPresent);
        if (set == new IntPtr(-1)) return devices;
        try
        {
            var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
            var id = new StringBuilder(512);
            for (int index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                if (!SetupDiGetDeviceInstanceId(set, ref info, id, id.Capacity, out _)) continue;
                var device = new PnpDevice(id.ToString());
                foreach (DevicePropertyKey key in keys)
                {
                    if (Read(set, ref info, key) is (int type, byte[] data)) device.Set(key, type, data);
                }
                devices.Add(device);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return devices;
    }

    private static (int, byte[])? Read(IntPtr set, ref SP_DEVINFO_DATA info, DevicePropertyKey key)
    {
        var native = new DEVPROPKEY { FormatId = key.FormatId, PropertyId = key.PropertyId };
        SetupDiGetDeviceProperty(set, ref info, ref native, out _, null, 0, out int size, 0);
        if (size <= 0) return null;
        var buffer = new byte[size];
        return SetupDiGetDeviceProperty(set, ref info, ref native, out int type, buffer, size, out _, 0) ? (type, buffer) : null;
    }

    private const int DigcfPresent = 0x02, DigcfAllClasses = 0x04;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA info);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA info, StringBuilder id, int size, out int required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA info, ref DEVPROPKEY key, out int type, byte[]? buffer, int size, out int required, int flags);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
