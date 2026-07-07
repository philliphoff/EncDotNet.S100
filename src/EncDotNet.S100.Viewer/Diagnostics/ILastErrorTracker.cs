using System;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Captures the most recently observed unhandled error so the feedback
/// reporter can include it automatically. Implementations are
/// thread-safe — errors may be recorded from any thread (UI thread,
/// task scheduler, <see cref="AppDomain"/> handler).
/// </summary>
internal interface ILastErrorTracker
{
    /// <summary>
    /// The most recent error, or <see langword="null"/> when no error
    /// has been recorded this session.
    /// </summary>
    LastErrorRecord? Current { get; }

    /// <summary>
    /// Records an error from a captured <see cref="Exception"/>.
    /// </summary>
    /// <param name="source">A short label for where the error came from
    /// (e.g. <c>"UIThread.UnhandledException"</c>).</param>
    /// <param name="exception">The exception to record.</param>
    void Record(string source, Exception exception);

    /// <summary>
    /// Records an error from an already-formatted message (used by the
    /// crash-log path, which may only have a string available).
    /// </summary>
    /// <param name="source">A short label for where the error came from.</param>
    /// <param name="message">The error text, typically
    /// <see cref="Exception.ToString"/>.</param>
    void Record(string source, string message);
}

/// <summary>
/// Read-only snapshot of a single recorded error.
/// </summary>
/// <param name="TimestampUtc">When the error was recorded (UTC).</param>
/// <param name="Source">Short origin label.</param>
/// <param name="ExceptionType">The exception's CLR type name, or
/// <see langword="null"/> when only a message was available.</param>
/// <param name="Message">The exception message (first line of the text
/// when only a message was available).</param>
/// <param name="StackTrace">The full exception text / stack trace.</param>
internal sealed record LastErrorRecord(
    DateTime TimestampUtc,
    string Source,
    string? ExceptionType,
    string Message,
    string? StackTrace);
