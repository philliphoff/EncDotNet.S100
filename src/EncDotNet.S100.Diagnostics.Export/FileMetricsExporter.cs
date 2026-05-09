using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace EncDotNet.S100.Diagnostics.Export;

/// <summary>
/// An OpenTelemetry <see cref="BaseExporter{T}"/> that serialises
/// <see cref="Metric"/> samples to a newline-delimited JSON file.
/// </summary>
/// <remarks>
/// Each exported metric point becomes one JSON line with structure:
/// <code>{"kind":"metric","name":"…","instrument":"histogram",…}</code>
/// The file is shared with <see cref="FileTelemetryExporter"/> when both
/// are pointed at the same path; the background writer serialises access.
/// </remarks>
public sealed class FileMetricsExporter : BaseExporter<Metric>
{
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 4096);
    private readonly Thread _writerThread;
    private readonly string _path;
    private volatile bool _disposed;

    /// <summary>
    /// Initialises the exporter, opening (or creating) the target file
    /// for appending metric lines.
    /// </summary>
    /// <param name="path">
    /// Absolute or relative path to the <c>.jsonl</c> output file.
    /// If the file already exists (e.g. written by the trace exporter),
    /// metrics are appended.
    /// </param>
    public FileMetricsExporter(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _writerThread = new Thread(WriterLoop)
        {
            Name = "MetricsFileWriter",
            IsBackground = true,
        };
        _writerThread.Start();
    }

    /// <inheritdoc />
    public override ExportResult Export(in Batch<Metric> batch)
    {
        if (_disposed)
            return ExportResult.Failure;

        foreach (var metric in batch)
        {
            foreach (ref readonly var point in metric.GetMetricPoints())
            {
                var tags = new Dictionary<string, string>();
                foreach (var tag in point.Tags)
                {
                    if (tag.Value is not null)
                        tags[tag.Key] = tag.Value.ToString()!;
                }

                string instrumentType;
                object? value = null;
                List<object>? buckets = null;

                switch (metric.MetricType)
                {
                    case MetricType.LongSum:
                        instrumentType = "counter";
                        value = point.GetSumLong();
                        break;
                    case MetricType.DoubleSum:
                        instrumentType = "counter";
                        value = point.GetSumDouble();
                        break;
                    case MetricType.LongGauge:
                        instrumentType = "gauge";
                        value = point.GetGaugeLastValueLong();
                        break;
                    case MetricType.DoubleGauge:
                        instrumentType = "gauge";
                        value = point.GetGaugeLastValueDouble();
                        break;
                    case MetricType.Histogram:
                        instrumentType = "histogram";
                        buckets = [];
                        var sumDouble = point.GetHistogramSum();
                        var countLong = point.GetHistogramCount();
                        if (point.TryGetHistogramMinMaxValues(out var min, out var max))
                        {
                            buckets.Add(new { sum = sumDouble, count = countLong, min, max });
                        }
                        else
                        {
                            buckets.Add(new { sum = sumDouble, count = countLong });
                        }
                        break;
                    default:
                        instrumentType = metric.MetricType.ToString();
                        break;
                }

                var line = JsonSerializer.Serialize(new
                {
                    kind = "metric",
                    name = metric.Name,
                    instrument = instrumentType,
                    unit = metric.Unit,
                    tags,
                    value,
                    buckets,
                }, TelemetryJsonFormat.SerializerOptions);

                _queue.TryAdd(line);
            }
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
        // Append mode so traces (written first by FileTelemetryExporter)
        // are preserved when both share the same file.
        using var writer = new StreamWriter(
            new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read),
            leaveOpen: false);

        foreach (var line in _queue.GetConsumingEnumerable())
        {
            writer.WriteLine(line);
            writer.Flush();
        }
    }
}
