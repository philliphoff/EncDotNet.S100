using EncDotNet.S100.Viewer.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the always-on rolling file logger: per-session file naming, size
/// rollover, the file-count cap, concurrent writers, keeping the files of
/// other running instances, keeping an unreported crashed session's newest
/// file, and reading the tail of a previous session's log for the crash
/// prompt.
/// </summary>
public sealed class RollingFileLoggerProviderTests : IDisposable
{
    private static readonly DateTime SessionStart = new(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "s100-rolling-log-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private RollingFileLoggerProvider Create(
        long maxFileBytes = RollingFileLoggerProvider.DefaultMaxFileBytes,
        int maxFiles = RollingFileLoggerProvider.DefaultMaxFiles,
        DateTime? start = null,
        int pid = 4242,
        Func<int, DateTime, bool>? isSessionAlive = null,
        Func<IEnumerable<PreviousSession>>? unreportedCrashes = null) =>
        new(_directory, LogLevel.Information, maxFileBytes, maxFiles, start ?? SessionStart, pid,
            isSessionAlive ?? ((_, _) => false), unreportedCrashes);

    /// <summary>Writes a crashed session's log file, last written
    /// <paramref name="age"/> ago, and returns its path.</summary>
    private string WriteCrashedFile(int pid, DateTime start, int sequence, string text, TimeSpan age)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory,
            $"viewer-{start:yyyyMMdd'T'HHmmss}Z-{pid}-{sequence:D3}.log");
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    /// <summary>The crash marker records a start slightly after the log's.</summary>
    private static PreviousSession Crash(int pid, DateTime start) => new(start.AddSeconds(1), pid, "1.0.0");

    private static void WriteMany(RollingFileLoggerProvider provider, string prefix, int count)
    {
        var logger = provider.CreateLogger("Test");
        for (var i = 0; i < count; i++)
        {
            logger.LogInformation("{Prefix} {Index} {Padding}", prefix, i, new string('x', 50));
        }
    }

    private string[] LogFiles() => Directory.GetFiles(_directory, "*.log").Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void WritesEntriesToSessionFile()
    {
        using (var provider = Create())
        {
            var logger = provider.CreateLogger("Test.Category");
            logger.LogInformation("Hello {Name}", "world");
            logger.LogDebug("below the floor");
        }

        var file = Assert.Single(LogFiles());
        Assert.Equal("viewer-20261025T093000Z-4242-000.log", Path.GetFileName(file));
        var text = File.ReadAllText(file);
        Assert.Contains("[Information] Test.Category: Hello world", text);
        Assert.DoesNotContain("below the floor", text);
    }

    [Fact]
    public void RollsOverWhenFileReachesSizeCap()
    {
        const long maxBytes = 2_000;
        using (var provider = Create(maxFileBytes: maxBytes, maxFiles: 100))
        {
            var logger = provider.CreateLogger("Test");
            for (var i = 0; i < 100; i++)
            {
                logger.LogInformation("Entry {Index} {Padding}", i, new string('x', 50));
            }
        }

        var files = LogFiles();
        Assert.True(files.Length > 1);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length <= maxBytes));

        // Nothing is lost across the rollover.
        var all = string.Concat(files.Select(File.ReadAllText));
        Assert.Contains("Entry 0 ", all);
        Assert.Contains("Entry 99 ", all);
    }

    [Fact]
    public void KeepsAtMostMaxFiles()
    {
        using (var provider = Create(maxFileBytes: 500, maxFiles: 3))
        {
            var logger = provider.CreateLogger("Test");
            for (var i = 0; i < 200; i++)
            {
                logger.LogInformation("Entry {Index} {Padding}", i, new string('x', 50));
            }
        }

        var files = LogFiles();
        Assert.Equal(3, files.Length);

        // The newest entries survive pruning.
        Assert.Contains("Entry 199 ", string.Concat(files.Select(File.ReadAllText)));
    }

    [Fact]
    public void PruningKeepsPreviousSessionAndRemovesOldest()
    {
        Directory.CreateDirectory(_directory);
        var oldest = Path.Combine(_directory, "viewer-20261020T080000Z-1-000.log");
        var older = Path.Combine(_directory, "viewer-20261021T080000Z-2-000.log");
        var previous = Path.Combine(_directory, "viewer-20261024T080000Z-3-000.log");
        File.WriteAllText(oldest, "a");
        File.WriteAllText(older, "b");
        File.WriteAllText(previous, "c");
        File.SetLastWriteTimeUtc(oldest, SessionStart.AddDays(-5));
        File.SetLastWriteTimeUtc(older, SessionStart.AddDays(-4));
        File.SetLastWriteTimeUtc(previous, SessionStart.AddDays(-1));

        using (var provider = Create(maxFiles: 3))
        {
            provider.CreateLogger("Test").LogInformation("new session");
        }

        var names = LogFiles().Select(Path.GetFileName).ToArray();
        Assert.Equal(3, names.Length);
        Assert.DoesNotContain("viewer-20261020T080000Z-1-000.log", names);
        Assert.Contains("viewer-20261024T080000Z-3-000.log", names);
        Assert.Contains("viewer-20261025T093000Z-4242-000.log", names);
    }

    [Fact]
    public void ConcurrentWritersDoNotLoseEntries()
    {
        const int threads = 8;
        const int perThread = 250;
        using (var provider = Create(maxFiles: 100))
        {
            var logger = provider.CreateLogger("Test");
            Parallel.For(0, threads, t =>
            {
                for (var i = 0; i < perThread; i++)
                {
                    logger.LogInformation("T{Thread}-E{Index}", t, i);
                }
            });
        }

        var lines = LogFiles().SelectMany(File.ReadAllLines).Where(l => l.Length > 0).ToArray();
        Assert.Equal(threads * perThread, lines.Length);
    }

    [Fact]
    public void TwoProcessesWriteSeparateFiles()
    {
        using (var first = Create(pid: 100))
        using (var second = Create(pid: 200))
        {
            first.CreateLogger("A").LogInformation("from first");
            second.CreateLogger("B").LogInformation("from second");
        }

        var files = LogFiles();
        Assert.Equal(2, files.Length);
        Assert.Contains("from first", File.ReadAllText(files.Single(f => f.Contains("-100-", StringComparison.Ordinal))));
        Assert.Contains("from second", File.ReadAllText(files.Single(f => f.Contains("-200-", StringComparison.Ordinal))));
    }

    [Fact]
    public void ReadSessionTail_ReturnsEndOfPreviousSessionAcrossRolledFiles()
    {
        using (var provider = Create(maxFileBytes: 400, maxFiles: 100, pid: 777))
        {
            var logger = provider.CreateLogger("Test");
            for (var i = 0; i < 30; i++)
            {
                logger.LogInformation("Entry {Index}", i);
            }

            logger.LogError("last words before the crash");
        }

        // The marker records a start time slightly after the log file name.
        var tail = RollingFileLoggerProvider.ReadSessionTail(
            _directory, 777, SessionStart.AddSeconds(1), maxChars: 600);

        Assert.NotNull(tail);
        Assert.True(tail.Length <= 600);
        Assert.EndsWith("last words before the crash" + Environment.NewLine, tail);
        Assert.Contains("Entry 29", tail);
        Assert.DoesNotContain("Entry 0" + Environment.NewLine, tail);
    }

    [Fact]
    public void ReadSessionTail_PicksSessionClosestToStartForReusedPid()
    {
        using (var earlier = Create(start: SessionStart.AddDays(-2), pid: 55))
        {
            earlier.CreateLogger("Test").LogInformation("earlier session");
        }

        using (var later = Create(start: SessionStart, pid: 55))
        {
            later.CreateLogger("Test").LogInformation("crashed session");
        }

        var tail = RollingFileLoggerProvider.ReadSessionTail(_directory, 55, SessionStart, 10_000);

        Assert.NotNull(tail);
        Assert.Contains("crashed session", tail);
        Assert.DoesNotContain("earlier session", tail);
    }

    [Fact]
    public void ReadSessionTail_ReturnsNullWhenNoFilesForSession()
    {
        Assert.Null(RollingFileLoggerProvider.ReadSessionTail(_directory, 1, SessionStart, 100));

        using (var provider = Create(pid: 2))
        {
            provider.CreateLogger("Test").LogInformation("other session");
        }

        Assert.Null(RollingFileLoggerProvider.ReadSessionTail(_directory, 1, SessionStart, 100));
    }

    [Fact]
    public void PruningKeepsFilesOfOtherLiveSessions()
    {
        Directory.CreateDirectory(_directory);
        var live = Path.Combine(_directory, "viewer-20261020T080000Z-100-000.log");
        var dead = Path.Combine(_directory, "viewer-20261021T080000Z-200-000.log");
        File.WriteAllText(live, "live");
        File.WriteAllText(dead, "dead");
        File.SetLastWriteTimeUtc(live, SessionStart.AddDays(-5));
        File.SetLastWriteTimeUtc(dead, SessionStart.AddDays(-4));

        // Every session counts as alive, including this one: its own older
        // files must still be pruned.
        using (var provider = Create(maxFileBytes: 500, maxFiles: 3, isSessionAlive: (pid, _) => pid != 200))
        {
            WriteMany(provider, "Entry", 200);
        }

        Assert.True(File.Exists(live));
        Assert.False(File.Exists(dead));
        var files = LogFiles();
        Assert.Equal(3, files.Length);
        Assert.Contains("Entry 199 ", string.Concat(files.Select(File.ReadAllText)));
    }

    [Fact]
    public void LiveSessionsBeyondCapAreAllKept()
    {
        Directory.CreateDirectory(_directory);
        for (var pid = 1; pid <= 4; pid++)
        {
            File.WriteAllText(Path.Combine(_directory, $"viewer-20261020T080000Z-{pid}-000.log"), "live");
        }

        using (var provider = Create(maxFileBytes: 500, maxFiles: 3, isSessionAlive: (_, _) => true))
        {
            WriteMany(provider, "Entry", 50);
        }

        // Four live sessions plus this session's current file.
        Assert.Equal(5, LogFiles().Length);
    }

    [Fact]
    public void BusyInstanceDoesNotDeleteFileOfRunningInstance()
    {
        // The quiet instance runs in this (live) process, with the default
        // liveness check; the busy instance rolls over many times meanwhile.
        using var quiet = new RollingFileLoggerProvider(_directory, LogLevel.Information);
        var quietLogger = quiet.CreateLogger("Quiet");
        quietLogger.LogInformation("quiet before");
        SpinWait.SpinUntil(() => quiet.CurrentFilePath is { } p && File.Exists(p), TimeSpan.FromSeconds(5));
        var quietPath = quiet.CurrentFilePath;
        Assert.NotNull(quietPath);
        File.SetLastWriteTimeUtc(quietPath, DateTime.UtcNow.AddDays(-1));

        using (var busy = new RollingFileLoggerProvider(_directory, LogLevel.Information, 500, 3, processId: 999_999))
        {
            WriteMany(busy, "Busy", 200);
        }

        quietLogger.LogInformation("quiet after");
        quiet.Dispose();

        Assert.True(File.Exists(quietPath));
        var text = File.ReadAllText(quietPath);
        Assert.Contains("quiet before", text);
        Assert.Contains("quiet after", text);
        Assert.Equal(3, LogFiles().Length);
    }

    [Fact]
    public void DefaultLivenessTreatsReusedProcessIdAsDead()
    {
        // This process is running, but it started after this file's session,
        // so the file belongs to an earlier process that reused the id.
        Directory.CreateDirectory(_directory);
        var reused = Path.Combine(_directory, $"viewer-20200101T000000Z-{Environment.ProcessId}-000.log");
        File.WriteAllText(reused, "old");
        File.SetLastWriteTimeUtc(reused, DateTime.UtcNow.AddDays(-1));

        using (var provider = new RollingFileLoggerProvider(_directory, LogLevel.Information, 500, 1, processId: 999_998))
        {
            provider.CreateLogger("Test").LogInformation("new session");
        }

        Assert.False(File.Exists(reused));
    }

    [Fact]
    public void UnreportedCrashedSessionFileSurvivesBusyNeighbour()
    {
        var crashStart = SessionStart.AddHours(-1);
        var older = WriteCrashedFile(300, crashStart, 0, "early lines", TimeSpan.FromHours(2));
        var newest = WriteCrashedFile(300, crashStart, 1, "last words before the crash", TimeSpan.FromHours(1));
        var crashes = new[] { Crash(300, crashStart) };

        using (var busy = Create(maxFileBytes: 500, maxFiles: 3, unreportedCrashes: () => crashes))
        {
            WriteMany(busy, "Busy", 200);
        }

        // Only the crashed session's newest file is kept, and it counts
        // towards the cap.
        Assert.True(File.Exists(newest));
        Assert.False(File.Exists(older));
        Assert.Equal(3, LogFiles().Length);
        Assert.Contains("Busy 199 ", string.Concat(LogFiles().Select(File.ReadAllText)));

        var tail = RollingFileLoggerProvider.ReadSessionTail(_directory, 300, crashes[0].StartedUtc, 1_000);
        Assert.Equal("last words before the crash", tail);
    }

    [Fact]
    public void CrashedSessionFileIsPrunedOnceReported()
    {
        var crashStart = SessionStart.AddHours(-1);
        var newest = WriteCrashedFile(300, crashStart, 0, "last words", TimeSpan.FromHours(1));
        IEnumerable<PreviousSession> crashes = [Crash(300, crashStart)];

        using (var busy = Create(maxFileBytes: 500, maxFiles: 3, unreportedCrashes: () => crashes))
        {
            WriteMany(busy, "Before", 100);
        }

        Assert.True(File.Exists(newest));

        // The next launch read the tail and removed the marker.
        crashes = [];
        using (var busy = Create(maxFileBytes: 500, maxFiles: 3, start: SessionStart.AddMinutes(1), pid: 4243,
                   unreportedCrashes: () => crashes))
        {
            WriteMany(busy, "After", 100);
        }

        Assert.False(File.Exists(newest));
        Assert.Equal(3, LogFiles().Length);
    }

    [Fact]
    public void KeepsAtMostMaxCrashLogsNewestFirst()
    {
        var crashes = new List<PreviousSession>();
        var paths = new List<string>();
        for (var i = 0; i < RollingFileLoggerProvider.MaxCrashLogs + 2; i++)
        {
            var start = SessionStart.AddHours(-10 + i);
            paths.Add(WriteCrashedFile(300 + i, start, 0, $"crash {i}", TimeSpan.FromHours(10 - i)));
            crashes.Add(Crash(300 + i, start));
        }

        using (var busy = Create(maxFileBytes: 500, maxFiles: 2, unreportedCrashes: () => crashes))
        {
            WriteMany(busy, "Busy", 200);
        }

        // The newest crashes are kept, beyond the cap; the oldest go.
        var kept = paths.TakeLast(RollingFileLoggerProvider.MaxCrashLogs).ToArray();
        Assert.All(kept, p => Assert.True(File.Exists(p)));
        Assert.All(paths.Except(kept), p => Assert.False(File.Exists(p)));
        Assert.Equal(RollingFileLoggerProvider.MaxCrashLogs + 1, LogFiles().Length);
    }

    [Fact]
    public void DoesNotKeepCrashLogOlderThanMaxAge()
    {
        var crashStart = SessionStart.AddDays(-10);
        var stale = WriteCrashedFile(300, crashStart, 0, "old crash",
            RollingFileLoggerProvider.MaxCrashLogAge + TimeSpan.FromDays(1));
        var crashes = new[] { Crash(300, crashStart) };

        using (var busy = Create(maxFileBytes: 500, maxFiles: 3, unreportedCrashes: () => crashes))
        {
            WriteMany(busy, "Busy", 200);
        }

        Assert.False(File.Exists(stale));
        Assert.Equal(3, LogFiles().Length);
    }

    [Fact]
    public void CrashLogMatchesSessionClosestToMarkerForReusedPid()
    {
        var earlier = WriteCrashedFile(300, SessionStart.AddDays(-2), 0, "earlier", TimeSpan.FromDays(2));
        var crashed = WriteCrashedFile(300, SessionStart.AddHours(-1), 0, "crashed", TimeSpan.FromHours(1));
        var crashes = new[] { Crash(300, SessionStart.AddHours(-1)) };

        using (var busy = Create(maxFileBytes: 500, maxFiles: 3, unreportedCrashes: () => crashes))
        {
            WriteMany(busy, "Busy", 200);
        }

        Assert.True(File.Exists(crashed));
        Assert.False(File.Exists(earlier));
    }

    [Fact]
    public void KeepsLiveSessionsAndUnreportedCrashTogether()
    {
        var live = WriteCrashedFile(100, SessionStart.AddDays(-5), 0, "live", TimeSpan.FromDays(5));
        var dead = WriteCrashedFile(200, SessionStart.AddDays(-4), 0, "dead", TimeSpan.FromDays(4));
        var crashed = WriteCrashedFile(300, SessionStart.AddHours(-1), 0, "crashed", TimeSpan.FromHours(1));
        var crashes = new[] { Crash(300, SessionStart.AddHours(-1)) };

        using (var busy = Create(maxFileBytes: 500, maxFiles: 3, isSessionAlive: (pid, _) => pid == 100,
                   unreportedCrashes: () => crashes))
        {
            WriteMany(busy, "Busy", 200);
        }

        Assert.True(File.Exists(live));
        Assert.True(File.Exists(crashed));
        Assert.False(File.Exists(dead));

        // The live file, the crash log and this session's current file.
        Assert.Equal(3, LogFiles().Length);
    }
}
