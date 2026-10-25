using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Always-on rolling file logger that keeps a bounded history of viewer
/// logs (by default 5 × 1 MB) under the viewer data directory, so the
/// crash prompt on the next launch can attach the end of the crashed
/// session's log.
/// </summary>
/// <remarks>
/// <para>
/// Each process writes its own <em>session</em> files, named
/// <c>viewer-{startUtc:yyyyMMddTHHmmss}Z-{pid}-{seq}.log</c>, and rolls to
/// the next sequence number when the current file reaches the size cap.
/// Several viewers running side by side therefore never write the same
/// file. Whenever a file is opened, the oldest log files in the directory
/// are deleted until at most the file-count cap remain. Files of another
/// session whose process is still running are never deleted, so the cap
/// can be exceeded by one current file per other running instance.
/// </para>
/// <para>
/// Log calls only format the line and enqueue it; a background thread
/// writes and flushes, so the UI thread never waits on disk I/O. When the
/// queue is full, new entries are dropped rather than blocking the caller.
/// All I/O is best-effort: failures are swallowed so logging can never
/// take the process down.
/// </para>
/// </remarks>
internal sealed partial class RollingFileLoggerProvider : ILoggerProvider
{
    /// <summary>Default size at which a log file is rolled over (1 MB).</summary>
    public const long DefaultMaxFileBytes = 1024 * 1024;

    /// <summary>Default number of log files kept in the directory.</summary>
    public const int DefaultMaxFiles = 5;

    private const string FilePrefix = "viewer-";
    private const string FileExtension = ".log";
    private const string TimestampFormat = "yyyyMMdd'T'HHmmss";
    private const int MaxQueuedEntries = 10_000;

    /// <summary>
    /// Slack when comparing a process's start time with a session start
    /// read from a file name, which is truncated to whole seconds.
    /// </summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private readonly DateTime _sessionStartUtc;
    private readonly int _processId;
    private readonly Func<int, DateTime, bool> _isSessionAlive;
    private readonly string _sessionId;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _writer;
    private int _queued;
    private volatile bool _disposed;

    private FileStream? _stream;
    private string? _currentPath;
    private int _sequence = -1;

    /// <summary>
    /// Creates the provider and starts its background writer thread.
    /// </summary>
    /// <param name="directory">Directory holding the rolling log files.</param>
    /// <param name="minLevel">Lowest level written to the files.</param>
    /// <param name="maxFileBytes">Size at which a file is rolled over.</param>
    /// <param name="maxFiles">Number of log files kept in the directory.</param>
    /// <param name="sessionStartUtc">Session start used in file names;
    /// defaults to now.</param>
    /// <param name="processId">Process id used in file names; defaults to
    /// the current process.</param>
    /// <param name="isSessionAlive">Decides whether another session, given
    /// its process id and start time (UTC), is still running; its files are
    /// then kept. Defaults to a process check that treats a reused process
    /// id as a dead session.</param>
    public RollingFileLoggerProvider(
        string directory,
        LogLevel minLevel,
        long maxFileBytes = DefaultMaxFileBytes,
        int maxFiles = DefaultMaxFiles,
        DateTime? sessionStartUtc = null,
        int? processId = null,
        Func<int, DateTime, bool>? isSessionAlive = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);

        _directory = directory;
        _minLevel = minLevel;
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
        _isSessionAlive = isSessionAlive ?? IsSessionAlive;
        _processId = processId ?? Environment.ProcessId;
        var start = (sessionStartUtc ?? DateTime.UtcNow).ToUniversalTime();
        var stamp = start.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        _sessionStartUtc = DateTime.ParseExact(stamp, TimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        _sessionId = string.Create(CultureInfo.InvariantCulture, $"{stamp}Z-{_processId}");

        _writer = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "Viewer rolling log writer",
        };
        _writer.Start();

        // The service provider is not disposed on exit, so flush what is
        // queued when the process ends normally.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    /// <summary>The file currently being written, or <see langword="null"/>
    /// before the first entry is written.</summary>
    internal string? CurrentFilePath => Volatile.Read(ref _currentPath);

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);

    /// <summary>
    /// Stops the writer after it has written every queued entry (waiting at
    /// most a couple of seconds) and closes the current file.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _signal.Set();
        _writer.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Returns the last <paramref name="maxChars"/> characters logged by a
    /// previous session, identified by its process id and start time, or
    /// <see langword="null"/> when none of its files remain. When several
    /// sessions in <paramref name="directory"/> share the process id (a
    /// reused PID), the one whose start is closest to
    /// <paramref name="startedUtc"/> is used. Never throws.
    /// </summary>
    /// <param name="directory">Directory holding the rolling log files.</param>
    /// <param name="processId">Process id of the session.</param>
    /// <param name="startedUtc">Approximate start of the session (UTC).</param>
    /// <param name="maxChars">Maximum number of trailing characters.</param>
    public static string? ReadSessionTail(string directory, int processId, DateTime startedUtc, int maxChars)
    {
        if (maxChars <= 0 || string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            var started = DateTime.SpecifyKind(startedUtc, DateTimeKind.Utc);
            var files = EnumerateLogFiles(directory)
                .Where(f => f.ProcessId == processId)
                .ToList();
            if (files.Count == 0)
            {
                return null;
            }

            var session = files
                .MinBy(f => Math.Abs((f.SessionStartUtc - started).Ticks))
                ?.SessionStartUtc;
            var sessionFiles = files
                .Where(f => f.SessionStartUtc == session)
                .OrderByDescending(f => f.Sequence);

            // Read newest-first until enough text is collected.
            var parts = new List<string>();
            var length = 0;
            foreach (var file in sessionFiles)
            {
                var text = ReadShared(file.Path);
                parts.Add(text);
                length += text.Length;
                if (length >= maxChars)
                {
                    break;
                }
            }

            parts.Reverse();
            var all = string.Concat(parts);
            return all.Length == 0 ? null : all.Length <= maxChars ? all : all[^maxChars..];
        }
        catch
        {
            return null;
        }
    }

    private void Enqueue(string line)
    {
        if (_disposed)
        {
            return;
        }

        if (Interlocked.Increment(ref _queued) > MaxQueuedEntries)
        {
            Interlocked.Decrement(ref _queued);
            return;
        }

        _queue.Enqueue(line);
        _signal.Set();
    }

    private void WriterLoop()
    {
        while (true)
        {
            _signal.WaitOne(TimeSpan.FromSeconds(1));
            var stopping = _disposed;

            try
            {
                var wrote = false;
                while (_queue.TryDequeue(out var line))
                {
                    Interlocked.Decrement(ref _queued);
                    WriteLine(line);
                    wrote = true;
                }

                if (wrote)
                {
                    _stream?.Flush();
                }
            }
            catch
            {
                // Best-effort — drop the stream and retry with a fresh file.
                CloseStream();
            }

            if (stopping)
            {
                CloseStream();
                return;
            }
        }
    }

    private void WriteLine(string line)
    {
        var bytes = Utf8.GetBytes(line);
        if (_stream is null || (_stream.Length > 0 && _stream.Length + bytes.Length > _maxFileBytes))
        {
            OpenNextFile();
        }

        _stream?.Write(bytes);
    }

    private void OpenNextFile()
    {
        CloseStream();
        Directory.CreateDirectory(_directory);
        _sequence++;
        var path = Path.Combine(_directory,
            string.Create(CultureInfo.InvariantCulture, $"{FilePrefix}{_sessionId}-{_sequence:D3}{FileExtension}"));

        // FileShare.Delete lets another instance prune this file and
        // FileShare.Read lets the next launch read it while it is open.
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        Volatile.Write(ref _currentPath, path);
        Prune(path);
    }

    private void Prune(string currentPath)
    {
        try
        {
            var others = EnumerateLogFiles(_directory)
                .Where(f => !string.Equals(f.Path, currentPath, StringComparison.Ordinal))
                .ToList();

            // Another running instance may still be writing its files; only
            // this session's own older files and dead sessions' files go.
            var liveSessions = others
                .Where(f => !IsOwnSession(f))
                .Select(f => (f.ProcessId, f.SessionStartUtc))
                .Distinct()
                .Where(s => _isSessionAlive(s.ProcessId, s.SessionStartUtc))
                .ToHashSet();
            var kept = others.Count(f => liveSessions.Contains((f.ProcessId, f.SessionStartUtc)));

            var stale = others
                .Where(f => !liveSessions.Contains((f.ProcessId, f.SessionStartUtc)))
                .OrderByDescending(f => SafeLastWriteUtc(f.Path))
                .ThenByDescending(f => f.SessionStartUtc)
                .ThenByDescending(f => f.Sequence)
                .Skip(Math.Max(0, _maxFiles - 1 - kept));
            foreach (var file in stale)
            {
                try
                {
                    File.Delete(file.Path);
                }
                catch
                {
                    // Another instance may hold or have just removed it.
                }
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private bool IsOwnSession(LogFile file) =>
        file.ProcessId == _processId && file.SessionStartUtc == _sessionStartUtc;

    /// <summary>
    /// Whether the session that wrote a log file is still running: a process
    /// with its id exists and started no later than the session did. A
    /// process id reused by a later process therefore counts as dead.
    /// </summary>
    private static bool IsSessionAlive(int processId, DateTime sessionStartUtc)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                return process.StartTime.ToUniversalTime() <= sessionStartUtc + StartTimeTolerance;
            }
            catch
            {
                // Start time unreadable (access denied): keep the files
                // rather than risk deleting a live instance's log.
                return true;
            }
        }
        catch
        {
            // No process with that id is running.
            return false;
        }
    }

    private void CloseStream()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // Best-effort.
        }

        _stream = null;
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    private static string ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Utf8);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static IEnumerable<LogFile> EnumerateLogFiles(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, FilePrefix + "*" + FileExtension))
        {
            var match = FileNamePattern().Match(Path.GetFileName(path));
            if (!match.Success)
            {
                continue;
            }

            if (!DateTime.TryParseExact(match.Groups["start"].Value, TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start)
                || !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                || !int.TryParse(match.Groups["seq"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seq))
            {
                continue;
            }

            yield return new LogFile(path, start, pid, seq);
        }
    }

    [GeneratedRegex(@"^viewer-(?<start>\d{8}T\d{6})Z-(?<pid>\d+)-(?<seq>\d+)\.log$")]
    private static partial Regex FileNamePattern();

    private sealed record LogFile(string Path, DateTime SessionStartUtc, int ProcessId, int Sequence);

    private sealed class RollingFileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= provider._minLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Enqueue(FileLoggerProvider.FormatLine(
                DateTime.Now, logLevel, category, formatter(state, exception), exception));
        }
    }
}
