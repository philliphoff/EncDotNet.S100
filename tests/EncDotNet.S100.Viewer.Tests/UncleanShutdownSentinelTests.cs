using System.Diagnostics;
using System.Runtime.InteropServices;
using EncDotNet.S100.Viewer.Diagnostics;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers <see cref="UncleanShutdownSentinel"/>: per-process marker
/// write/clear lifecycle, disabled no-ops, multi-instance behaviour
/// (a live side-by-side instance is not reported and its marker is
/// retained), and cross-platform PID-reuse robustness (a recycled PID
/// with a different start time is treated as a crashed session).
/// </summary>
/// <remarks>
/// The sentinel keeps process-wide static state, so every test configures
/// its own temp marker directory and the class runs its methods
/// sequentially.
/// </remarks>
public sealed class UncleanShutdownSentinelTests : IDisposable
{
    private readonly string _dir;

    public UncleanShutdownSentinelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"viewer-sentinel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        UncleanShutdownSentinel.Configure(enabled: false);
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private int MarkerCount() =>
        Directory.EnumerateFiles(_dir, "viewer-session-*.lock").Count();

    private void WriteMarker(
        int pid,
        string processName,
        DateTime? processStartUtc,
        DateTime startedUtc,
        string version)
    {
        var startUtcJson = processStartUtc is { } s ? $"\"{s:O}\"" : "null";
        var json =
            $$"""
            {"Pid":{{pid}},"ProcessName":"{{processName}}","ProcessStartUtc":{{startUtcJson}},"StartedUtc":"{{startedUtc:O}}","Version":"{{version}}"}
            """;
        File.WriteAllText(Path.Combine(_dir, $"viewer-session-{pid}.lock"), json);
    }

    [Fact]
    public void BeginSession_NoMarkers_ReturnsEmptyAndWritesOwnMarker()
    {
        UncleanShutdownSentinel.Configure(enabled: true, _dir);

        var crashed = UncleanShutdownSentinel.BeginSession("1.2.3");

        Assert.Empty(crashed);
        // This process's own marker now exists.
        Assert.True(File.Exists(Path.Combine(_dir, $"viewer-session-{Environment.ProcessId}.lock")));
    }

    [Fact]
    public void MarkCleanExit_RemovesOnlyOwnMarker()
    {
        UncleanShutdownSentinel.Configure(enabled: true, _dir);
        UncleanShutdownSentinel.BeginSession("1.0.0");

        // A foreign live-looking marker should be left untouched.
        var foreignPath = Path.Combine(_dir, "viewer-session-424242.lock");
        File.WriteAllText(foreignPath, "{}");

        UncleanShutdownSentinel.MarkCleanExit();

        Assert.False(File.Exists(Path.Combine(_dir, $"viewer-session-{Environment.ProcessId}.lock")));
        Assert.True(File.Exists(foreignPath));
    }

    [Fact]
    public void BeginSession_DeadMarkers_ReportedAndRemoved()
    {
        var t1 = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 3, 6, 7, 8, DateTimeKind.Utc);
        // PIDs that are overwhelmingly unlikely to map to a live process.
        WriteMarker(999_999_991, "ghost", processStartUtc: null, t1, "9.0.0");
        WriteMarker(999_999_992, "ghost", processStartUtc: null, t2, "9.1.0");

        UncleanShutdownSentinel.Configure(enabled: true, _dir);
        var crashed = UncleanShutdownSentinel.BeginSession("10.0.0");

        Assert.Equal(2, crashed.Count);
        // Ordered oldest-first.
        Assert.Equal(t1, crashed[0].StartedUtc);
        Assert.Equal(t2, crashed[1].StartedUtc);
        Assert.Equal("9.1.0", crashed[^1].Version);

        // Stale markers removed; only this process's marker remains.
        Assert.False(File.Exists(Path.Combine(_dir, "viewer-session-999999991.lock")));
        Assert.False(File.Exists(Path.Combine(_dir, "viewer-session-999999992.lock")));
        Assert.Equal(1, MarkerCount());
    }

    [Fact]
    public void BeginSession_CalledTwice_DoesNotReportOwnLiveMarker()
    {
        UncleanShutdownSentinel.Configure(enabled: true, _dir);
        UncleanShutdownSentinel.BeginSession("1.0.0");

        // Re-scan: our own marker is alive (matching pid + start time), so
        // it must not be reported as a crash.
        var crashed = UncleanShutdownSentinel.BeginSession("1.0.0");

        Assert.Empty(crashed);
    }

    [Fact]
    public void Disabled_BeginSessionAndMarkCleanExit_AreNoOps()
    {
        UncleanShutdownSentinel.Configure(enabled: false, _dir);

        var crashed = UncleanShutdownSentinel.BeginSession("1.0.0");

        Assert.Empty(crashed);
        Assert.Equal(0, MarkerCount());

        // Must not throw even with nothing to clean up.
        UncleanShutdownSentinel.MarkCleanExit();
    }

    [SkippableFact]
    public void BeginSession_MarkerFromLiveOtherInstance_NotReportedAndRetained()
    {
        using var child = StartSleeper();
        Skip.If(child is null, "Could not spawn a helper process on this platform.");

        child!.Refresh();
        WriteMarker(
            child.Id,
            child.ProcessName,
            child.StartTime.ToUniversalTime(),
            startedUtc: DateTime.UtcNow,
            version: "5.0.0");

        UncleanShutdownSentinel.Configure(enabled: true, _dir);
        var crashed = UncleanShutdownSentinel.BeginSession("6.0.0");

        // The marker's writer (the child) is still alive — a concurrent
        // side-by-side instance, not a crash.
        Assert.DoesNotContain(crashed, c => c.Pid == child.Id);
        Assert.True(File.Exists(Path.Combine(_dir, $"viewer-session-{child.Id}.lock")));

        TryKill(child);
    }

    [SkippableFact]
    public void BeginSession_PidReusedWithDifferentStartTime_ReportedAsCrash()
    {
        using var child = StartSleeper();
        Skip.If(child is null, "Could not spawn a helper process on this platform.");

        // Same (live) PID, but a start time from long ago: this simulates a
        // recycled PID whose original viewer session is actually dead.
        var staleStart = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteMarker(
            child!.Id,
            child.ProcessName,
            processStartUtc: staleStart,
            startedUtc: staleStart,
            version: "1.0.0");

        UncleanShutdownSentinel.Configure(enabled: true, _dir);
        var crashed = UncleanShutdownSentinel.BeginSession("2.0.0");

        Assert.Contains(crashed, c => c.Pid == child.Id);
        Assert.False(File.Exists(Path.Combine(_dir, $"viewer-session-{child.Id}.lock")));

        TryKill(child);
    }

    private static Process? StartSleeper()
    {
        try
        {
            var psi = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new ProcessStartInfo("cmd.exe", "/c timeout /t 30 /nobreak")
                : new ProcessStartInfo("sleep", "30");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* best-effort */ }
    }
}
