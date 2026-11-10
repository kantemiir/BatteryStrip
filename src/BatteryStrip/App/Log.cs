using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BatteryStrip.App;

/// <summary>
/// Plain-text log in %LOCALAPPDATA%\BatteryStrip\logs. When the file reaches <see cref="MaxBytes"/> it
/// becomes app.old.log, so at most two files exist. Logging never throws.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>When set, every line is also written here (used by --diagnose).</summary>
    public static TextWriter? Mirror { get; set; }

    public static void Start(string directory)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                _path = Path.Combine(directory, "app.log");
            }
            catch (Exception)
            {
                _path = null;
            }
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception exception) => Write("ERROR", $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        string line = string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss.fff} {1} {2}", DateTime.Now, level, message);
        lock (Gate)
        {
            try
            {
                Mirror?.WriteLine(line);
                if (_path == null) return;

                var file = new FileInfo(_path);
                if (file.Exists && file.Length > MaxBytes)
                {
                    string old = Path.Combine(file.DirectoryName!, "app.old.log");
                    File.Delete(old);
                    File.Move(_path, old);
                }
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                // A full disk or a locked file must not take the program down.
            }
        }
    }
}
