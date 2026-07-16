using EncDotNet.S100.Viewer.Diagnostics;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers <see cref="LastErrorTracker"/>: recording from an exception and
/// from a pre-formatted message, and latest-wins semantics.
/// </summary>
public class LastErrorTrackerTests
{
    [Fact]
    public void Current_IsNull_BeforeAnyError()
    {
        var tracker = new LastErrorTracker();
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void Record_Exception_CapturesTypeMessageAndStack()
    {
        var tracker = new LastErrorTracker();
        Exception ex;
        try { throw new InvalidOperationException("kaboom"); }
        catch (Exception caught) { ex = caught; }

        tracker.Record("UIThread.UnhandledException", ex);

        var current = tracker.Current;
        Assert.NotNull(current);
        Assert.Equal("UIThread.UnhandledException", current!.Source);
        Assert.Equal("System.InvalidOperationException", current.ExceptionType);
        Assert.Equal("kaboom", current.Message);
        Assert.Contains("kaboom", current.StackTrace);
    }

    [Fact]
    public void Record_Message_UsesFirstLineAsMessageAndKeepsFullText()
    {
        var tracker = new LastErrorTracker();
        tracker.Record("UnhandledException", "Top line\nstack frame 1\nstack frame 2");

        var current = tracker.Current;
        Assert.NotNull(current);
        Assert.Null(current!.ExceptionType);
        Assert.Equal("Top line", current.Message);
        Assert.Contains("stack frame 2", current.StackTrace);
    }

    [Fact]
    public void Record_LatestWins()
    {
        var tracker = new LastErrorTracker();
        tracker.Record("first", new Exception("one"));
        tracker.Record("second", new Exception("two"));

        Assert.Equal("two", tracker.Current!.Message);
        Assert.Equal("second", tracker.Current.Source);
    }
}
