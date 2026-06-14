using System;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Default <see cref="ILastErrorTracker"/>: keeps only the most recent
/// error in a single volatile field guarded by a lock. Registered as a
/// singleton and fed by the application's global exception handlers (see
/// <c>App.OnFrameworkInitializationCompleted</c>).
/// </summary>
internal sealed class LastErrorTracker : ILastErrorTracker
{
    private readonly object _gate = new();
    private LastErrorRecord? _current;

    /// <inheritdoc />
    public LastErrorRecord? Current
    {
        get { lock (_gate) return _current; }
    }

    /// <inheritdoc />
    public void Record(string source, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var record = new LastErrorRecord(
            TimestampUtc: DateTime.UtcNow,
            Source: string.IsNullOrWhiteSpace(source) ? "(unknown)" : source,
            ExceptionType: exception.GetType().FullName,
            Message: exception.Message,
            StackTrace: exception.ToString());
        lock (_gate) _current = record;
    }

    /// <inheritdoc />
    public void Record(string source, string message)
    {
        var text = message ?? string.Empty;
        // Use the first non-empty line as the short message; keep the
        // whole text as the stack trace so nothing is lost.
        var firstLine = text;
        var newline = text.IndexOf('\n');
        if (newline >= 0)
            firstLine = text[..newline].TrimEnd('\r');

        var record = new LastErrorRecord(
            TimestampUtc: DateTime.UtcNow,
            Source: string.IsNullOrWhiteSpace(source) ? "(unknown)" : source,
            ExceptionType: null,
            Message: firstLine,
            StackTrace: string.IsNullOrWhiteSpace(text) ? null : text);
        lock (_gate) _current = record;
    }
}
