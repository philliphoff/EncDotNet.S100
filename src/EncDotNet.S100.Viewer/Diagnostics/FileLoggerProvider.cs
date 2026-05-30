using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Minimal append-only file logger used to satisfy the viewer's
/// <c>--log-file</c> option. Writes one formatted line per log entry to
/// a single file. Intended for agent/automation runs that need to
/// collect viewer logs from a known location; it is deliberately tiny
/// and dependency-free rather than a full rolling-file implementation.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly LogLevel _minLevel;
    private readonly object _gate = new();

    public FileLoggerProvider(string path, LogLevel minLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _minLevel = minLevel;

        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
        catch
        {
            // Best-effort — logging must never crash the process.
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string line)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Best-effort — swallow IO failures.
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= _provider._minLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("O"))
                .Append(" [").Append(logLevel).Append("] ")
                .Append(_category).Append(": ")
                .Append(message);
            if (exception is not null)
                sb.Append(Environment.NewLine).Append(exception);
            sb.Append(Environment.NewLine);

            _provider.Write(sb.ToString());
        }
    }
}
