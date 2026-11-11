using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// <see cref="ILoggerProvider"/> that writes every log entry into a
/// <see cref="LogBuffer"/> (#1080). Filtering is the buffer's own
/// <see cref="LogBuffer.MinimumLevel"/>; <see cref="AddLogBuffer"/> lets every
/// level through the logging pipeline to this provider so that it can.
/// </summary>
internal sealed class LogBufferLoggerProvider(LogBuffer buffer) : ILoggerProvider
{
    private readonly LogBuffer _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

    /// <summary>The buffer entries are written to.</summary>
    public LogBuffer Buffer => _buffer;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new BufferLogger(_buffer, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// Category prefixes the buffer records only at Warning and above, whatever
    /// its own minimum. These are the hosting libraries under the MCP server
    /// and HTTP clients: they log several Information (and more Debug) entries
    /// for every request, so while an agent drives the viewer over MCP they
    /// would push everything else out of the 1,000-entry buffer, and out of the
    /// logs a feedback report carries. Their warnings and errors still show.
    /// The viewer's own MCP messages (<c>McpServerHost</c>) are not affected.
    /// </summary>
    internal static readonly IReadOnlyList<string> QuietCategories =
    [
        "Microsoft.AspNetCore",
        "ModelContextProtocol",
        "System.Net.Http.HttpClient",
    ];

    /// <summary>
    /// Adds a <see cref="LogBufferLoggerProvider"/> for <paramref name="buffer"/>
    /// and opens the pipeline's filter for it to every level, so the buffer's
    /// own minimum, not the global <c>SetMinimumLevel</c>, decides what it keeps,
    /// except that <see cref="QuietCategories"/> stay at Warning and above.
    /// </summary>
    public static void AddLogBuffer(ILoggingBuilder builder, LogBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(buffer);
        builder.AddProvider(new LogBufferLoggerProvider(buffer));
        builder.AddFilter<LogBufferLoggerProvider>(category: null, LogLevel.Trace);
        foreach (var category in QuietCategories)
            builder.AddFilter<LogBufferLoggerProvider>(category, LogLevel.Warning);
    }

    private sealed class BufferLogger(LogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => buffer.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!buffer.IsEnabled(logLevel))
                return;

            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch (Exception ex)
            {
                message = $"(log message could not be formatted: {ex.Message})";
            }
            if (string.IsNullOrEmpty(message) && exception is not null)
                message = exception.Message;

            buffer.Add(logLevel, category, message, exception);
        }
    }
}
