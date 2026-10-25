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
    /// Adds a <see cref="LogBufferLoggerProvider"/> for <paramref name="buffer"/>
    /// and opens the pipeline's filter for it to every level, so the buffer's
    /// own minimum, not the global <c>SetMinimumLevel</c>, decides what it keeps.
    /// </summary>
    public static void AddLogBuffer(ILoggingBuilder builder, LogBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(buffer);
        builder.AddProvider(new LogBufferLoggerProvider(buffer));
        builder.AddFilter<LogBufferLoggerProvider>(category: null, LogLevel.Trace);
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
