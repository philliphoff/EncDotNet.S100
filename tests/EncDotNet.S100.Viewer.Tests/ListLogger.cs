using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A logger that keeps every entry it is given, so tests can assert on
/// what was logged and at which level.
/// </summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly object _gate = new();
    private readonly List<ListLoggerEntry> _entries = [];

    public IReadOnlyList<ListLoggerEntry> Entries
    {
        get { lock (_gate) return [.. _entries]; }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var entry = new ListLoggerEntry(logLevel, formatter(state, exception), exception);
        lock (_gate) _entries.Add(entry);
    }
}

/// <summary>One entry recorded by <see cref="ListLogger{T}"/>.</summary>
internal sealed record ListLoggerEntry(LogLevel Level, string Message, Exception? Exception);
