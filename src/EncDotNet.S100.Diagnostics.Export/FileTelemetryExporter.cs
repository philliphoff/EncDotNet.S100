using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using OpenTelemetry;

namespace EncDotNet.S100.Diagnostics.Export;

/// <summary>
/// An OpenTelemetry <see cref="BaseExporter{T}"/> that serialises
/// <see cref="Activity"/> spans to a newline-delimited JSON file.
/// </summary>
/// <remarks>
/// <para>
/// Each exported span becomes one JSON line with structure:
/// <code>{"kind":"span","name":"…","traceId":"…","spanId":"…",…}</code>
/// </para>
/// <para>
/// Thread safety is guaranteed by pushing serialised lines into a
/// <see cref="BlockingCollection{T}"/> that is drained by a dedicated
/// background thread. The file is opened with
/// <see cref="FileShare.Read"/> so external tools can tail the output
/// while the run is in progress.
/// </para>
/// </remarks>
public sealed class FileTelemetryExporter : BaseExporter<Activity>
{
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 4096);
    private readonly Thread _writerThread;
    private readonly string _path;
    private volatile bool _disposed;

    /// <summary>
    /// Initialises the exporter, opening (or creating) the target file
    /// and writing the version-1 header line.
    /// </summary>
    /// <param name="path">
    /// Absolute or relative path to the <c>.jsonl</c> output file.
    /// The file is created if it does not exist; an existing file is
    /// truncated.
    /// </param>
    public FileTelemetryExporter(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;

        // Ensure the directory exists.
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _writerThread = new Thread(WriterLoop)
        {
            Name = "TelemetryFileWriter",
            IsBackground = true,
        };
        _writerThread.Start();

        // Write the schema header as the first line.
        var header = JsonSerializer.Serialize(new
        {
            kind = "header",
            version = TelemetryJsonFormat.SchemaVersion,
            startedAtUtc = DateTime.UtcNow,
        }, TelemetryJsonFormat.SerializerOptions);
        _queue.Add(header);
    }

    /// <inheritdoc />
    public override ExportResult Export(in Batch<Activity> batch)
    {
        if (_disposed)
            return ExportResult.Failure;

        foreach (var activity in batch)
        {
            var tags = new Dictionary<string, string>();
            foreach (var tag in activity.TagObjects)
            {
                if (tag.Value is not null)
                    tags[tag.Key] = tag.Value.ToString()!;
            }

            var line = JsonSerializer.Serialize(new
            {
                kind = "span",
                name = activity.DisplayName,
                traceId = activity.TraceId.ToString(),
                spanId = activity.SpanId.ToString(),
                parentSpanId = activity.ParentSpanId == default
                    ? null
                    : activity.ParentSpanId.ToString(),
                startUnixNs = new DateTimeOffset(activity.StartTimeUtc, TimeSpan.Zero).ToUnixTimeNanoseconds(),
                endUnixNs = new DateTimeOffset(activity.StartTimeUtc + activity.Duration, TimeSpan.Zero).ToUnixTimeNanoseconds(),
                durationMs = activity.Duration.TotalMilliseconds,
                status = activity.Status.ToString(),
                tags,
            }, TelemetryJsonFormat.SerializerOptions);

            _queue.TryAdd(line);
        }

        return ExportResult.Success;
    }

    /// <inheritdoc />
    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        _queue.CompleteAdding();
        _writerThread.Join(timeoutMilliseconds > 0 ? timeoutMilliseconds : 5000);
        return true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _queue.CompleteAdding();
            _writerThread.Join(5000);
            _queue.Dispose();
        }

        base.Dispose(disposing);
    }

    private void WriterLoop()
    {
        using var writer = new StreamWriter(
            new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read),
            leaveOpen: false);

        foreach (var line in _queue.GetConsumingEnumerable())
        {
            writer.WriteLine(line);
            writer.Flush();
        }
    }
}

internal static class DateTimeOffsetExtensions
{
    private static readonly long UnixEpochTicks = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;

    public static long ToUnixTimeNanoseconds(this DateTimeOffset dto)
    {
        return (dto.Ticks - UnixEpochTicks) * 100;
    }
}
