using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Detects abnormal viewer terminations that the managed exception
/// handlers cannot catch — native crashes (SkiaSharp / GPU),
/// <see cref="Environment.FailFast(string)"/>, stack-overflow, an
/// out-of-memory kill, or an external <c>kill -9</c> — by leaving a
/// small <em>per-process session marker</em> file on disk for the
/// lifetime of each run.
/// </summary>
/// <remarks>
/// <para>
/// Each instance writes its own marker, named
/// <c>viewer-session-{pid}.lock</c>, in a shared marker directory on
/// startup (<see cref="BeginSession"/>) and deletes <em>only its own</em>
/// marker on a clean shutdown (<see cref="MarkCleanExit"/>). This makes
/// the sentinel correct when several viewers run side by side (e.g.
/// charts compared in two windows): a live instance's marker is left
/// untouched by others, and a clean exit never erases another instance's
/// crash evidence.
/// </para>
/// <para>
/// On startup, any marker whose owning process is no longer alive
/// indicates a previous session that terminated without running its
/// clean-shutdown path — i.e. it crashed. <see cref="BeginSession"/>
/// returns every such session and removes their stale markers.
/// </para>
/// <para>
/// Liveness is decided cross-platform (Windows, macOS, Linux) by process
/// id <em>plus the OS process start time</em>: a recycled PID assigned to
/// a different process has a different start time, so a reused PID is
/// correctly treated as a dead session rather than a live one. The
/// process name is a secondary guard used only when a start time cannot
/// be read.
/// </para>
/// <para>
/// Every operation is best-effort: a failure to read or write a marker
/// (read-only volume, missing directory, …) is swallowed so crash
/// detection can never itself take the process down.
/// </para>
/// </remarks>
internal static class UncleanShutdownSentinel
{
    private static readonly object Gate = new();

    private const string MarkerPrefix = "viewer-session-";
    private const string MarkerExtension = ".lock";

    /// <summary>
    /// Tolerance when comparing a recorded process start time to the live
    /// process's start time. Generous enough to absorb clock-resolution
    /// and serialization rounding, tight enough to distinguish a reused
    /// PID (whose start time differs by seconds or more).
    /// </summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Default marker directory: a <c>crash-markers</c> folder under the
    /// viewer's per-user application-data directory (alongside the
    /// persisted settings), so markers survive temp-directory cleanup
    /// between runs.
    /// </summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EncDotNet.S100.Viewer",
        "crash-markers");

    private static string s_directory = DefaultDirectory;
    private static bool s_enabled = true;

    /// <summary>The active marker directory.</summary>
    public static string Directory
    {
        get { lock (Gate) return s_directory; }
    }

    /// <summary>
    /// Configures the sentinel. When <paramref name="enabled"/> is
    /// <see langword="false"/> (e.g. <c>--ephemeral</c> or one-shot
    /// screenshot runs) <see cref="BeginSession"/> and
    /// <see cref="MarkCleanExit"/> become no-ops so automation runs never
    /// write a marker or report a stale one.
    /// </summary>
    /// <param name="enabled">Whether crash detection is active this run.</param>
    /// <param name="directory">Marker directory override; <see langword="null"/>
    /// or whitespace resets to <see cref="DefaultDirectory"/>.</param>
    public static void Configure(bool enabled, string? directory = null)
    {
        lock (Gate)
        {
            s_enabled = enabled;
            s_directory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        }
    }

    /// <summary>
    /// Starts a session: scans the marker directory for sessions from
    /// previous runs whose process is no longer alive (crashes), removes
    /// their stale markers, then writes a fresh marker for the current
    /// process.
    /// </summary>
    /// <param name="version">The current application version string,
    /// recorded in the marker for diagnostics.</param>
    /// <returns>
    /// Every previous session that ended without a clean shutdown, ordered
    /// oldest-first. Empty when there was no prior crash, when the only
    /// other markers belong to still-running instances, or when the
    /// sentinel is disabled.
    /// </returns>
    public static IReadOnlyList<PreviousSession> BeginSession(string version)
    {
        bool enabled;
        string directory;
        lock (Gate)
        {
            enabled = s_enabled;
            directory = s_directory;
        }

        if (!enabled)
            return Array.Empty<PreviousSession>();

        var crashed = ScanForCrashedSessions(directory);
        WriteMarker(directory, version);
        return crashed;
    }

    /// <summary>
    /// Records a clean shutdown by deleting <em>this process's</em> marker
    /// only. After this call a subsequent startup will not report this
    /// (graceful) exit as a crash. No-op when disabled.
    /// </summary>
    public static void MarkCleanExit()
    {
        bool enabled;
        string directory;
        lock (Gate)
        {
            enabled = s_enabled;
            directory = s_directory;
        }

        if (!enabled)
            return;

        TryDelete(MarkerPathFor(directory, SafeCurrentPid()));
    }

    private static List<PreviousSession> ScanForCrashedSessions(string directory)
    {
        var results = new List<PreviousSession>();
        try
        {
            if (!System.IO.Directory.Exists(directory))
                return results;

            foreach (var file in System.IO.Directory.EnumerateFiles(
                         directory, MarkerPrefix + "*" + MarkerExtension))
            {
                try
                {
                    var marker = JsonSerializer.Deserialize<SessionMarker>(
                        File.ReadAllText(file), JsonOptions);
                    if (marker is null)
                    {
                        // Malformed marker — clean it up so it never lingers.
                        TryDelete(file);
                        continue;
                    }

                    // A marker whose writer is still alive belongs to a
                    // concurrent side-by-side instance (or to this very
                    // process on a re-scan) — leave it be.
                    if (IsProcessAlive(marker.Pid, marker.ProcessStartUtc, marker.ProcessName))
                        continue;

                    results.Add(new PreviousSession(
                        StartedUtc: marker.StartedUtc,
                        Pid: marker.Pid,
                        Version: marker.Version ?? "(unknown)"));

                    // The owning process is gone — remove the stale marker so
                    // the crash is reported exactly once.
                    TryDelete(file);
                }
                catch
                {
                    // Skip an unreadable individual marker; keep scanning.
                }
            }
        }
        catch
        {
            // Directory enumeration failed — report nothing.
        }

        results.Sort(static (a, b) => a.StartedUtc.CompareTo(b.StartedUtc));
        return results;
    }

    private static void WriteMarker(string directory, string version)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);

            var pid = SafeCurrentPid();
            var marker = new SessionMarker
            {
                Pid = pid,
                ProcessName = SafeCurrentProcessName(),
                ProcessStartUtc = SafeCurrentProcessStartUtc(),
                StartedUtc = DateTime.UtcNow,
                Version = string.IsNullOrWhiteSpace(version) ? null : version,
            };

            File.WriteAllText(
                MarkerPathFor(directory, pid),
                JsonSerializer.Serialize(marker, JsonOptions));
        }
        catch
        {
            // Best-effort — a missing marker just means no crash detection
            // for the next run, never a fault for this one.
        }
    }

    private static string MarkerPathFor(string directory, int pid) =>
        Path.Combine(directory, $"{MarkerPrefix}{pid}{MarkerExtension}");

    /// <summary>
    /// Determines whether the process that wrote a marker is still the
    /// live process it claims to be — cross-platform. Returns
    /// <see langword="false"/> when no process with that id exists, when
    /// it has exited, or when its start time no longer matches (the PID
    /// was reused by a different process).
    /// </summary>
    private static bool IsProcessAlive(int pid, DateTime? processStartUtc, string? processName)
    {
        if (pid <= 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
                return false;

            // Primary, OS-agnostic disambiguation: the OS process start
            // time. A recycled PID belongs to a process that started at a
            // different time, so a mismatch means the original session is
            // dead. Defeats PID reuse identically on Windows/macOS/Linux.
            if (processStartUtc is { } recorded)
            {
                try
                {
                    var actual = process.StartTime.ToUniversalTime();
                    return Math.Abs((actual - recorded).TotalSeconds) <= StartTimeTolerance.TotalSeconds;
                }
                catch
                {
                    // StartTime unreadable (access denied / exited mid-check)
                    // — fall through to the process-name guard.
                }
            }

            // Secondary guard when no start time is available: only treat
            // the PID as a live viewer when the process name also matches.
            return string.IsNullOrEmpty(processName)
                || string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // No process with that id is running.
            return false;
        }
        catch
        {
            // Access denied or platform quirk — assume not our process so
            // we err towards reporting rather than silently swallowing.
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort — never crash on cleanup.
        }
    }

    private static int SafeCurrentPid()
    {
        try { return Environment.ProcessId; }
        catch { return -1; }
    }

    private static string? SafeCurrentProcessName()
    {
        try { return Process.GetCurrentProcess().ProcessName; }
        catch { return null; }
    }

    private static DateTime? SafeCurrentProcessStartUtc()
    {
        try { return Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return null; }
    }

    private sealed class SessionMarker
    {
        public int Pid { get; set; }
        public string? ProcessName { get; set; }
        public DateTime? ProcessStartUtc { get; set; }
        public DateTime StartedUtc { get; set; }
        public string? Version { get; set; }
    }
}

/// <summary>
/// Read-only details of a previous viewer session that terminated without
/// a clean shutdown.
/// </summary>
/// <param name="StartedUtc">When the previous session started (UTC).</param>
/// <param name="Pid">Process id of the previous session.</param>
/// <param name="Version">Application version of the previous session.</param>
internal sealed record PreviousSession(
    DateTime StartedUtc,
    int Pid,
    string Version);
