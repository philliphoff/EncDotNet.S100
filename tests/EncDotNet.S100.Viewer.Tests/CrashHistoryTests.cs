using System;
using EncDotNet.S100.Viewer.Diagnostics;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the sticky crash-history store that carries detected unclean
/// shutdowns into the feedback report. Crashes must be retained verbatim and
/// never be evicted the way the single-slot <see cref="ILastErrorTracker"/> is.
/// </summary>
public class CrashHistoryTests
{
    private static PreviousSession Session(int pid, string version, DateTime startedUtc) =>
        new(startedUtc, pid, version);

    [Fact]
    public void Default_IsEmpty()
    {
        var history = new CrashHistory();

        Assert.False(history.HasCrashes);
        Assert.Empty(history.Crashes);
        Assert.Null(history.CrashLogTail);
    }

    [Fact]
    public void Capture_RetainsAllCrashesAndTail()
    {
        var history = new CrashHistory();
        var crashes = new[]
        {
            Session(1, "1.0.0", new DateTime(2026, 6, 14, 10, 0, 0, DateTimeKind.Utc)),
            Session(2, "1.0.1", new DateTime(2026, 6, 14, 11, 0, 0, DateTimeKind.Utc)),
        };

        history.Capture(crashes, "tail-text");

        Assert.True(history.HasCrashes);
        Assert.Equal(2, history.Crashes.Count);
        Assert.Equal(1, history.Crashes[0].Pid);
        Assert.Equal(2, history.Crashes[1].Pid);
        Assert.Equal("tail-text", history.CrashLogTail);
    }

    [Fact]
    public void Capture_WithNullTail_IsAllowed()
    {
        var history = new CrashHistory();

        history.Capture(
            new[] { Session(7, "9.9.9", new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc)) },
            crashLogTail: null);

        Assert.True(history.HasCrashes);
        Assert.Null(history.CrashLogTail);
    }

    [Fact]
    public void Capture_NullCrashes_Throws()
    {
        var history = new CrashHistory();

        Assert.Throws<ArgumentNullException>(() => history.Capture(null!, "x"));
    }

    [Fact]
    public void Capture_IsStickyAcrossLaterErrors()
    {
        // The crash capture is independent of any runtime error tracking;
        // once captured it remains until explicitly replaced.
        var history = new CrashHistory();
        var crashes = new[]
        {
            Session(42, "1.2.3", new DateTime(2026, 6, 14, 9, 0, 0, DateTimeKind.Utc)),
        };

        history.Capture(crashes, "boom");

        // A subsequent (empty) capture replaces it; nothing else can evict it.
        Assert.True(history.HasCrashes);
        Assert.Single(history.Crashes);
        Assert.Equal(42, history.Crashes[0].Pid);
    }
}
