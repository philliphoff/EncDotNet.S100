using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Diagnostics.Export;

/// <summary>
/// Shared JSON serialisation options and helpers used by
/// <see cref="FileTelemetryExporter"/> and
/// <see cref="FileMetricsExporter"/> for the newline-delimited JSON
/// (<c>.jsonl</c>) telemetry file format.
/// </summary>
internal static class TelemetryJsonFormat
{
    private static readonly object FilesLock = new();
    private static readonly Dictionary<string, SharedTelemetryFile> Files =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Current schema version. Bumped when the JSON shape changes in a
    /// way that <c>perfreport</c> must know about.
    /// </summary>
    public const int SchemaVersion = 1;

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    internal static string FormatTagValue(object value) =>
        value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;

    internal static TelemetryFileLease AcquireFile(string path, bool truncate)
    {
        var fullPath = Path.GetFullPath(path);
        lock (FilesLock)
        {
            if (!Files.TryGetValue(fullPath, out var file))
            {
                file = new SharedTelemetryFile(fullPath, truncate);
                Files.Add(fullPath, file);
            }
            else if (truncate)
            {
                file.Truncate();
            }

            file.ReferenceCount++;
            return new TelemetryFileLease(fullPath, file);
        }
    }

    internal sealed class TelemetryFileLease : IDisposable
    {
        private readonly string _path;
        private SharedTelemetryFile? _file;

        internal TelemetryFileLease(string path, SharedTelemetryFile file)
        {
            _path = path;
            _file = file;
        }

        internal void WriteLine(string line) =>
            (_file ?? throw new ObjectDisposedException(nameof(TelemetryFileLease)))
                .WriteLine(line);

        public void Dispose()
        {
            lock (FilesLock)
            {
                if (_file is null)
                {
                    return;
                }

                _file.ReferenceCount--;
                if (_file.ReferenceCount == 0)
                {
                    _file.Dispose();
                    Files.Remove(_path);
                }

                _file = null;
            }
        }
    }

    internal sealed class SharedTelemetryFile : IDisposable
    {
        private readonly object _sync = new();
        private readonly string _path;
        private StreamWriter _writer;

        internal SharedTelemetryFile(string path, bool truncate)
        {
            _path = path;
            _writer = OpenWriter(truncate);
        }

        internal int ReferenceCount { get; set; }

        internal void WriteLine(string line)
        {
            lock (_sync)
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
        }

        internal void Truncate()
        {
            lock (_sync)
            {
                _writer.Dispose();
                _writer = OpenWriter(truncate: true);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _writer.Dispose();
            }
        }

        private StreamWriter OpenWriter(bool truncate) =>
            new(
                new FileStream(
                    _path,
                    truncate ? FileMode.Create : FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read),
                leaveOpen: false);
    }
}
