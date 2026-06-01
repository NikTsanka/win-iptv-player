using System;
using System.IO;

namespace IptvPlayer.Services;

/// <summary>
/// Dead-simple synchronous file logger for live diagnostics. Each Write opens, appends
/// and closes the file, so the LAST line is always flushed to disk even if the very next
/// operation hangs the process — which is exactly what we need to locate a freeze.
/// </summary>
public static class Log
{
    // OFF by default: file I/O on every log call caused playback hitches. Flip to true
    // only when diagnosing an issue.
    public static bool Enabled = false;

    private static readonly object Gate = new();
    public static readonly string Path;

    static Log()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IptvPlayer");
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "debug.log");
    }

    public static void Write(string msg)
    {
        if (!Enabled) return;
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [t{Environment.CurrentManagedThreadId}] {msg}";
            lock (Gate) File.AppendAllText(Path, line + Environment.NewLine);
        }
        catch { /* logging must never throw */ }
    }

    public static void Clear()
    {
        try { lock (Gate) File.WriteAllText(Path, ""); } catch { }
    }
}
