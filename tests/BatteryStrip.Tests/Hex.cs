using System;
using System.Linq;

namespace BatteryStrip.Tests;

internal static class Hex
{
    public static byte[] Bytes(string text) =>
        text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(part => Convert.ToByte(part, 16)).ToArray();

    public static string Text(byte[] bytes) => BitConverter.ToString(bytes).Replace('-', ' ');
}
