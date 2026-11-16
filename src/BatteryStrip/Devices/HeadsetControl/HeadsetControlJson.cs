using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BatteryStrip.Devices.HeadsetControl;

/// <summary>One headset as the HeadsetControl command-line tool reports it.</summary>
/// <param name="VendorId">USB vendor id, read from the tool's text such as "0x1038".</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="Name">Name the tool gives the headset; empty when it gives none.</param>
/// <param name="Status">Battery status as the tool names it, such as "BATTERY_AVAILABLE"; empty when there is no battery object.</param>
/// <param name="Level">Charge as reported, from 0 to 100; -1 when there is none.</param>
internal sealed record HeadsetControlDevice(ushort VendorId, ushort ProductId, string Name, string Status, int Level);

/// <summary>
/// Reads the JSON that "headsetcontrol -o json" prints. Deliberately small: objects, arrays, strings with escapes,
/// numbers and the literals true, false and null, nested at most <see cref="MaxDepth"/> deep. The program references
/// no JSON library, so this is all the reading there is.
/// </summary>
internal static class HeadsetControlJson
{
    /// <summary>Deeper nesting than this is not what the tool prints; reading stops there.</summary>
    private const int MaxDepth = 32;

    /// <summary>The headsets in the output, or none when the output cannot be read. <see cref="TryParse"/> tells the two apart.</summary>
    public static IReadOnlyList<HeadsetControlDevice> Parse(string output) =>
        TryParse(output, out IReadOnlyList<HeadsetControlDevice> devices) ? devices : Array.Empty<HeadsetControlDevice>();

    /// <summary>
    /// Reads the headsets from the tool's output. Text before the first '{' is skipped, and anything after the object
    /// is ignored. Returns false when there is no JSON object with a "devices" array. A device entry without a readable
    /// vendor or product id is left out; any other field may be missing.
    /// </summary>
    public static bool TryParse(string output, out IReadOnlyList<HeadsetControlDevice> devices)
    {
        devices = Array.Empty<HeadsetControlDevice>();
        int start = output.IndexOf('{');
        if (start < 0) return false;

        try
        {
            var reader = new Reader(output, start);
            if (reader.ReadValue(0) is not Dictionary<string, object?> root) return false;
            if (!root.TryGetValue("devices", out object? list) || list is not List<object?> entries) return false;

            var found = new List<HeadsetControlDevice>();
            foreach (object? entry in entries)
            {
                if (entry is Dictionary<string, object?> fields && ToDevice(fields) is HeadsetControlDevice device) found.Add(device);
            }
            devices = found;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static HeadsetControlDevice? ToDevice(Dictionary<string, object?> fields)
    {
        if (!TryId(fields, "id_vendor", out ushort vendor) || !TryId(fields, "id_product", out ushort product)) return null;

        string name = fields.TryGetValue("device", out object? device) && device is string text ? text : "";
        string status = "";
        int level = -1;
        if (fields.TryGetValue("battery", out object? battery) && battery is Dictionary<string, object?> charge)
        {
            if (charge.TryGetValue("status", out object? state) && state is string word) status = word;
            if (charge.TryGetValue("level", out object? value) && value is double number && number >= int.MinValue && number <= int.MaxValue)
                level = (int)Math.Round(number);
        }
        return new HeadsetControlDevice(vendor, product, name, status, level);
    }

    /// <summary>Reads a vendor or product id: hex text such as "0x1038" (the prefix is optional), or a plain number.</summary>
    private static bool TryId(Dictionary<string, object?> fields, string key, out ushort id)
    {
        id = 0;
        if (!fields.TryGetValue(key, out object? value)) return false;

        if (value is string text)
        {
            string digits = text.Trim();
            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) digits = digits.Substring(2);
            if (!int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int parsed)) return false;
            if (parsed < 0 || parsed > ushort.MaxValue) return false;
            id = (ushort)parsed;
            return true;
        }

        if (value is double number && number >= 0 && number <= ushort.MaxValue && Math.Floor(number) == number)
        {
            id = (ushort)number;
            return true;
        }
        return false;
    }

    /// <summary>Recursive-descent reader. Starts at one position of the text and reads one value; what follows it is not read.</summary>
    private sealed class Reader
    {
        private readonly string _text;
        private int _at;

        public Reader(string text, int start)
        {
            _text = text;
            _at = start;
        }

        /// <summary>Reads the value at the current position: a dictionary for an object, a list for an array, a string, a double, a bool or null.</summary>
        /// <param name="depth">How many objects or arrays enclose this value.</param>
        public object? ReadValue(int depth)
        {
            SkipSpace();
            char next = Peek();
            switch (next)
            {
                case '{':
                    return ReadObject(depth + 1);
                case '[':
                    return ReadArray(depth + 1);
                case '"':
                    return ReadString();
                case 't':
                    ExpectWord("true");
                    return true;
                case 'f':
                    ExpectWord("false");
                    return false;
                case 'n':
                    ExpectWord("null");
                    return null;
                default:
                    if (next == '-' || (next >= '0' && next <= '9')) return ReadNumber();
                    throw Malformed();
            }
        }

        private Dictionary<string, object?> ReadObject(int depth)
        {
            if (depth > MaxDepth) throw Malformed();
            _at++; // the '{'
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);

            SkipSpace();
            if (Peek() == '}')
            {
                _at++;
                return fields;
            }
            while (true)
            {
                SkipSpace();
                if (Peek() != '"') throw Malformed();
                string key = ReadString();
                SkipSpace();
                if (Next() != ':') throw Malformed();
                fields[key] = ReadValue(depth);
                SkipSpace();
                char after = Next();
                if (after == '}') return fields;
                if (after != ',') throw Malformed();
            }
        }

        private List<object?> ReadArray(int depth)
        {
            if (depth > MaxDepth) throw Malformed();
            _at++; // the '['
            var items = new List<object?>();

            SkipSpace();
            if (Peek() == ']')
            {
                _at++;
                return items;
            }
            while (true)
            {
                items.Add(ReadValue(depth));
                SkipSpace();
                char after = Next();
                if (after == ']') return items;
                if (after != ',') throw Malformed();
            }
        }

        private string ReadString()
        {
            _at++; // the opening quote
            var text = new StringBuilder();
            while (true)
            {
                char c = Next();
                if (c == '"') return text.ToString();
                if (c < ' ') throw Malformed();
                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }

                switch (Next())
                {
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '/': text.Append('/'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'u': text.Append(ReadHex4()); break;
                    default: throw Malformed();
                }
            }
        }

        /// <summary>The four hex digits of a \uXXXX escape. A surrogate pair is two escapes and comes out right as two chars.</summary>
        private char ReadHex4()
        {
            int code = 0;
            for (int i = 0; i < 4; i++)
            {
                char digit = Next();
                int value = digit switch
                {
                    >= '0' and <= '9' => digit - '0',
                    >= 'a' and <= 'f' => digit - 'a' + 10,
                    >= 'A' and <= 'F' => digit - 'A' + 10,
                    _ => throw Malformed(),
                };
                code = code * 16 + value;
            }
            return (char)code;
        }

        private double ReadNumber()
        {
            int start = _at;
            while (_at < _text.Length && IsNumberPart(_text[_at])) _at++;
            if (!double.TryParse(_text.Substring(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                throw Malformed();
            return number;
        }

        private static bool IsNumberPart(char c) =>
            (c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E';

        private void ExpectWord(string word)
        {
            if (_at + word.Length > _text.Length || string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0) throw Malformed();
            _at += word.Length;
        }

        private void SkipSpace()
        {
            while (_at < _text.Length && (_text[_at] == ' ' || _text[_at] == '\t' || _text[_at] == '\r' || _text[_at] == '\n')) _at++;
        }

        private char Peek() => _at < _text.Length ? _text[_at] : throw Malformed();

        private char Next()
        {
            char c = Peek();
            _at++;
            return c;
        }

        private FormatException Malformed() => new FormatException("Not valid JSON at character " + _at + ".");
    }
}
