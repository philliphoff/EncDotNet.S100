using System;
using System.IO;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Process-wide crash-log sink. The destination defaults to a
/// <c>viewer-crash.log</c> file in the system temp directory and can be
/// overridden by the <c>--crash-log</c> command-line option so agent
/// runs collect their own diagnostics in a known location instead of a
/// hard-coded path.
/// </summary>
/// <remarks>
/// Every write is best-effort: a failure to append (read-only volume,
/// missing directory, …) is swallowed so logging can never take the
/// process down. The path is set once during command execution, before
/// any startup code can fault, via <see cref="ConfigurePath"/>.
/// </remarks>
internal static class CrashLog
{
    private static readonly object Gate = new();

    /// <summary>Default crash-log path: <c>{TEMP}/viewer-crash.log</c>.</summary>
    public static string DefaultPath { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "viewer-crash.log");

    private static string s_path = DefaultPath;

    /// <summary>The active crash-log file path.</summary>
    public static string Path
    {
        get { lock (Gate) return s_path; }
    }

    /// <summary>
    /// Sets the crash-log destination. A <c>null</c> or whitespace
    /// value resets to <see cref="DefaultPath"/>.
    /// </summary>
    public static void ConfigurePath(string? path)
    {
        lock (Gate)
        {
            s_path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        }
    }

    /// <summary>
    /// Appends a labelled crash entry to the configured file. Never
    /// throws.
    /// </summary>
    public static void Append(string label, string message)
    {
        string path;
        lock (Gate) path = s_path;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(path, $"{DateTime.Now:O} [{label}] {message}\n\n");
        }
        catch
        {
            // Best-effort — diagnostics must never crash the process.
        }
    }
}
