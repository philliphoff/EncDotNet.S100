using EncDotNet.S100.Viewer.Diagnostics;
using Microsoft.Extensions.Logging;

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

    [Fact]
    public void Record_Exception_LogsAnError()
    {
        var logger = new ListLogger<LastErrorTracker>();
        var tracker = new LastErrorTracker(logger);
        var ex = new InvalidOperationException("kaboom");

        tracker.Record("UIThread.UnhandledException", ex);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(ex, entry.Exception);
        Assert.Contains("UIThread.UnhandledException", entry.Message, StringComparison.Ordinal);
        Assert.Contains("kaboom", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_Message_LogsAnErrorWithTheFullText()
    {
        var logger = new ListLogger<LastErrorTracker>();
        var tracker = new LastErrorTracker(logger);

        tracker.Record("McpServerHost", "Top line\nstack frame 1");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("McpServerHost", entry.Message, StringComparison.Ordinal);
        Assert.Contains("stack frame 1", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_StillRecords_WhenTheLoggerThrows()
    {
        var tracker = new LastErrorTracker(new ThrowingLogger());

        tracker.Record("source", new Exception("one"));

        Assert.Equal("one", tracker.Current!.Message);
    }

    private sealed class ThrowingLogger : ILogger<LastErrorTracker>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("provider failed");
    }
}
